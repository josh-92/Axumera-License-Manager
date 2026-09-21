using System.Drawing;
using System.Windows.Forms;
using Axumera.LicenseManager.Core.Accounts;

namespace Axumera.LicenseManager.Ui;

/// <summary>
/// Shared layout pieces for the first-run and login windows. The window is split
/// by a root TableLayoutPanel into a fixed 250px navy brand column and a 100%
/// content column. Unlike the previous version — which added a Dock.Left panel
/// before a Dock.Fill panel and so overlapped (WinForms lays out docked children
/// in reverse collection order: the Fill panel claimed the entire client rect
/// and the brand column was then placed at the origin on top of it) — a
/// two-column TableLayoutPanel partitions the space deterministically at every
/// DPI/size, so no control can ever sit underneath the brand panel.
/// </summary>
internal static class AuthLayout
{
    /// <summary>The navy brand column (logo + product + tagline), fills its cell.</summary>
    public static Panel BrandPanel(string taglineText)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.DeepNavy };
        var logo = new PictureBox
        {
            Image = Resources.Logo,
            SizeMode = PictureBoxSizeMode.Zoom,
            Location = new Point(16, 24),
            Size = new Size(218, 56),
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
        };
        var product = Theme.Body("License Manager");
        product.ForeColor = Theme.Gold;
        product.Location = new Point(16, 92);
        var tagline = Theme.Caption(taglineText);
        tagline.ForeColor = Color.FromArgb(0xB9, 0xC4, 0xCE);
        tagline.Location = new Point(16, 122);
        tagline.MaximumSize = new Size(220, 0);
        panel.Controls.Add(logo);
        panel.Controls.Add(product);
        panel.Controls.Add(tagline);
        return panel;
    }

    /// <summary>
    /// Root split: column 0 = fixed-width brand, column 1 = content. This
    /// determines a clean geometry for both cells with no dock-order ambiguity.
    /// </summary>
    public static TableLayoutPanel Root(Control brandPanel, Control content)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = Padding.Empty,
            Margin = Padding.Empty,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(brandPanel, 0, 0);
        root.Controls.Add(content, 1, 0);
        return root;
    }

    /// <summary>
    /// Content grid: 120px label column + 100% input column. <c>rows</c> is a
    /// list of cells per visual row:
    /// <list type="bullet">
    /// <item><c>[label, field]</c> — label in column 0, field in column 1;</item>
    /// <item><c>[control]</c> — spans both columns (error/notice text);</item>
    /// <item><c>[null, control]</c> — column 1 only (right-aligned action).</item>
    /// </list>
    /// The final row is a percent-size filler so the button row stays visible no
    /// matter how tall the window becomes (DPI scaling, resize).
    /// </summary>
    public static TableLayoutPanel Grid(Control heading, params Control[][] rows)
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Length + 2,
            Padding = new Padding(36, 36, 36, 24),
            Margin = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rows.Length + 1; i++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(heading, 0, 0);
        grid.SetColumnSpan(heading, 2);

        for (int r = 0; r < rows.Length; r++)
        {
            int row = r + 1;
            var cells = rows[r];
            if (cells.Length == 1)
            {
                grid.Controls.Add(cells[0], 0, row);
                grid.SetColumnSpan(cells[0], 2);
            }
            else
            {
                if (cells[0] is not null)
                {
                    grid.Controls.Add(cells[0], 0, row);
                }

                grid.Controls.Add(cells[1], 1, row);
            }
        }

        return grid;
    }

    /// <summary>Auto-sizing message text for the content grid.</summary>
    public static Label MessageLabel(Color color)
    {
        return new Label
        {
            ForeColor = color,
            Font = Theme.BodyFont,
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 4, 0, 4),
        };
    }
}

/// <summary>
/// First-run screen. Creates the single local administrator account. The
/// account hash (PBKDF2) is stored under %LOCALAPPDATA%\Axumera\LicenseManager
/// with restrictive ACLs; no password is persisted or logged anywhere.
/// </summary>
internal sealed class SetupForm : Form
{
    private readonly AccountService _accounts;
    private readonly TextBox _username = Theme.TextBox(240);
    private readonly TextBox _password = Theme.TextBox(240);
    private readonly TextBox _confirm = Theme.TextBox(240);
    private readonly Label _error = AuthLayout.MessageLabel(Theme.Danger);

    public string Username => _username.Text.Trim();

    public SetupForm(AccountService accounts)
    {
        _accounts = accounts;
        Text = "Axumera License Manager — First Run";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 404);
        Theme.Apply(this);
        _password.PasswordChar = '•';
        _confirm.PasswordChar = '•';

        var create = Theme.GoldButton("Create Administrator Account");
        create.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        create.Margin = new Padding(0, 12, 0, 0);

        var grid = AuthLayout.Grid(
            Theme.Section("Setup administrator"),
            FieldRow(Theme.Body("Username"), _username),
            FieldRow(Theme.Body("Password"), _password),
            FieldRow(Theme.Body("Confirm"), _confirm),
            new Control[] { _error },
            new Control[] { null!, create });

        Controls.Add(AuthLayout.Root(
            AuthLayout.BrandPanel("Create the local administrator account\nto secure this license signing station."),
            grid));

        create.Click += (_, _) => CreateAccount();
        AcceptButton = create;
        Shown += (_, _) => _username.Focus();
    }

    private static Control[] FieldRow(Label label, TextBox field)
    {
        label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        label.Margin = new Padding(0, 4, 6, 8);
        field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 2, 0, 8);
        return new Control[] { label, field };
    }

    private void CreateAccount()
    {
        _error.Text = string.Empty;
        try
        {
            var result = _accounts.CreateInitialAccount(_username.Text, _password.Text, _confirm.Text);
            if (!result.Ok)
            {
                _error.Text = result.Error ?? "The account could not be created.";
                return;
            }
        }
        catch (Exception)
        {
            _error.Text = "The account could not be created. Check that the application data folder is writable and try again.";
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Login gate for the local administrator account.</summary>
internal sealed class LoginForm : Form
{
    private readonly AccountService _accounts;
    private readonly TextBox _username = Theme.TextBox(240);
    private readonly TextBox _password = Theme.TextBox(240);
    private readonly Label _error = AuthLayout.MessageLabel(Theme.Danger);

    public string Username { get; private set; } = string.Empty;

    public LoginForm(AccountService accounts)
    {
        _accounts = accounts;
        Text = "Axumera License Manager — Sign in";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(700, 404);
        Theme.Apply(this);
        _password.PasswordChar = '•';

        var login = Theme.GoldButton("Sign in");
        login.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        login.Margin = new Padding(0, 12, 0, 0);

        var grid = AuthLayout.Grid(
            Theme.Section("Sign in"),
            FieldRow(Theme.Body("Username"), _username),
            FieldRow(Theme.Body("Password"), _password),
            new Control[] { _error },
            new Control[] { null!, login });

        Controls.Add(AuthLayout.Root(
            AuthLayout.BrandPanel("Sign in to access the license\nsigning console."),
            grid));

        login.Click += (_, _) => AttemptLogin();
        AcceptButton = login;
        Shown += (_, _) => _username.Focus();
    }

    private static Control[] FieldRow(Label label, TextBox field)
    {
        label.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        label.Margin = new Padding(0, 4, 6, 8);
        field.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        field.Margin = new Padding(0, 2, 0, 8);
        return new Control[] { label, field };
    }

    private void AttemptLogin()
    {
        _error.Text = string.Empty;
        try
        {
            // Login() is the single real credential check: only a verified
            // username/password pair yields the canonical username. There is no
            // fallback path — every failure (unknown user, wrong password,
            // corrupt/missing store) produces the same result.
            string? canonical = _accounts.Login(_username.Text, _password.Text);
            if (canonical is null)
            {
                _error.Text = "Invalid username or password.";
                return;
            }

            Username = canonical;
        }
        catch (Exception)
        {
            _error.Text = "Sign-in is temporarily unavailable. Check that the application data folder is readable.";
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// Shown when accounts.json exists but cannot be parsed. The file is never
/// overwritten or deleted automatically — the operator decides how to recover.
/// </summary>
internal sealed class CorruptAccountForm : Form
{
    public CorruptAccountForm()
    {
        Text = "Axumera License Manager — Account problem";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 320);
        Theme.Apply(this);

        var brand = AuthLayout.BrandPanel("The local administrator account record\ncannot be read.");
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(32, 28, 32, 24),
            Margin = Padding.Empty,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = Theme.Section("Account record is corrupted");
        var message = Theme.Body(
            "The existing administrator account file could not be read, so sign-in and " +
            "password recovery are disabled. This app never replaces the file " +
            "automatically.\n\n" +
            "Recovery options:\n" +
            "  • Restore the file from a backup, or\n" +
            "  • If no working account exists, delete the file to start first-run " +
            "setup again.\n\n" +
            "Contact the person who manages this station if you did not expect this.");
        message.MaximumSize = new Size(420, 0);
        message.AutoSize = true;

        var close = Theme.GoldButton("Close");
        close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        content.Controls.Add(heading, 0, 0);
        content.Controls.Add(message, 0, 1);
        content.Controls.Add(close, 0, 2);

        Controls.Add(AuthLayout.Root(brand, content));
        AcceptButton = close;
        close.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
    }
}