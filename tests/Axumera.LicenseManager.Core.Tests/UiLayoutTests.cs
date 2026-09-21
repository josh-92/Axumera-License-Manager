using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Windows.Forms;
using Axumera.LicenseManager.Core.Accounts;
using Axumera.LicenseManager.Ui;
using Xunit;

namespace Axumera.LicenseManager.Core.Tests;

/// <summary>
/// The vstest testhost (Microsoft.NET.Test.Sdk) only rolls forward on
/// Microsoft.NETCore.App, so assemblies of the Microsoft.WindowsDesktop.App
/// shared framework (System.Windows.Forms &amp; friends) cannot bind by default.
/// This resolver loads them from the installed WindowsDesktop shared runtime so
/// the WinForms layout tests can run under <c>dotnet test</c>.
/// </summary>
internal static class WindowsDesktopAssemblyResolver
{
    private static int _installed;

    public static void InstallOnce()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        string? sharedDir = FindSharedFrameworkDir();
        if (sharedDir is null)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string path = Path.Combine(sharedDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    private static string? FindSharedFrameworkDir()
    {
        string[] candidateRoots =
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? string.Empty,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "dotnet"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
        };

        foreach (string root in candidateRoots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            string baseDir = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            string? version = Directory.GetDirectories(baseDir)
                .Select(Path.GetFileName)
                .OrderByDescending(v => v, StringComparer.Ordinal)
                .FirstOrDefault();
            if (version is null)
            {
                continue;
            }

            string dir = Path.Combine(baseDir, version);
            if (File.Exists(Path.Combine(dir, "System.Windows.Forms.dll")))
            {
                return dir;
            }
        }

        return null;
    }
}

/// <summary>
/// Layout regression tests for the first-run / login / corrupt-account windows.
/// They prove that no interactive control is clipped, sits underneath the navy
/// brand column (the reported bug), or falls out of visual/tab order — at the
/// base size and after WinForms DPI auto-scaling (125%/150%) and window growth.
/// </summary>
public class UiLayoutTests
{
    private static AccountService NewService()
        => new(Path.Combine(Path.GetTempPath(), "AxumeraLM-Tests", Guid.NewGuid().ToString("N"), "accounts.json"));

    /// <summary>WinForms controls are created/layouted on a single STA thread.</summary>
    private static void RunSta(Action action)
    {
        WindowsDesktopAssemblyResolver.InstallOnce();
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException("UI test failed", failure);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        var stack = new Stack<Control>();
        foreach (Control child in root.Controls)
        {
            stack.Push(child);
        }

        while (stack.Count > 0)
        {
            var control = stack.Pop();
            yield return control;
            foreach (Control child in control.Controls)
            {
                stack.Push(child);
            }
        }
    }

    private static Panel BrandPanel(Form form)
    {
        var brand = Descendants(form).OfType<Panel>()
            .FirstOrDefault(p => p.BackColor.ToArgb() == Theme.DeepNavy.ToArgb());
        Assert.NotNull(brand);
        return brand!;
    }

    /// <summary>Translates a control's bounds into the form's client coordinate space using the
/// managed parent-chain offsets (deterministic; does not depend on window visibility or DPI).</summary>
    private static Rectangle InFormClientSpace(Control form, Control control)
    {
        var origin = Point.Empty;
        for (Control? current = control; current != null && !ReferenceEquals(current, form); current = current.Parent!)
        {
            origin.Offset(current.Location);
        }
        return new Rectangle(origin, control.Size);
    }

    private static void AssertFullyInside(Control form, Control control, string label, bool allowZeroWidth = false)
    {
        var bounds = InFormClientSpace(form, control);
        var client = form.ClientRectangle;
        Assert.True(
            (allowZeroWidth || bounds.Width > 0) && bounds.Height > 0,
            $"{label} has non-empty bounds: {bounds}");
        Assert.True(
            bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= client.Right && bounds.Bottom <= client.Bottom,
            $"{label} ({bounds}) must be fully inside the client area ({client})");
    }

    private static void AssertNoBrandOverlap(Control form, Control control, Panel brand, string label)
    {
        var bounds = InFormClientSpace(form, control);
        var brandBounds = InFormClientSpace(form, brand);
        var overlap = Rectangle.Intersect(bounds, brandBounds);
        Assert.True(overlap.IsEmpty, $"{label} ({bounds}) overlaps the brand panel ({brandBounds})");
    }

    private static void AssertLayout(Form form, string kind, int expectedTextBoxCount, TextBox[] fields, Control action, Control notice)
    {
        form.Handle.ToString();
        form.PerformLayout();

        var brand = BrandPanel(form);
        var client = form.ClientRectangle;
        Assert.True(client.Width >= 600, $"{kind} has a usable client width: {client}");

        foreach (var field in fields)
        {
            AssertFullyInside(form, field, $"{kind} field");
            AssertNoBrandOverlap(form, field, brand, $"{kind} field");
        }

        AssertFullyInside(form, action, $"{kind} action");
        AssertNoBrandOverlap(form, action, brand, $"{kind} action");
        AssertFullyInside(form, notice, $"{kind} notice", allowZeroWidth: true);
        AssertNoBrandOverlap(form, notice, brand, $"{kind} notice");

        // Visual (vertical) order equals tab order: fields top-down, then the action.
        var ordered = fields.OrderBy(f => f.TabIndex).ToArray();
        Assert.Equal(expectedTextBoxCount, fields.Length);
        for (int i = 1; i < ordered.Length; i++)
        {
            Assert.True(ordered[i].Top > ordered[i - 1].Top, $"{kind} field {i} must be below field {i - 1}");
        }

        Assert.True(ordered[^1].Top < action.Top, $"{kind} action must be below the last field");
        Assert.True(action.TabIndex > ordered[^1].TabIndex, $"{kind} tab order ends with the action button");
        Assert.True(notice.TabIndex >= 0, $"{kind} notice label present");
    }

    [Fact]
    public void First_run_setup_layout_is_fully_visible_and_ordered()
    {
        RunSta(() =>
        {
            using var form = new SetupForm(NewService());
            var fields = Descendants(form).OfType<TextBox>().ToArray();
            var action = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Create"));
            var notice = Descendants(form).OfType<Label>().Single(l => l.ForeColor.ToArgb() == Theme.Danger.ToArgb());
            AssertLayout(form, "setup", 3, fields, action, notice);
        });
    }

    [Fact]
    public void Login_layout_is_fully_visible_and_ordered()
    {
        RunSta(() =>
        {
            using var form = new LoginForm(NewService());
            var fields = Descendants(form).OfType<TextBox>().ToArray();
            var action = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Sign in"));
            var notice = Descendants(form).OfType<Label>().Single(l => l.ForeColor.ToArgb() == Theme.Danger.ToArgb());
            AssertLayout(form, "login", 2, fields, action, notice);
        });
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void Setup_layout_survives_winforms_dpi_auto_scaling(float scale)
    {
        RunSta(() =>
        {
            using var form = new SetupForm(NewService());
            form.Handle.ToString();
            form.PerformLayout();
            form.Scale(new SizeF(scale, scale));
            form.PerformLayout();

            var brand = BrandPanel(form);
            Form formRef = form;
            foreach (var field in Descendants(form).OfType<TextBox>())
            {
                AssertFullyInside(formRef, field, "setup field @dpi");
                AssertNoBrandOverlap(formRef, field, brand, "setup field @dpi");
            }

            var action = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Create"));
            AssertFullyInside(formRef, action, "setup action @dpi");
            AssertNoBrandOverlap(formRef, action, brand, "setup action @dpi");
        });
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(1.25f)]
    [InlineData(1.5f)]
    public void Login_layout_survives_winforms_dpi_auto_scaling(float scale)
    {
        RunSta(() =>
        {
            using var form = new LoginForm(NewService());
            form.Handle.ToString();
            form.PerformLayout();
            form.Scale(new SizeF(scale, scale));
            form.PerformLayout();

            var brand = BrandPanel(form);
            Form formRef = form;
            foreach (var field in Descendants(form).OfType<TextBox>())
            {
                AssertFullyInside(formRef, field, "login field @dpi");
                AssertNoBrandOverlap(formRef, field, brand, "login field @dpi");
            }

            var action = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Sign in"));
            AssertFullyInside(formRef, action, "login action @dpi");
            AssertNoBrandOverlap(formRef, action, brand, "login action @dpi");
        });
    }

    [Theory]
    [InlineData(900, 560)]
    [InlineData(1100, 640)]
    public void Setup_layout_adapts_to_a_larger_window(int width, int height)
    {
        RunSta(() =>
        {
            using var form = new SetupForm(NewService());
            form.Handle.ToString();
            form.ClientSize = new Size(width, height);
            form.PerformLayout();

            var brand = BrandPanel(form);
            Form formRef = form;
            var action = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Create"));
            AssertFullyInside(formRef, action, "setup action @resize");
            AssertNoBrandOverlap(formRef, action, brand, "setup action @resize");

            foreach (var field in Descendants(form).OfType<TextBox>())
            {
                AssertFullyInside(formRef, field, "setup field @resize");
            }
        });
    }

    [Fact]
    public void Corrupt_account_screen_is_readable_and_not_overlapped()
    {
        RunSta(() =>
        {
            using var form = new CorruptAccountForm();
            form.Handle.ToString();
            form.PerformLayout();

            var brand = BrandPanel(form);
            Form formRef = form;
            var close = Descendants(form).OfType<Button>().Single(b => b.Text.Contains("Close"));
            AssertFullyInside(formRef, close, "corrupt close");
            AssertNoBrandOverlap(formRef, close, brand, "corrupt close");

            var message = Descendants(form).OfType<Label>()
                .Single(l => l.Text.StartsWith("The existing administrator account file"));
            AssertFullyInside(formRef, message, "corrupt message");
        });
    }
}