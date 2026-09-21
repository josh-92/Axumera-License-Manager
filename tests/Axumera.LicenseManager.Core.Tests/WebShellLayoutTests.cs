using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Axumera.LicenseManager.Core.Accounts;
using Axumera.LicenseManager.Core.Persistence;
using Axumera.LicenseManager.Ui;
using Axumera.LicenseManager.Ui.Web;
using Microsoft.Web.WebView2.Core;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

/// <summary>
/// End-to-end layout regression tests for the WebView2 shell. A real browser
/// host is created in-process (needs the WebView2 Evergreen runtime installed)
/// and drives the actual SPA over the full JSON bridge: the sidebar
/// (232px / 64px) must participate in the page flow so the main viewport always
/// starts exactly at the sidebar's right edge, regardless of collapse state,
/// navigation, or window size.
/// </summary>
public class WebShellLayoutTests
{
    private static string ProbeJs => """
        (() => {
          const sb = document.querySelector('.sidebar').getBoundingClientRect();
          const vp = document.querySelector('.viewport').getBoundingClientRect();
          const nav = performance.getEntriesByType('navigation')[0];
          return {
            h1: ((document.querySelector('.page h1') || {}).textContent || '').trim(),
            navCount: document.querySelectorAll('.nav-item').length,
            collapsed: document.documentElement.classList.contains('collapsed'),
            navType: nav ? nav.type : '?',
            readyState: document.readyState,
            sbLeft: sb.left, sbRight: sb.right, sbWidth: sb.width,
            vpLeft: vp.left, vpRight: vp.right,
            clientWidth: document.documentElement.clientWidth,
          };
        })()
        """;

    private static string WaitSidebarJs(double target) => $$"""
        (() => new Promise((r) => {
          const t = Date.now();
          (function p() {
            const sb = document.querySelector('.sidebar').getBoundingClientRect();
            const vp = document.querySelector('.viewport').getBoundingClientRect();
            const flush = Math.abs(vp.left - sb.right) < 2;
            if ((Math.abs(sb.width - {{target}}) < 2 && flush) || Date.now() - t > 15000) r('ok');
            else setTimeout(p, 20);
          })();
        }))()
        """;

    private const string ToggleJs = "document.getElementById('sidebarToggle').click(); true";

    private static string NavClickJs(string pageId) => $"document.querySelector('.nav-item[data-page={JsonSerializer.Serialize(pageId)}]').click(); true";

    /* ----------------------------- harness ---------------------------- */

    private sealed class Harness : IDisposable
    {
        public WebShellForm Form { get; }

        public Harness() : this(authenticated: true) { }

        public Harness(bool authenticated)
        {
            string dir = Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests-WebShell", Guid.NewGuid().ToString("N"));
            string baseRoot = Path.Combine(dir, "AppData");
            Directory.CreateDirectory(Path.Combine(baseRoot, "Axumera", "LicenseManager"));

            var paths = new AppDataPaths(baseRoot);
            var accounts = new AccountService(paths);
            Assert.True(accounts.CreateInitialAccount("admin", "password123", "password123").Ok);
            var session = new AppSession(paths, accounts, new JsonSettingsStore(paths), new JsonLicenseRecordStore(paths), "admin", authenticated);
            Form = new WebShellForm(session);
        }

        public void Dispose()
        {
            try { Form.Dispose(); } catch { /* best-effort */ }
        }
    }

    private static void RunSta(Action action)
    {
        WindowsDesktopAssemblyResolver.InstallOnce();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Application.ExitThread(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException("WebView2 UI test failed", failure);
        }
    }

    private static void PumpUntil(Func<bool> keepGoing, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        while (keepGoing())
        {
            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"Message pump did not finish within {timeoutMs} ms.");
            }

            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static JsonElement Eval(CoreWebView2 core, string js, int timeoutMs = 45000)
    {
        var task = core.ExecuteScriptAsync(js);
        string? result = null;
        PumpUntil(() =>
        {
            if (!task.IsCompleted)
            {
                return true;
            }

            result = task.GetAwaiter().GetResult();
            return false;
        }, timeoutMs);

        Assert.NotNull(result);
        using var doc = JsonDocument.Parse(result!);
        return doc.RootElement.Clone();
    }

    private static CoreWebView2 ShowAndWaitReady(WebShellForm form)
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.ViewReady += () => ready.TrySetResult(true);
        form.Show();
        PumpUntil(() => !ready.Task.IsCompleted, 120_000);
        return form.Core ?? throw new Xunit.Sdk.XunitException("WebView2 core unavailable (Evergreen runtime not installed?).");
    }

    private static JsonElement Stabilize(CoreWebView2 core, string expectedHeading, string context)
    {
        var stopwatch = Stopwatch.StartNew();
        string last = "";
        while (stopwatch.ElapsedMilliseconds < 30_000)
        {
            var probe = Eval(core, ProbeJs);
            last = $"h1='{probe.GetProperty("h1").GetString()}' nav={probe.GetProperty("navCount").GetInt32()} " +
                $"collapsed={probe.GetProperty("collapsed").GetBoolean()} " +
                $"navType={probe.GetProperty("navType").GetString()} ready={probe.GetProperty("readyState").GetString()}";

            if (probe.GetProperty("h1").GetString() == expectedHeading &&
                probe.GetProperty("navCount").GetInt32() == 6)
            {
                return probe;
            }

            PumpSleep(150);
        }

        throw new TimeoutException($"Stabilize('{expectedHeading}') for '{context}' timed out. Last: {last}");
    }

    private static void PumpSleep(int ms)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void EnsureExpanded(CoreWebView2 core)
    {
        if (Eval(core, ProbeJs).GetProperty("collapsed").GetBoolean())
        {
            Eval(core, ToggleJs);
            Eval(core, WaitSidebarJs(232));
        }
    }

    private static void AssertFlushLayout(JsonElement probe, string context)
    {
        double sbRight = probe.GetProperty("sbRight").GetDouble();
        double vpLeft = probe.GetProperty("vpLeft").GetDouble();
        double clientWidth = probe.GetProperty("clientWidth").GetDouble();

        Assert.True(Math.Abs(vpLeft - sbRight) <= 1.5,
            $"{context}: viewport left ({vpLeft}) must abut sidebar right ({sbRight})");
        Assert.True(sbRight >= -1.5, $"{context}: sidebar has a sane left edge ({sbRight})");
        Assert.True(sbRight <= clientWidth + 1.5, $"{context}: sidebar stays inside the window ({sbRight} vs {clientWidth})");
        Assert.True(probe.GetProperty("vpRight").GetDouble() <= clientWidth + 1.5, $"{context}: viewport stays inside the window");
    }

    private static void AssertSidebarWidth(JsonElement probe, double expected, string context)
        => Assert.True(Math.Abs(probe.GetProperty("sbWidth").GetDouble() - expected) <= 2,
            $"{context}: sidebar width is {probe.GetProperty("sbWidth").GetDouble()} (expected ~{expected})");

    private static void AssertPair(JsonElement probe, string context)
    {
        AssertFlushLayout(probe, context);
        Assert.True(Math.Abs(probe.GetProperty("sbWidth").GetDouble() - (probe.GetProperty("collapsed").GetBoolean() ? 64 : 232)) <= 2,
            $"{context}: sidebar width mismatches collapse state ({probe.GetProperty("sbWidth").GetDouble()})");
    }

    /* ------------------------------- tests ----------------------------- */

    [Fact]
    public void Shell_layout_abuts_viewport_and_toggle_stays_flush()
    {
        RunSta(() =>
        {
            using var harness = new Harness();
            var form = harness.Form;
            var core = ShowAndWaitReady(form);
            EnsureExpanded(core);

            var p = Stabilize(core, "Welcome back, admin", "dashboard expanded");
            AssertPair(p, "dashboard expanded");

            Eval(core, ToggleJs);
            Eval(core, WaitSidebarJs(64));
            PumpSleep(400);
            p = Stabilize(core, "Welcome back, admin", "dashboard collapsed");
            Assert.True(p.GetProperty("collapsed").GetBoolean());
            AssertPair(p, "dashboard collapsed");

            Eval(core, ToggleJs);
            Eval(core, WaitSidebarJs(232));
            PumpSleep(400);
            p = Stabilize(core, "Welcome back, admin", "dashboard re-expanded");
            Assert.False(p.GetProperty("collapsed").GetBoolean());
            AssertPair(p, "dashboard re-expanded");

            form.ClientSize = new Size(1100, 700);
            PumpSleep(400);
            p = Stabilize(core, "Welcome back, admin", "window 1100x700");
            AssertPair(p, "window 1100x700");

            form.ClientSize = new Size(1400, 900);
            PumpSleep(400);
            p = Stabilize(core, "Welcome back, admin", "window 1400x900");
            AssertPair(p, "window 1400x900");

            form.Close();
        });
    }

    [Fact]
    public void Navigation_reaches_all_six_pages_without_overlap()
    {
        RunSta(() =>
        {
            using var harness = new Harness();
            var form = harness.Form;
            var core = ShowAndWaitReady(form);
            EnsureExpanded(core);

            var pages = new (string Id, string Heading)[]
            {
                ("dashboard", "Welcome back, admin"),
                ("generate", "Generate License"),
                ("active", "Active Licenses"),
                ("archived", "Archived Licenses"),
                ("verify", "Verify License"),
                ("settings", "Settings"),
            };

            foreach (var (id, heading) in pages)
            {
                Eval(core, NavClickJs(id), timeoutMs: 90_000);
                var probe = Stabilize(core, heading, "page '" + id + "'");
                AssertPair(probe, "page '" + id + "'");
            }

            form.Close();
        });
    }

    [Fact]
    public void Logout_ends_the_shell_and_returns_to_the_gate()
    {
        RunSta(() =>
        {
            using var harness = new Harness();
            var form = harness.Form;
            var core = ShowAndWaitReady(form);
            EnsureExpanded(core);
            Stabilize(core, "Welcome back, admin", "dashboard");

            Eval(core, "document.querySelector('.btn-logout').click(); true", timeoutMs: 30_000);

            var stopwatch = Stopwatch.StartNew();
            while (!form.LoggedOut && stopwatch.ElapsedMilliseconds < 15_000)
            {
                PumpSleep(50);
            }

            Assert.True(form.LoggedOut, "Logout must mark the shell as signed out.");
            stopwatch.Restart();
            while (!form.IsDisposed && stopwatch.ElapsedMilliseconds < 15_000)
            {
                PumpSleep(50);
            }

            Assert.True(form.IsDisposed, "Logout must close the shell so the login gate re-runs.");
        });
    }

    [Fact]
    public void Unauthenticated_session_renders_lock_gate_not_dashboard()
    {
        RunSta(() =>
        {
            using var harness = new Harness(authenticated: false);
            var form = harness.Form;
            var core = ShowAndWaitReady(form);

            var stopwatch = Stopwatch.StartNew();
            string heading = "";
            while (stopwatch.ElapsedMilliseconds < 30_000)
            {
                heading = Eval(core, ProbeJs).GetProperty("h1").GetString() ?? "";
                if (heading == "Access locked")
                {
                    break;
                }

                PumpSleep(150);
            }

            var gate = Eval(core, ProbeJs);
            Assert.Equal("Access locked", heading);
            Assert.Equal(0, gate.GetProperty("navCount").GetInt32());
            Assert.Equal(0, gate.GetProperty("sbWidth").GetDouble());
            form.Close();
        });
    }
}