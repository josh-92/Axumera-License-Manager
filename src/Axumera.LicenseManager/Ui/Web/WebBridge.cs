using System.Text.Json;
using System.Text.Json.Serialization;
using Axumera.LicenseManager.Core.Accounts;
using Axumera.LicenseManager.Core.Crypto;
using Axumera.LicenseManager.Core.Models;
using Axumera.LicenseManager.Core.Rules;
using Axumera.LicenseManager.Core.Security;
using Axumera.LicenseManager.Core.Versioning;
using WinForms = System.Windows.Forms;

namespace Axumera.LicenseManager.Ui.Web;

/// <summary>
/// JSON-RPC-style bridge between the WebView2 frontend and the C# backend.
/// Every security-sensitive operation (signing, verification, key handling,
/// password changes, persistence, file dialogs) lives here or below — never in
/// JavaScript. The frontend only sends field values and page intent; the bridge
/// replies with plain data DTOs. <c>Dispatch</c> is pure (no WebView2 types are
/// required to call it) so the protocol is fully unit-testable.
/// </summary>
internal sealed class WebBridge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly AppSession _session;
    private readonly Action _onLogout;
    private readonly WinForms.IWin32Window? _owner;

    public WebBridge(AppSession session, Action onLogout, WinForms.IWin32Window? owner = null)
    {
        _session = session;
        _onLogout = onLogout;
        _owner = owner;
    }

    /// <summary>Handles one incoming web message; returns the reply JSON (or null if none is expected).</summary>
    public string? Dispatch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Reply(root, false, "Malformed message.");
            }

            long id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt64()
                : 0;
            string action = root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : string.Empty;
            var payload = root.TryGetProperty("payload", out var p) ? p : default;

            // The shell must never serve content to an unauthenticated session.
            // account.session is the only action that answers before
            // authentication so the frontend can (a) show the lock gate and
            // (b) never reach the dashboard without a verified session.
            if (!_session.Authenticated && action != "account.session")
            {
                return JsonSerializer.Serialize(new { id, ok = false, error = "Not signed in." }, Json);
            }

            var result = RunAction(action, payload);
            return result.Ok
                ? JsonSerializer.Serialize(new { id, ok = true, data = result.Data }, Json)
                : JsonSerializer.Serialize(new { id, ok = false, error = result.Error }, Json);
        }
        catch (JsonException ex)
        {
            return JsonSerializer.Serialize(new { id = 0, ok = false, error = "The request is not valid JSON: " + ex.Message }, Json);
        }
    }

    private (bool Ok, object? Data, string Error) ActionError(string message) => (false, null, message);

    private (bool Ok, object? Data, string Error) RunAction(string action, JsonElement payload)
    {
        try
        {
            return action switch
            {
                "app.state" => AppState(),
                "licenses.list" => (true, LicenseRows(GetBool(payload, "archived")), string.Empty),
                "licenses.get" => LicenseDetails(GetString(payload, "id")),
                "licenses.archive" => StoreAction("archive", GetString(payload, "id")),
                "licenses.restore" => StoreAction("restore", GetString(payload, "id")),
                "licenses.delete" => StoreAction("delete", GetString(payload, "id")),
                "verify.open" => (true, new { path = PickFile(VerifyFileDialog()) }, string.Empty),
                "verify.run" => VerifyRun(GetString(payload, "path"), GetString(payload, "expectedHwid")),
                "generate.review" => GenerateReview(GetString(payload, "school"), GetString(payload, "hwid"), GetString(payload, "expires")),
                "generate.sign" => GenerateSign(GetString(payload, "school"), GetString(payload, "hwid"), GetString(payload, "expires")),
                "key.status" => (true, KeyStatusDto(KeyCheck()), string.Empty),
                "key.browse" => (true, new { path = PickFile(KeyFileDialog()) }, string.Empty),
                "key.save" => KeySave(GetString(payload, "path")),
                "key.restrict" => KeyRestrict(GetString(payload, "path")),
                "account.session" => SessionState(),
                "account.changePassword" => ChangePassword(
                    GetString(payload, "current"),
                    GetString(payload, "new"),
                    GetString(payload, "confirm")),
                "account.logout" => Logout(),
                "shell.openLocation" => OpenLocation(GetString(payload, "path")),
                _ => ActionError($"Unknown action '{action}'."),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ActionError(ex.Message);
        }
    }

    private (bool Ok, object? Data, string Error) AppState()
    {
        var all = _session.Licenses.Load().OrderByDescending(r => r.CreatedUtc).ToList();
        var counts = Counts(all);
        var check = KeyCheck();
        var recent = all
            .Take(8)
            .Select(r => new LicenseRowDto(r, Describe(r)))
            .ToArray();

        return (true, new
        {
            username = _session.Username,
            productName = ProductInfo.ProductName,
            version = ProductInfo.Version,
            licenseFormatVersion = ProductInfo.LicenseFormatVersion,
            fullLabel = ProductInfo.FullLabel,
            key = KeyStatusDto(check),
            counts = new
            {
                active = counts.Active,
                expiringSoon = counts.ExpiringSoon,
                expired = counts.Expired,
                archived = counts.Archived,
            },
            recent,
        }, string.Empty);
    }

    /// <summary>
    /// Session bootstrap for the frontend. Answers before authentication so the
    /// SPA can decide between the venture pages and the lock gate; the dashboard
    /// data itself is never exposed to an unauthenticated session.
    /// </summary>
    private (bool Ok, object? Data, string Error) SessionState()
    {
        string state = _session.Accounts.State switch
        {
            AccountStoreState.NotPresent => "setup",
            AccountStoreState.Corrupt => "corrupt",
            _ => "ready",
        };

        return _session.Authenticated
            ? (true, new { authenticated = true, state, username = _session.Username }, string.Empty)
            : (true, new { authenticated = false, state }, string.Empty);
    }

    private static (int Active, int ExpiringSoon, int Expired, int Archived) Counts(IEnumerable<LicenseRecord> all)
    {
        var list = all.ToList();
        return (
            list.Count(r => !r.IsArchived && Describe(r).Status == LicenseRecordStatus.Active),
            list.Count(r => !r.IsArchived && Describe(r).Status == LicenseRecordStatus.ExpiringSoon),
            list.Count(r => !r.IsArchived && Describe(r).Status == LicenseRecordStatus.Expired),
            list.Count(r => r.IsArchived));
    }

    private object LicenseRows(bool archivedOnly)
    {
        var records = _session.Licenses.Load().Where(r => r.IsArchived == archivedOnly);
        if (archivedOnly)
        {
            records = records.OrderByDescending(r => r.ArchivedUtc ?? r.CreatedUtc);
        }
        else
        {
            records = records.OrderBy(r => r.Expires).ThenBy(r => r.SchoolName, StringComparer.OrdinalIgnoreCase);
        }

        return records.Select(r => new LicenseRowDto(r, Describe(r))).ToArray();
    }

    private (bool, object?, string) LicenseDetails(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return ActionError("License id required.");
        }

        var record = _session.Licenses.Get(id);
        if (record is null)
        {
            return ActionError("The license record no longer exists.");
        }

        LicenseVerificationDto? verification = null;
        if (!string.IsNullOrEmpty(record.LicenseFilePath))
        {
            verification = VerifyFile(record.LicenseFilePath, expectedHwid: null);
        }

        return (true, new
        {
            record = new LicenseRowDto(record, Describe(record)),
            verification,
        }, string.Empty);
    }

    private (bool, object?, string) StoreAction(string action, string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return ActionError("License id required.");
        }

        bool changed = action switch
        {
            "archive" => _session.Licenses.Archive(id),
            "restore" => _session.Licenses.Restore(id),
            "delete" => _session.Licenses.Delete(id),
            _ => false,
        };
        return changed ? (true, new { changed }, string.Empty) : ActionError("The license record no longer exists.");
    }

    private (bool, object?, string) VerifyRun(string? path, string? expectedHwid)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ActionError("Choose a license file first.");
        }

        if (!File.Exists(path))
        {
            return ActionError("The license file does not exist.");
        }

        return (true, VerifyFile(path, string.IsNullOrWhiteSpace(expectedHwid) ? null : expectedHwid), string.Empty);
    }

    private static LicenseVerificationDto VerifyFile(string path, string? expectedHwid)
    {
        using var publicKey = ProductionKeys.CreatePublicKey();
        var result = LicenseVerifier.VerifyFile(path, publicKey, expectedHwid);
        return new LicenseVerificationDto(result);
    }

    private (bool, object?, string) GenerateReview(string school, string hwid, string expires)
    {
        var validation = ValidateInputs(school, hwid, expires, out string normalized, out string dates);
        if (validation is not null)
        {
            return ActionError(validation);
        }

        int remaining = ExpiryRules.RemainingDays(dates);
        var check = KeyCheck();
        return (true, new
        {
            school = school.Trim(),
            normalizedHwid = normalized,
            expires = dates,
            remainingDays = remaining,
            daysLabel = ExpiryRules.DaysLabel(remaining),
            payloadPreview = LicensePayloadWriter.WritePayloadText(school.Trim(), normalized, dates),
            key = KeyStatusDto(check),
        }, string.Empty);
    }

    private (bool, object?, string) GenerateSign(string school, string hwid, string expires)
    {
        var validation = ValidateInputs(school, hwid, expires, out _, out _);
        if (validation is not null)
        {
            return ActionError(validation);
        }

        var check = KeyCheck();
        if (!check.Safe)
        {
            return ActionError(check.Message);
        }

        var signer = new LicenseSigner(new PrivateKeyAccess(_session.Settings.PrivateKeyPath));
        var signResult = signer.SignLicense(school.Trim(), hwid, expires, DateOnly.FromDateTime(DateTime.Now));
        if (!signResult.Ok)
        {
            return ActionError(signResult.Error ?? "The license could not be signed.");
        }

        string suggestedDir = _session.Settings.LastSaveDirectory;
        using var save = new WinForms.SaveFileDialog
        {
            Title = "Save the signed license",
            Filter = "License file (*.lic)|*.lic|All files (*.*)|*.*",
            FileName = "license.lic",
            InitialDirectory = string.IsNullOrEmpty(suggestedDir) || !Directory.Exists(suggestedDir) ? null : suggestedDir,
        };

        if (save.ShowDialog(_owner) != WinForms.DialogResult.OK)
        {
            return (false, null, "Signing cancelled at the Save dialog.");
        }

        try
        {
            File.WriteAllText(save.FileName, signResult.LicenseText!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ActionError("The license could not be saved: " + ex.Message);
        }

        _session.Settings.LastSaveDirectory = Path.GetDirectoryName(save.FileName) ?? string.Empty;
        string normalized = MachineIdNormalizer.Normalize(hwid);
        _session.Licenses.Add(LicenseRecord.Create(school.Trim(), normalized, expires, save.FileName));
        return (true, new { path = save.FileName }, string.Empty);
    }

    private static string? ValidateInputs(string school, string hwid, string expires, out string normalizedHwid, out string dates)
    {
        normalizedHwid = string.Empty;
        dates = string.Empty;

        string cleanSchool = (school ?? string.Empty).Trim();
        if (cleanSchool.Length == 0)
        {
            return "Enter a school name.";
        }

        if (cleanSchool.Length > 150)
        {
            return "School name must be 150 characters or fewer.";
        }

        normalizedHwid = MachineIdNormalizer.Normalize(hwid);
        if (normalizedHwid.Length == 0)
        {
            return "Enter a valid hardware ID.";
        }

        dates = (expires ?? string.Empty).Trim();
        if (!ExpiryRules.TryParseDateOnly(dates, out var expiry) || expiry.ToString("yyyy-MM-dd") != dates)
        {
            return "Select a valid expiration date.";
        }

        string today = DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd");
        if (dates.CompareTo(today) < 0)
        {
            return "Expiration date must be today or later.";
        }

        return null;
    }

    private (bool, object?, string) KeySave(string? path)
    {
        string clean = (path ?? string.Empty).Trim();
        if (clean.Length > 0 && !File.Exists(clean))
        {
            return ActionError("That file does not exist.");
        }

        _session.Settings.PrivateKeyPath = clean;
        _session.NotifySettingsChanged();
        var check = KeyCheck();
        return (true, new
        {
            message = clean.Length == 0 ? "Key path cleared." : "Key path saved.",
            key = KeyStatusDto(check),
        }, string.Empty);
    }

    private (bool, object?, string) KeyRestrict(string? path)
    {
        string clean = (path ?? string.Empty).Trim();
        if (!File.Exists(clean))
        {
            return ActionError("Select the key file first.");
        }

        try
        {
            AclGuard.RestrictFile(clean);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return ActionError("Could not change permissions. Run once as an administrator or fix them in File Properties → Security.");
        }

        var check = KeyCheck();
        return (true, new
        {
            message = "Permissions restricted to the current user, Administrators and SYSTEM.",
            key = KeyStatusDto(check),
        }, string.Empty);
    }

    private (bool, object?, string) ChangePassword(string current, string next, string confirm)
    {
        var result = _session.Accounts.ChangePassword(_session.Username, current ?? string.Empty, next ?? string.Empty, confirm ?? string.Empty);
        return result.Ok
            ? (true, new { message = "Password updated." }, string.Empty)
            : ActionError(result.Error ?? "The password could not be changed.");
    }

    private (bool, object?, string) Logout()
    {
        _onLogout();
        return (true, new { }, string.Empty);
    }

    private (bool, object?, string) OpenLocation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return ActionError("The file is no longer available.");
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
            return (true, new { }, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return ActionError("Could not open the folder.");
        }
    }

    private AclCheckResult KeyCheck()
        => new PrivateKeyAccess(_session.Settings.PrivateKeyPath).Check();

    private object KeyStatusDto(AclCheckResult check) => new
    {
        configured = new PrivateKeyAccess(_session.Settings.PrivateKeyPath).IsConfigured,
        path = _session.Settings.PrivateKeyPath,
        safe = check.Safe,
        warning = check.Warning,
        message = check.Message,
    };

    private static string? PickFile(WinForms.OpenFileDialog dialog)
    {
        using (dialog)
        {
            return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.FileName : null;
        }
    }

    private static WinForms.OpenFileDialog VerifyFileDialog() => new()
    {
        Title = "Select a license",
        Filter = "License file (*.lic)|*.lic|All files (*.*)|*.*",
    };

    private WinForms.OpenFileDialog KeyFileDialog()
    {
        string? dir = null;
        string current = _session.Settings.PrivateKeyPath;
        if (!string.IsNullOrEmpty(current))
        {
            dir = Path.GetDirectoryName(current);
        }

        return new WinForms.OpenFileDialog
        {
            Title = "Select private_key.pem",
            Filter = "PEM key (*.pem)|*.pem|All files (*.*)|*.*",
            InitialDirectory = string.IsNullOrEmpty(dir) || !Directory.Exists(dir) ? null : dir,
        };
    }

    private static string GetString(JsonElement payload, string name)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : string.Empty;

    private static bool GetBool(JsonElement payload, string name)
        => payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    private static string Reply(JsonElement root, bool ok, string error)
    {
        long id = root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number ? idEl.GetInt64() : 0;
        return JsonSerializer.Serialize(new { id, ok, error = error }, Json);
    }

    private static LicenseRecordDescriptor Describe(LicenseRecord record)
    {
        if (record.IsArchived)
        {
            return new LicenseRecordDescriptor(null, LicenseRecordStatus.Archived, "Archived", "—");
        }

        int remaining = ExpiryRules.RemainingDays(record.Expires);
        var status = ExpiryRules.Status(remaining);
        string label = status switch
        {
            LicenseRecordStatus.Active => "Active",
            LicenseRecordStatus.ExpiringSoon => "Expiring soon",
            _ => "Expired",
        };
        return new LicenseRecordDescriptor(remaining, status, label, ExpiryRules.DaysLabel(remaining));
    }

    internal sealed record LicenseRecordDescriptor(int? RemainingDays, LicenseRecordStatus Status, string Label, string DaysLabel);

    /// <summary>JSON DTO for a license ledger row (remaining days computed live, never stored).</summary>
    internal sealed record LicenseRowDto
    {
        public string Id { get; init; } = string.Empty;
        public string SchoolName { get; init; } = string.Empty;
        public string HardwareId { get; init; } = string.Empty;
        public string Expires { get; init; } = string.Empty;
        public string CreatedLocal { get; init; } = string.Empty;
        public int? RemainingDays { get; init; }
        public string Status { get; init; } = string.Empty;
        public string StatusLabel { get; init; } = string.Empty;
        public string DaysLabel { get; init; } = string.Empty;
        public string LicenseFilePath { get; init; } = string.Empty;
        public bool IsArchived { get; init; }

        internal LicenseRowDto(LicenseRecord r, LicenseRecordDescriptor d)
        {
            Id = r.Id;
            SchoolName = r.SchoolName;
            HardwareId = r.HardwareId;
            Expires = r.Expires;
            CreatedLocal = r.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            RemainingDays = d.RemainingDays;
            Status = d.Status switch
            {
                LicenseRecordStatus.Active => "active",
                LicenseRecordStatus.ExpiringSoon => "expiringSoon",
                LicenseRecordStatus.Expired => "expired",
                _ => "archived",
            };
            StatusLabel = d.Label;
            DaysLabel = d.DaysLabel;
            LicenseFilePath = r.LicenseFilePath;
            IsArchived = r.IsArchived;
        }
    }

    internal sealed record LicenseVerificationDto
    {
        public bool Valid { get; init; }
        public string? Error { get; init; }
        public string? SchoolName { get; init; }
        public string? HardwareId { get; init; }
        public string? Expires { get; init; }
        public bool Expired { get; init; }
        public bool? MachineMatches { get; init; }

        public LicenseVerificationDto(LicenseVerificationResult r)
        {
            Valid = r.Verified;
            Error = r.Error;
            SchoolName = r.SchoolName;
            HardwareId = r.HardwareId;
            Expires = r.Expires;
            Expired = r.Expired;
            MachineMatches = r.MachineMatches;
        }
    }
}