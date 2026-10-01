using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Conch.Services;
using Conch.Services.Roles;
using Conch.Utilities;
using Conch.ViewModel;
using Consolonia.Controls;

namespace Conch.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>How often the network is actually sampled, in timer ticks of one second.</summary>
        private const int NetworkSampleSeconds = 5;

        private AppLauncher? _launcher;
        private RoleRegistry? _roles;
        private DispatcherTimer? _statusTimer;
        private bool? _networkUp;

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

            StartStatusArea();
        }

        /// <summary>
        /// Drives the clock and the network indicator.
        /// </summary>
        /// <remarks>
        /// One timer at one second, not two at different rates, and each indicator decides for
        /// itself whether anything actually changed. That is the whole trick: this sits behind
        /// a console redraw loop, where assigning the same string to a TextBlock still costs a
        /// repaint of the desktop, so the clock writes only when the displayed minute turns and
        /// the network is sampled only every fifth tick.
        /// </remarks>
        private void StartStatusArea()
        {
            UpdateClock();
            UpdateNetwork();

            var ticks = 0;
            _statusTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
                {
                    UpdateClock();

                    if (++ticks % NetworkSampleSeconds == 0)
                    {
                        UpdateNetwork();
                    }
                });

            _statusTimer.Start();
            Closed += (_, _) => _statusTimer?.Stop();
        }

        private void UpdateClock()
        {
            var now = DateTime.Now.ToString("HH:mm");
            if (Clock.Text != now)
            {
                Clock.Text = now;
            }
        }

        private async void UpdateNetwork()
        {
            // Off the UI thread: enumerating interfaces measures at about 30ms on Windows,
            // which is roughly two frames of console redraw to answer a question nobody is
            // waiting on.
            var up = await NetworkStatus.IsUpAsync();

            if (_networkUp == up)
            {
                return;
            }

            _networkUp = up;

            // Words, not a glyph. The status area is two items wide and a symbol here would be
            // one more thing that renders as a box on a console without the font for it --
            // which is exactly the failure this project has already been through once.
            NetworkIndicator.Content = up ? "net" : "no net";
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
                // Same rule as the app manager: Settings should not offer, for the text editor,
                // an app that cannot exist on this machine.
                role => App.Tools
                    .Where(t => t.IsAvailableHere && t.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Select(t => new ToolRoleProvider(t, (tool, values) => Apps.LaunchTool(tool, values))),
                role => App.Settings.GetRoleChoice(role));

            registry.RegisterBuiltIn(ShellRoles.AppLauncher, new BuiltInRoleProvider(
                "builtin.launcher", "App Launcher",
                _ => new AppLauncherDialog(App).Show(Windows)));

            registry.RegisterBuiltIn(ShellRoles.AppManager, new BuiltInRoleProvider(
                "builtin.manager", "Software Manager",
                _ => new AppManagerDialog(App).Show(Windows)));

            registry.RegisterBuiltIn(ShellRoles.FileExplorer, new BuiltInRoleProvider(
                "builtin.files", "Files",
                path => new FilesDialog(path, OpenInEditor).Show(Windows)));

            // Unavailable where NetworkManager is not, which is everywhere but Linux. The role
            // then falls back to whatever the catalog offers, exactly as it did before there was
            // a built-in for it.
            registry.RegisterBuiltIn(ShellRoles.NetworkConfig, new BuiltInRoleProvider(
                "builtin.network", "Network",
                _ => new NetworkDialog().Show(Windows),
                () => Nmcli.IsPresent));

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

        private void OnNetworkClicked(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.NetworkConfig);

        private void OnShowNetwork(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.NetworkConfig);

        private void OnShowFiles(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.FileExplorer);

        /// <summary>
        /// Hands a file to whatever serves the text-editor role.
        /// </summary>
        /// <remarks>
        /// Returns false rather than complaining, so the file browser can carry on being a file
        /// browser; the prompt about configuring one is raised here, where Settings is reachable.
        /// </remarks>
        private bool OpenInEditor(string path)
        {
            if (Roles.TryInvoke(ShellRoles.TextEditor, path))
            {
                return true;
            }

            OpenRole(ShellRoles.TextEditor, path);
            return false;
        }

        /// <summary>
        /// Opens whatever serves <paramref name="role"/>, offering Settings when nothing does.
        /// </summary>
        /// <remarks>
        /// Nothing available is an ordinary state, not an error -- audio configuration has no
        /// candidate at all until something is installed for it, and on Conchix there is not yet
        /// an audio stack for anything to configure. Saying so and offering the place to fix it
        /// beats a click that does nothing.
        /// </remarks>
        private async void OpenRole(string role, string? argument = null)
        {
            if (Roles.TryInvoke(role, argument))
            {
                return;
            }

            var name = ShellRoles.DisplayName(role);
            var answer = await MessageBox.ShowDialog(
                name,
                $"No app is set up for {name.ToLowerInvariant()}. Choose one in Settings?",
                MessageBoxStyle.YesNo);

            if (answer == MessageBoxResult.Yes)
            {
                new SettingsDialog(App, Roles).Show(Windows);
            }
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
