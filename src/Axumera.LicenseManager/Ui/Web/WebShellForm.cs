using System.Windows.Forms;
using Axumera.LicenseManager.Core.Versioning;
using Axumera.LicenseManager.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Axumera.LicenseManager.Ui.Web;

/// <summary>
/// The full authenticated application window. A WinForms form hosts a full-window
/// WebView2 that renders the SPA; the form only provides the window frame, icon
/// and sign-out contract. All layout (sidebar + main content) lives in the page.
/// </summary>
internal sealed class WebShellForm : Form
{
    private readonly AppSession _session;
    private readonly WebBridge _bridge;
    private WebView2? _webView;
    private bool _ready;

    /// <summary>true when the operator chose to sign out (the app re-runs the login loop).</summary>
    public bool LoggedOut { get; private set; }

    /// <summary>Test hook: the CoreWebView2 once initialized (null if the runtime is missing).</summary>
    internal CoreWebView2? Core => _webView?.CoreWebView2;

    /// <summary>Test hook: raised once the SPA's first navigation completes.</summary>
    internal event Action? ViewReady;

    public WebShellForm(AppSession session)
    {
        _session = session;
        // Sign-out ends the shell: the flag tells Program.Main to re-run the
        // login gate, and closing the window prevents the SPA reload from
        // re-rendering the dashboard. No in-memory session is left around.
        _bridge = new WebBridge(session, () =>
        {
            LoggedOut = true;
            if (IsHandleCreated)
            {
                BeginInvoke((Action)Close);
            }
            else
            {
                Close();
            }
        }, this);

        Text = ProductInfo.ShortName;
        Icon = Resources.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 600);
        ClientSize = new Size(1280, 800);
        BackColor = Theme.LightGray; // visible only until the web view paints
        ShowInTaskbar = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Name = "webView",
            TabIndex = 0,
        };
        Controls.Add(_webView);

        // Any in-shell settings change (key path) refreshes the page's key widget.
        _session.SettingsChanged += () =>
        {
            if (_ready)
            {
                _webView?.CoreWebView2.PostWebMessageAsJson("{\"kind\":\"event\",\"event\":\"settings.changed\"}");
            }
        };

        Load += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_webView is null)
        {
            return;
        }

        try
        {
            await _webView.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException
            or System.Runtime.InteropServices.COMException)
        {
            ShowRuntimeError();
            return;
        }

        var core = _webView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = true;
        core.WebMessageReceived += OnWebMessage;
        WebResources.Install(core);
        _ready = true;
        core.NavigationCompleted += (_, _) => ViewReady?.Invoke();
        core.Navigate(WebResources.VirtualHost + "/index.html");
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? reply = _bridge.Dispatch(e.TryGetWebMessageAsString());
        if (reply is not null && _webView?.CoreWebView2 is { } core)
        {
            core.PostWebMessageAsJson(reply);
        }
    }

    private void ShowRuntimeError()
    {
        if (_webView is not null)
        {
            Controls.Remove(_webView);
            _webView.Dispose();
            _webView = null;
        }

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.White,
            Padding = new Padding(28),
        };

        var heading = new Label
        {
            Text = "Microsoft Edge WebView2 Runtime is required",
            Font = Theme.TitleFont,
            ForeColor = Theme.DeepNavy,
            AutoSize = true,
        };

        var body = new Label
        {
            Text =
                "The Axumera License Manager dashboard runs on the WebView2 runtime,\r\n" +
                "which is not installed on this machine. Install the Evergreen runtime\r\n" +
                "(https://developer.microsoft.com/microsoft-edge/webview2/) and restart\r\n" +
                "the application.",
            Font = Theme.BodyFont,
            ForeColor = Theme.Muted,
            AutoSize = true,
        };

        var exit = new Button
        {
            Text = "Exit",
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Gold,
            ForeColor = Theme.White,
            Font = Theme.SectionFont,
            Width = 96,
            Height = 34,
            Cursor = Cursors.Hand,
        };
        exit.FlatAppearance.BorderSize = 0;
        exit.Click += (_, _) => Close();

        panel.Controls.Add(exit);
        panel.Controls.Add(body);
        panel.Controls.Add(heading);

        heading.Location = new Point(28, 40);
        body.Location = new Point(28, heading.Bottom + 16);
        exit.Location = new Point(28, body.Bottom + 24);

        Controls.Add(panel);
    }
}