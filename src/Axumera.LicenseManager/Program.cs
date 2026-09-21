using System.Windows.Forms;
using Axumera.LicenseManager.Core.Accounts;
using Axumera.LicenseManager.Core.Persistence;
using Axumera.LicenseManager.Ui;
using Axumera.LicenseManager.Ui.Web;

namespace Axumera.LicenseManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var paths = AppDataPaths.Default;
        paths.EnsureReady();
        var accounts = new AccountService(paths);

        while (true)
        {
            string? username;
            switch (accounts.State)
            {
                case AccountStoreState.NotPresent:
                    using (var setup = new SetupForm(accounts))
                    {
                        if (setup.ShowDialog() != DialogResult.OK)
                        {
                            return;
                        }

                        username = setup.Username;
                    }

                    break;

                case AccountStoreState.Corrupt:
                    using (var corrupt = new CorruptAccountForm())
                    {
                        corrupt.ShowDialog();
                    }

                    return;

                default: // Ready
                    using (var login = new LoginForm(accounts))
                    {
                        if (login.ShowDialog() != DialogResult.OK)
                        {
                            return;
                        }

                        username = login.Username;
                    }

                    break;
            }

            var session = new AppSession(
                paths,
                accounts,
                new JsonSettingsStore(paths),
                new JsonLicenseRecordStore(paths),
                username!,
                authenticated: true);

            using var shell = new WebShellForm(session);
            shell.ShowDialog();
            if (!shell.LoggedOut)
            {
                return;
            }
        }
    }
}