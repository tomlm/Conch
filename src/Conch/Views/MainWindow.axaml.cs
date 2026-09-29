using Avalonia.Controls;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.Services.Roles;
using Conch.Utilities;
using Conch.ViewModel;
using Consolonia.Controls;

namespace Conch.Views
{
    public partial class MainWindow : Window
    {
        private AppLauncher? _launcher;
        private RoleRegistry? _roles;

        public MainWindow()
        {
            InitializeComponent();

            // Powering the machine down is only Conch's business when Conch IS the session.
            // Hidden rather than disabled: a greyed-out Shut Down on a developer's desktop
            // invites a click and then explains nothing.
            var ownsSession = CommandLineOptions.Current.OwnsSession;
            PowerSeparator.IsVisible = ownsSession;
            RestartItem.IsVisible = ownsSession;
            ShutDownItem.IsVisible = ownsSession;
        }

        private AppLauncher Apps => _launcher ??= new AppLauncher(Windows);

        private AppViewModel App => (AppViewModel)this.DataContext!;

        /// <summary>
        /// What serves each role on this machine.
        /// </summary>
        /// <remarks>
        /// Built here rather than alongside the rest of the view model because both halves of
        /// it need the window: a built-in has to be shown on the <c>WindowsPanel</c>, and an
        /// app has to be launched into one.
        /// </remarks>
        private RoleRegistry Roles => _roles ??= BuildRoles();

        private RoleRegistry BuildRoles()
        {
            var registry = new RoleRegistry(
                role => App.Tools
                    .Where(t => t.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Select(t => new ToolRoleProvider(t, (tool, values) => Apps.LaunchTool(tool, values))),
                role => App.Settings.GetRoleChoice(role));

            registry.RegisterBuiltIn(ShellRoles.AppLauncher, new BuiltInRoleProvider(
                "builtin.launcher", "App Launcher",
                _ => new AppLauncherDialog(App).Show(Windows)));

            registry.RegisterBuiltIn(ShellRoles.AppManager, new BuiltInRoleProvider(
                "builtin.manager", "Software Manager",
                _ => new AppManagerDialog(App).Show(Windows)));

            return registry;
        }

        private void OnExit(object sender, RoutedEventArgs e)
        {
            this.Close();

            //var lifetime = Application.Current!.ApplicationLifetime as IControlledApplicationLifetime;
            //lifetime!.Shutdown();
        }

        private void OnShowLauncher(object? sender, RoutedEventArgs e)
        {
            var dialog = new AppLauncherDialog((AppViewModel)this.DataContext!);
            dialog.Show(Windows);
        }

        private void OnManageApps(object? sender, RoutedEventArgs e)
        {
            var dialog = new AppManagerDialog((AppViewModel)this.DataContext!);
            dialog.Show(Windows);
        }

        private void OnShowLog(object? sender, RoutedEventArgs e)
        {
            var dialog = new LogDialog();
            dialog.Show(Windows);
        }

        private void OnShowSettings(object? sender, RoutedEventArgs e)
        {
            var dialog = new SettingsDialog(App, Roles);
            dialog.Show(Windows);
        }

        private void OnNewTerminal(object? sender, RoutedEventArgs e)
        {
            Apps.LaunchShell();
        }

        private async void OnLogout(object? sender, RoutedEventArgs e)
        {
            // Closing the window ends the process, and where Conch is the login shell that
            // returns the tty to the login prompt -- which is what logging out means here.
            if (await Confirm("Logout", "End this session?"))
            {
                this.Close();
            }
        }

        private void OnRestart(object? sender, RoutedEventArgs e)
            => RunPowerAction("Restart", "Restart the machine?", SessionActions.RestartCommand(Host.Current));

        private void OnShutDown(object? sender, RoutedEventArgs e)
            => RunPowerAction("Shut Down", "Shut the machine down?", SessionActions.PowerOffCommand(Host.Current));

        /// <summary>
        /// Confirms, then runs a power command in a window that stays on screen.
        /// </summary>
        /// <remarks>
        /// Visibly, through <see cref="AppLauncher.RunScript"/>, because on Conchix the
        /// command goes through sudo and may ask for a password. A prompt drawn where nobody
        /// can answer it is indistinguishable from a hang, and if the command fails instead,
        /// its reason stays readable.
        /// </remarks>
        private async void RunPowerAction(string title, string question, string? command)
        {
            if (command == null)
            {
                Log.Warning("Session", $"{title} is not supported on {Host.Current}.");
                return;
            }

            if (!await Confirm(title, question))
            {
                return;
            }

            Apps.RunScript(title, command, viaWsl: false);
        }

        private static async Task<bool> Confirm(string title, string question)
            => await MessageBox.ShowDialog(title, question, MessageBoxStyle.YesNo) == MessageBoxResult.Yes;

        private async void OnCustomTerminal(object? sender, RoutedEventArgs e)
        {
            var dialog = new CommandLineDialog();
            var result = await dialog.ShowDialog<bool?>(this);

            if (result == true && !string.IsNullOrWhiteSpace(dialog.CommandLine))
            {
                Apps.LaunchCommandLine(dialog.CommandLine);
            }
        }
    }
}
