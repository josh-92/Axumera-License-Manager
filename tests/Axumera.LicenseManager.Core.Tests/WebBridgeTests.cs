using System.Text.Json;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Persistence;
using Axumera.LicenseManager.Ui;
using Axumera.LicenseManager.Ui.Web;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

/// <summary>
/// Exercises the JSON bridge protocol that the WebView2 frontend talks to.
/// The bridge is pure (no WebView2 control needed), so the whole request â†’
/// reply round trip is testable without a browser. Signing itself is covered
/// indirectly (review/validation/key-status); the actual RSA signing needs the
/// production key and is deliberately excluded from automated tests.
/// </summary>
public class WebBridgeTests
{
    private sealed class Harness : IDisposable
    {
        public string Dir { get; }
        public AccountServiceProxy Accounts { get; }
        public JsonSettingsStore Settings { get; }
        public JsonLicenseRecordStore Licenses { get; }
        public WebBridge Bridge { get; }
        public bool LoggedOut { get; private set; }

        public Harness() : this(authenticated: true) { }

        public Harness(bool authenticated)
        {
            Dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests-WebBridge", Guid.NewGuid().ToString("N"));
            var baseRoot = Path.Combine(Dir, "AppData");
            Directory.CreateDirectory(Path.Combine(baseRoot, "Axumera", "LicenseManager"));

            var paths = new AppDataPaths(baseRoot);
            var accounts = new Core.Accounts.AccountService(paths);
            Assert.True(accounts.CreateInitialAccount("admin", "password123", "password123").Ok);
            Accounts = new AccountServiceProxy(accounts);
            Settings = new JsonSettingsStore(paths);
            Licenses = new JsonLicenseRecordStore(paths);
            var session = new AppSession(paths, accounts, Settings, Licenses, "admin", authenticated);
            Bridge = new WebBridge(session, () => LoggedOut = true, owner: null);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* temp cleanup best-effort */ }
        }
    }

    private sealed class AccountServiceProxy
    {
        private readonly Core.Accounts.AccountService _inner;
        public AccountServiceProxy(Core.Accounts.AccountService inner) => _inner = inner;

        public bool VerifyLogin(string user, string pass) => _inner.VerifyLogin(user, pass);
    }

    private static string Dispatch(WebBridge bridge, string action, string payloadJson = "null")
        => bridge.Dispatch($"{{\"id\":7,\"action\":\"{action}\",\"payload\":{payloadJson}}}") ?? string.Empty;

    private static JsonElement Data(string reply)
    {
        using var doc = JsonDocument.Parse(reply);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean(), "expected ok: " + reply);
        Assert.Equal(7, root.GetProperty("id").GetInt64());
        return root.GetProperty("data").Clone();
    }

    private static string Error(string reply)
    {
        using var doc = JsonDocument.Parse(reply);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        return doc.RootElement.GetProperty("error").GetString() ?? string.Empty;
    }

    private static string Days(int offset)
        => DateOnly.FromDateTime(DateTime.Now).AddDays(offset).ToString("yyyy-MM-dd");

    private static void AddRecord(Harness h, string school, string hwid, int daysFromToday)
        => h.Licenses.Add(LicenseRecord.Create(school, hwid, Days(daysFromToday)));

    private static void Archivate(Harness h, string id) => Assert.True(h.Licenses.Archive(id));

    /* ------------------------------ app.state -------------------------- */

    [Fact]
    public void App_state_reports_username_and_zero_counts()
    {
        using var h = new Harness();
        var data = Data(Dispatch(h.Bridge, "app.state", "null"));

        Assert.Equal("admin", data.GetProperty("username").GetString());
        Assert.Equal("1.0.0", data.GetProperty("version").GetString());
        Assert.Equal("1.0", data.GetProperty("licenseFormatVersion").GetString());
        Assert.Equal(0, data.GetProperty("counts").GetProperty("active").GetInt32());
        Assert.Equal(0, data.GetProperty("counts").GetProperty("expiringSoon").GetInt32());
        Assert.Equal(0, data.GetProperty("counts").GetProperty("expired").GetInt32());
        Assert.Equal(0, data.GetProperty("counts").GetProperty("archived").GetInt32());
        Assert.Empty(data.GetProperty("recent").EnumerateArray());

        var key = data.GetProperty("key");
        Assert.False(key.GetProperty("configured").GetBoolean());
        Assert.Contains("private_key.pem", key.GetProperty("message").GetString());
    }

    /* --------------------------- generate.review ---------------------- */

    [Fact]
    public void Review_normalizes_hwid_and_previews_exact_payload()
    {
        using var h = new Harness();
        var data = Data(Dispatch(h.Bridge, "generate.review",
            JsonSerializer.Serialize(new { school = "  Riverside High School  ", hwid = "ab-Cd_12 34", expires = Days(60) })));

        Assert.Equal("Riverside High School", data.GetProperty("school").GetString());
        Assert.Equal("ABCD1234", data.GetProperty("normalizedHwid").GetString());
        Assert.Equal(Days(60), data.GetProperty("expires").GetString());
        Assert.True(data.GetProperty("remainingDays").GetInt32() > 30);
        Assert.Contains("\"school_name\":\"Riverside High School\"", data.GetProperty("payloadPreview").GetString());
        Assert.Contains("\"hwid\":\"ABCD1234\"", data.GetProperty("payloadPreview").GetString());
    }

    [Fact]
    public void Review_rejects_blank_school_and_invalid_dates()
    {
        using var h = new Harness();
        Assert.Contains("school", Error(Dispatch(h.Bridge, "generate.review", "{\"school\":\"\",\"hwid\":\"ABC\",\"expires\":\"2029-01-01\"}")));
        Assert.Contains("hardware", Error(Dispatch(h.Bridge, "generate.review", "{\"school\":\"S\",\"hwid\":\"!!!\",\"expires\":\"2029-01-01\"}")));
        Assert.Contains("expiration", Error(Dispatch(h.Bridge, "generate.review", "{\"school\":\"S\",\"hwid\":\"ABC\",\"expires\":\"not-a-date\"}")));

        string past = Days(-5);
        Assert.Contains("today or later", Error(Dispatch(h.Bridge, "generate.review",
            JsonSerializer.Serialize(new { school = "S", hwid = "ABC", expires = past }))));
    }

    [Fact]
    public void Review_rejects_overlong_school_name()
    {
        using var h = new Harness();
        string longName = new string('x', 151);
        string reply = Dispatch(h.Bridge, "generate.review", JsonSerializer.Serialize(new { school = longName, hwid = "ABC", expires = Days(10) }));
        Assert.Contains("150", Error(reply));
    }

    /* ------------------------- licenses lifecycle --------------------- */

    [Fact]
    public void License_rows_compute_status_and_remaining_days_live()
    {
        using var h = new Harness();
        AddRecord(h, "Expired School", "AAA111", 0);
        AddRecord(h, "Soon School", "BBB222", 10);
        AddRecord(h, "Active School", "CCC333", 90);

        var data = Data(Dispatch(h.Bridge, "licenses.list", "{\"archived\":false}"));
        var rows = data.EnumerateArray().ToList();

        Assert.Equal(3, rows.Count);
        var byStatus = rows.ToDictionary(r => r.GetProperty("status").GetString()!, r => r);

        Assert.Equal("expired", byStatus["expired"].GetProperty("status").GetString());
        Assert.Equal(0, byStatus["expired"].GetProperty("remainingDays").GetInt32());
        Assert.Equal("0 days", byStatus["expired"].GetProperty("daysLabel").GetString());

        Assert.Equal("expiringSoon", byStatus["expiringSoon"].GetProperty("status").GetString());
        Assert.Equal(10, byStatus["expiringSoon"].GetProperty("remainingDays").GetInt32());

        Assert.Equal("active", byStatus["active"].GetProperty("status").GetString());
        Assert.True(byStatus["active"].GetProperty("remainingDays").GetInt32() > 30);
    }

    [Fact]
    public void Active_list_is_sorted_by_expiry_and_archived_are_separate()
    {
        using var h = new Harness();
        var a = LicenseRecord.Create("Zeta School", "AAA111", Days(200));
        var b = LicenseRecord.Create("Alpha School", "BBB222", Days(5));
        h.Licenses.Add(a);
        h.Licenses.Add(b);

        var active = Data(Dispatch(h.Bridge, "licenses.list", "{\"archived\":false}")).EnumerateArray().ToList();
        Assert.Equal(2, active.Count);
        Assert.Equal("Alpha School", active[0].GetProperty("schoolName").GetString());

        Archivate(h, a.Id);
        var archived = Data(Dispatch(h.Bridge, "licenses.list", "{\"archived\":true}")).EnumerateArray().ToList();
        Assert.Single(archived);
        Assert.Equal("archived", archived[0].GetProperty("status").GetString());
        Assert.True(archived[0].GetProperty("isArchived").GetBoolean());

        var activeAfter = Data(Dispatch(h.Bridge, "licenses.list", "{\"archived\":false}")).EnumerateArray().ToList();
        Assert.Single(activeAfter);
        Assert.Equal("Alpha School", activeAfter[0].GetProperty("schoolName").GetString());

        Assert.True(h.Licenses.Restore(a.Id));
        Assert.Equal(2, Data(Dispatch(h.Bridge, "licenses.list", "{\"archived\":false}")).GetArrayLength());
    }

    [Fact]
    public void Archive_restore_delete_round_trip_through_the_bridge()
    {
        using var h = new Harness();
        var rec = LicenseRecord.Create("Round Trip School", "DDD444", Days(30));
        h.Licenses.Add(rec);

        Assert.True(Data(Dispatch(h.Bridge, "licenses.archive", $"{{\"id\":\"{rec.Id}\"}}")).GetProperty("changed").GetBoolean());
        Assert.True(h.Licenses.Get(rec.Id)!.IsArchived);

        Assert.True(Data(Dispatch(h.Bridge, "licenses.restore", $"{{\"id\":\"{rec.Id}\"}}")).GetProperty("changed").GetBoolean());
        Assert.False(h.Licenses.Get(rec.Id)!.IsArchived);

        Assert.True(Data(Dispatch(h.Bridge, "licenses.delete", $"{{\"id\":\"{rec.Id}\"}}")).GetProperty("changed").GetBoolean());
        Assert.Null(h.Licenses.Get(rec.Id));

        string missing = Dispatch(h.Bridge, "licenses.delete", "{\"id\":\"nope\"}");
        Assert.Contains("no longer exists", Error(missing));
    }

    [Fact]
    public void License_details_return_info_without_a_file()
    {
        using var h = new Harness();
        var rec = LicenseRecord.Create("Detail School", "EEE555", Days(20));
        h.Licenses.Add(rec);

        var data = Data(Dispatch(h.Bridge, "licenses.get", $"{{\"id\":\"{rec.Id}\"}}"));
        Assert.Equal("Detail School", data.GetProperty("record").GetProperty("schoolName").GetString());
        Assert.Equal("EEE555", data.GetProperty("record").GetProperty("hardwareId").GetString());
        Assert.Equal("expiringSoon", data.GetProperty("record").GetProperty("status").GetString());
        // No saved file â†’ no verification payload: the null property is omitted.
        Assert.False(data.TryGetProperty("verification", out _));
    }

    /* ------------------------------- verify ---------------------------- */

    [Fact]
    public void Verify_run_rejects_missing_file_and_reports_invalid_content()
    {
        using var h = new Harness();
        Assert.Contains("does not exist", Error(Dispatch(h.Bridge, "verify.run", "{\"path\":\"X:\\\\nope\\\\x.lic\",\"expectedHwid\":\"\"}")));

        string file = Path.Combine(h.Dir, "garbage.lic");
        File.WriteAllText(file, "not json at all");
        var data = Data(Dispatch(h.Bridge, "verify.run", JsonSerializer.Serialize(new { path = file, expectedHwid = "ABC123" })));
        Assert.False(data.GetProperty("valid").GetBoolean());
        Assert.NotNull(data.GetProperty("error").GetString());
    }

    /* -------------------------------- key ----------------------------- */

    [Fact]
    public void Key_save_requires_existing_file_and_can_clear()
    {
        using var h = new Harness();
        var reply = Dispatch(h.Bridge, "key.save", "{\"path\":\"C:\\\\missing\\\\key.pem\"}");
        Assert.Contains("does not exist", Error(reply));

        string keyPath = Path.Combine(h.Dir, "private_key.pem");
        File.WriteAllText(keyPath, "-----BEGIN PRIVATE KEY-----\ndummy\n-----END PRIVATE KEY-----");

        var ok = Data(Dispatch(h.Bridge, "key.save", JsonSerializer.Serialize(new { path = keyPath })));
        Assert.Equal("Key path saved.", ok.GetProperty("message").GetString());
        Assert.True(ok.GetProperty("key").GetProperty("path").GetString() == keyPath);

        var cleared = Data(Dispatch(h.Bridge, "key.save", "{\"path\":\"\"}"));
        Assert.Equal("Key path cleared.", cleared.GetProperty("message").GetString());
    }

    [Fact]
    public void Key_status_is_not_configured_by_default()
    {
        using var h = new Harness();
        var data = Data(Dispatch(h.Bridge, "key.status", "null"));
        Assert.False(data.GetProperty("configured").GetBoolean());
        Assert.Contains("No signing key", data.GetProperty("message").GetString());
    }

    /* ------------------------------ account --------------------------- */

    [Fact]
    public void Change_password_round_trips_and_blocks_wrong_current()
    {
        using var h = new Harness();
        var wrong = Dispatch(h.Bridge, "account.changePassword", "{\"current\":\"wrong!\",\"new\":\"freshPass1\",\"confirm\":\"freshPass1\"}");
        Assert.Contains("current", Error(wrong));
        Assert.True(h.Accounts.VerifyLogin("admin", "password123"));

        var ok = Dispatch(h.Bridge, "account.changePassword", "{\"current\":\"password123\",\"new\":\"freshPass1\",\"confirm\":\"freshPass1\"}");
        Data(ok);
        Assert.False(h.Accounts.VerifyLogin("admin", "password123"));
        Assert.True(h.Accounts.VerifyLogin("admin", "freshPass1"));
    }

    [Fact]
    public void Logout_invokes_the_host_callback()
    {
        using var h = new Harness();
        Data(Dispatch(h.Bridge, "account.logout", "null"));
        Assert.True(h.LoggedOut);
    }

    [Fact]
    public void Session_reports_authenticated_context()
    {
        using var h = new Harness();
        var data = Data(Dispatch(h.Bridge, "account.session", "null"));
        Assert.True(data.GetProperty("authenticated").GetBoolean());
        Assert.Equal("ready", data.GetProperty("state").GetString());
        Assert.Equal("admin", data.GetProperty("username").GetString());
    }

    [Fact]
    public void Unauthenticated_session_is_reported_and_gates_every_action()
    {
        using var h = new Harness(authenticated: false);

        // account.session still answers so the frontend can show the lock gate.
        var session = Data(Dispatch(h.Bridge, "account.session", "null"));
        Assert.False(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal("ready", session.GetProperty("state").GetString());
        Assert.False(session.TryGetProperty("username", out _));

        // Every protected action must be refused — the dashboard data in
        // particular must never leave the backend for an unauthenticated session.
        string[] refused =
        {
            "app.state", "licenses.list", "generate.review", "generate.sign",
            "key.status", "verify.run", "account.changePassword", "account.logout",
        };
        foreach (string action in refused)
        {
            string error = Error(Dispatch(h.Bridge, action, "null"));
            Assert.Equal("Not signed in.", error);
        }
    }

    [Fact]
    public void App_state_requires_an_authenticated_session()
    {
        using var h = new Harness(authenticated: false);
        Assert.Equal("Not signed in.", Error(Dispatch(h.Bridge, "app.state", "null")));
    }

    /* ------------------------------ misc ------------------------------ */

    [Fact]
    public void Unknown_action_and_malformed_message_are_rejected()
    {
        using var h = new Harness();
        Assert.Contains("Unknown action", Error(Dispatch(h.Bridge, "no.such.action", "null")));

        string? bad = h.Bridge.Dispatch("{ not-json");
        Assert.NotNull(bad);
        using var doc = JsonDocument.Parse(bad!);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
    }
}
