using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Conch.Services;
using Conch.Services.Roles;
using Conch.Utilities;
using Conch.ViewModel;
using Consolonia.Controls;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    public partial class MainWindow : Window
    {
        /// <summary>How often the network is actually sampled, in timer ticks of one second.</summary>
        private const int NetworkSampleSeconds = 5;

        /// <summary>
        /// How long the search box trusts what detection last found before looking again, so
        /// an app installed from a terminal shows up without a probe on every keystroke.
        /// </summary>
        private static readonly TimeSpan AppRecheckInterval = TimeSpan.FromMinutes(1);

        /// <summary>The search popup's width: its 48-column panel and the border round it.</summary>
        private const double SearchPopupWidth = 50;

        private AppLauncher? _launcher;
        private RoleRegistry? _roles;
        private FileOpeners? _openers;
        private ShellSearchViewModel? _search;
        private ManagedWindow? _returnTo;
        private DateTime _appsCheckedAt = DateTime.MinValue;
        private bool _checkingApps;
        private DispatcherTimer? _statusTimer;
        private bool? _networkUp;

        public MainWindow()
        {
            InitializeComponent();

            // Powering the machine down is only Conch's business when Conch IS the session.
            // Hidden rather than disabled: a greyed-out Shut Down on a developer's desktop
            // invites a click and then explains nothing.
            var ownsSession = CommandLineOptions.Current.OwnsSession;
            RestartItem.IsVisible = ownsSession;
            ShutDownItem.IsVisible = ownsSession;
            LogoutItem.Header = ownsSession ? "_Logout" : "E_xit";

            // Tunnelling, on the window: the first handler any key press reaches, whichever
            // window has focus, so a hotkey is the shell's before the app underneath sees it.
            AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);

            SearchPopup.PlacementTarget = SearchButton;
            SearchPopup.Closed += (_, _) => OnSearchClosed();
            SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
            SearchBox.LostFocus += (_, _) => Dispatcher.UIThread.Post(CloseSearchIfFocusLeft);
            SearchResults.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
            SearchResults.Tapped += OnSearchResultTapped;

            Loaded += OnLoaded;
            StartStatusArea();
        }

        private AppLauncher Apps => _launcher ??= new AppLauncher(Windows);

        private AppViewModel App => (AppViewModel)this.DataContext!;

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            SearchArea.DataContext = _search = new ShellSearchViewModel(
                ShellItems, InstalledApps, text => () => Apps.LaunchCommandLine(text));

            // Here rather than in the constructor: the panel's windows live in its template,
            // which exists only once it has been laid out.
            WindowList.Attach(Windows);

            App.Settings.Changed += (_, _) => ApplySettings();
            ApplySettings();
            CheckRoleIndicatorsAsync();
            StartControl();
        }

        /// <summary>Brings the bar up to date with Preferences: icons or words, which indicators.</summary>
        private void ApplySettings()
        {
            ConchMenu.Header = App.Settings.StatusAsText ? "Conch" : "🐚";
            SearchButton.Content = App.Settings.StatusAsText ? "search" : "🔍";
            ShowNetwork();
            UpdateRoleIndicators();
        }

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
                // Same rule as the app manager: Preferences should not offer, for the text
                // editor, an app that cannot exist on this machine.
                role => App.Tools
                    .Where(t => t.IsAvailableHere && t.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
                    .Select(t => new ToolRoleProvider(t, (tool, values) => Apps.LaunchTool(tool, values))),
                role => App.Settings.GetRoleChoice(role),
                role => ShellRoles.PreferredDefault(role, Host.Current));

            registry.RegisterBuiltIn(ShellRoles.AppManager, new BuiltInRoleProvider(
                "builtin.manager", "Software",
                _ => new AppManagerDialog(App).Show(Windows)));

            registry.RegisterBuiltIn(ShellRoles.FileExplorer, new BuiltInRoleProvider(
                "builtin.files", "Files",
                path => new FilesDialog(path, OpenFile, OpenFileWith).Show(Windows)));

            // Unavailable where NetworkManager is not, which is everywhere but Linux. The role
            // then falls back to whatever the catalog offers, exactly as it did before there was
            // a built-in for it.
            registry.RegisterBuiltIn(ShellRoles.NetworkConfig, new BuiltInRoleProvider(
                "builtin.network", "Network",
                _ => new NetworkDialog().Show(Windows),
                () => Nmcli.IsPresent));

            return registry;
        }

        #region Search

        /// <summary>
        /// The shell's own entries in the search box: what used to be menu items.
        /// </summary>
        private IEnumerable<SearchItem> ShellItems()
        {
            var ownsSession = CommandLineOptions.Current.OwnsSession;

            yield return new(SearchItemKind.Shell, "Files", "Browse folders",
                ["explorer", "folders", "directory"], () => OpenRole(ShellRoles.FileExplorer));
            yield return new(SearchItemKind.Shell, "Terminal", "A new shell",
                ["shell", "console", "command prompt", "bash"], () => Apps.LaunchShell());
            yield return new(SearchItemKind.Shell, ShellRoles.DisplayName(ShellRoles.AppManager),
                "Find, install and remove apps", ["install", "apps", "store", "catalog", "manager", "app center"],
                () => OpenRole(ShellRoles.AppManager));

            yield return new(SearchItemKind.Setting, "Network settings", "Wi-Fi and connections",
                ["wifi", "ethernet", "internet"], () => OpenRole(ShellRoles.NetworkConfig));
            yield return new(SearchItemKind.Setting, "Display settings", "Font, size and resolution",
                ["screen", "font", "resolution", "monitor"], () => OpenRole(ShellRoles.DisplayConfig));
            yield return new(SearchItemKind.Setting, "Audio settings", "Volume and devices",
                ["sound", "volume", "speaker"], () => OpenRole(ShellRoles.AudioConfig));
            yield return new(SearchItemKind.Setting, "Preferences", "Default apps, keyboard and appearance",
                ["conch", "settings", "hotkeys", "keyboard", "shortcuts", "theme", "default apps"], () => ShowPreferences());

            yield return new(SearchItemKind.Command, "Conch log", "What Conch has been doing",
                ["log", "errors"], ShowLog);
            yield return new(SearchItemKind.Command, "About Conch", "Version and paths",
                ["version"], () => ShowPreferences(SettingsDialog.AboutTab));
            yield return new(SearchItemKind.Command, ownsSession ? "Logout" : "Exit",
                ownsSession ? "End this session" : "Close Conch", ["quit", "sign out", "log off"], Logout);

            if (ownsSession)
            {
                yield return new(SearchItemKind.Command, "Restart", "Restart the machine", ["reboot"], Restart);
                yield return new(SearchItemKind.Command, "Shut Down", "Turn the machine off", ["power off", "shutdown"], ShutDown);
            }
        }

        private IEnumerable<SearchItem> InstalledApps()
            => App.Tools.Where(t => t.IsInstalled)
                .Select(t => ShellSearchViewModel.ForApp(t, () => LaunchApp(t)));

        private void OnSearchClicked(object? sender, RoutedEventArgs e)
        {
            if (SearchPopup.IsOpen)
            {
                CloseSearch(returnFocus: true);
            }
            else
            {
                OpenSearch();
            }
        }

        /// <summary>Opens the search box and its results, with the cursor in the box.</summary>
        private void OpenSearch()
        {
            if (_search == null)
            {
                return;
            }

            // Where Esc goes back to: the window the user was in.
            if (!SearchPopup.IsOpen)
            {
                _returnTo = Windows.ActiveWindow;
            }

            _search.Reload();
            _search.Refresh();

            // Consolonia centres a Bottom popup on its target, which would put most of the list
            // to the left of a two-column icon; shifting it by half the difference starts it
            // under the icon instead. (BottomEdgeAlignedLeft would say this directly, and does
            // not draw at all.)
            SearchPopup.HorizontalOffset = Math.Max(0, (SearchPopupWidth - SearchButton.Bounds.Width) / 2);
            SearchPopup.IsOpen = true;

            // The box lives in the popup, which has only just been put on screen.
            Dispatcher.UIThread.Post(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }, DispatcherPriority.Input);

            CheckAppsAsync();
        }

        /// <summary>
        /// A fresh search next time, however this one ended: Esc, a result chosen, or a click
        /// elsewhere dismissing it.
        /// </summary>
        private void OnSearchClosed()
        {
            if (_search != null)
            {
                _search.Query = string.Empty;
            }
        }

        /// <summary>
        /// Looks for installed apps when nothing has looked recently, and refreshes the
        /// results when it is done.
        /// </summary>
        /// <remarks>
        /// What the Apps window did on every open. Detection does not clear what it already
        /// knows while it probes, so the list never empties and refills.
        /// </remarks>
        private async void CheckAppsAsync()
        {
            if (_checkingApps || DateTime.UtcNow - _appsCheckedAt < AppRecheckInterval)
            {
                return;
            }

            _checkingApps = true;
            try
            {
                await ToolDetector.RefreshAsync(App.Tools.Where(t => t.IsAvailableHere));
                _appsCheckedAt = DateTime.UtcNow;
            }
            finally
            {
                _checkingApps = false;
            }

            if (SearchPopup.IsOpen)
            {
                _search?.Reload();
                _search?.Refresh();
            }

            UpdateRoleIndicators();
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                case Key.Up:
                    MoveSelection(e.Key == Key.Down ? 1 : -1);
                    e.Handled = true;
                    break;

                case Key.Enter:
                    InvokeSearchResult();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CloseSearch(returnFocus: true);
                    e.Handled = true;
                    break;
            }
        }

        private void MoveSelection(int step)
        {
            if (_search == null || _search.Results.Count == 0)
            {
                return;
            }

            SearchPopup.IsOpen = true;
            var index = _search.SelectedResult == null ? -1 : _search.Results.IndexOf(_search.SelectedResult);
            index = Math.Clamp(index + step, 0, _search.Results.Count - 1);
            _search.SelectedResult = _search.Results[index];
            SearchResults.ScrollIntoView(index);
        }

        private void OnSearchResultTapped(object? sender, TappedEventArgs e)
        {
            // Only a tap on a result; the list's scroll bar is in the same control.
            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) != null)
            {
                InvokeSearchResult();
            }
        }

        private void InvokeSearchResult()
        {
            var item = _search?.SelectedResult ?? _search?.Results.FirstOrDefault();
            if (item == null)
            {
                return;
            }

            CloseSearch(returnFocus: false);
            item.Invoke();

            // A launched app takes focus when its window opens. Something that opens nothing --
            // a confirmation answered No -- would otherwise leave the cursor in the bar.
            Dispatcher.UIThread.Post(() =>
            {
                if (SearchBox.IsKeyboardFocusWithin)
                {
                    ReturnFocus();
                }
            }, DispatcherPriority.Background);
        }

        private void CloseSearch(bool returnFocus)
        {
            SearchPopup.IsOpen = false;

            if (returnFocus)
            {
                ReturnFocus();
            }
        }

        private void ReturnFocus()
        {
            var window = _returnTo != null && Windows.Windows.Contains(_returnTo) ? _returnTo : null;
            if (window == null)
            {
                Windows.EnsureActiveWindow();
                window = Windows.ActiveWindow;
            }

            if (window != null)
            {
                window.Activate();
                window.FocusContent();
            }
            else
            {
                // Nothing open: the desktop itself, so the next key does not land in the bar.
                Windows.Focus();
            }
        }

        /// <summary>Closes the results once focus has gone somewhere other than the search.</summary>
        private void CloseSearchIfFocusLeft()
        {
            var focused = FocusManager?.GetFocusedElement() as Visual;
            if (SearchBox.IsKeyboardFocusWithin || focused == SearchResults
                || (focused != null && SearchResults.IsVisualAncestorOf(focused)))
            {
                return;
            }

            SearchPopup.IsOpen = false;
        }

        /// <summary>
        /// Starts an app, asking for its arguments first when its registration has any.
        /// </summary>
        private async void LaunchApp(ToolViewModel tool)
        {
            // Only interrupt for a prompt when the registration declares placeholders; most
            // apps take none and should start on the first keystroke.
            if (!ArgumentTemplate.RequiresPrompt(tool.Args))
            {
                Apps.LaunchTool(tool);
                return;
            }

            var dialog = new ArgumentsDialog(tool);
            if (await dialog.ShowDialog<bool?>(this) == true)
            {
                Apps.LaunchTool(tool, dialog.Values);
            }
        }

        #endregion

        #region Hotkeys

        private void OnShellKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not AppViewModel app)
            {
                return;
            }

            // Preferences is recording a new binding: the press is the binding, not a command.
            if (app.Hotkeys.Capture is { } capture)
            {
                capture(e.Key, e.KeyModifiers);
                e.Handled = true;
                return;
            }

            var action = app.Hotkeys.ActionFor(e.Key, e.KeyModifiers);
            if (action == null)
            {
                return;
            }

            e.Handled = true;
            RunHotkey(action);
        }

        private void RunHotkey(string action)
        {
            switch (action)
            {
                case Hotkeys.FocusSearch:
                    OpenSearch();
                    break;

                case Hotkeys.OpenMenu:
                    ConchMenu.Focus();
                    ConchMenu.Open();
                    break;

                case Hotkeys.NewTerminal:
                    Apps.LaunchShell();
                    break;

                case Hotkeys.Files:
                    OpenRole(ShellRoles.FileExplorer);
                    break;

                case Hotkeys.MaximizeWindow:
                    if (Windows.ActiveWindow is { } window)
                    {
                        window.WindowState = window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                    }
                    break;
            }
        }

        #endregion

        #region Status area

        /// <summary>
        /// Drives the clock and the indicators.
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
                        UpdateRoleIndicators();
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
            ShowNetwork();
        }

        /// <remarks>
        /// A glyph by default, words when Preferences says so: on a console whose font lacks
        /// the glyph it draws as a box, which is exactly the failure this project has already
        /// been through once. Conchix ships Symbola for these, and Windows Terminal falls back
        /// to Segoe UI Symbol.
        /// </remarks>
        private void ShowNetwork()
        {
            if (_networkUp is not { } up)
            {
                return;
            }

            var asText = DataContext is AppViewModel app && app.Settings.StatusAsText;
            SetIfChanged(NetworkIndicator, asText ? (up ? "net" : "no net") : (up ? "⇅" : "⊘"));
            ToolTip.SetTip(NetworkIndicator, up ? "Network: connected" : "Network: not connected");
        }

        /// <summary>Shows the Audio and Display indicators when something serves their role.</summary>
        private void UpdateRoleIndicators()
        {
            if (DataContext is not AppViewModel app)
            {
                return;
            }

            var asText = app.Settings.StatusAsText;
            ShowIndicator(AudioIndicator, ShellRoles.AudioConfig, asText ? "audio" : "♪");
            ShowIndicator(DisplayIndicator, ShellRoles.DisplayConfig, asText ? "display" : "▭");
        }

        private void ShowIndicator(Button indicator, string role, string content)
        {
            var visible = Roles.Resolve(role) != null;
            if (indicator.IsVisible != visible)
            {
                indicator.IsVisible = visible;
            }

            SetIfChanged(indicator, content);
        }

        private static void SetIfChanged(ContentControl control, string content)
        {
            if (!Equals(control.Content, content))
            {
                control.Content = content;
            }
        }

        /// <summary>
        /// Looks for the apps behind the indicators, which until now nothing had looked for.
        /// </summary>
        private async void CheckRoleIndicatorsAsync()
        {
            await DetectRoleCandidatesAsync(ShellRoles.AudioConfig);
            await DetectRoleCandidatesAsync(ShellRoles.DisplayConfig);
            UpdateRoleIndicators();
        }

        #endregion

        #region Menu

        private void ShowLog() => new LogDialog().Show(Windows);

        private void OnManageApps(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.AppManager);

        private void OnShowSettings(object? sender, RoutedEventArgs e) => ShowPreferences();

        private void OnShowAbout(object? sender, RoutedEventArgs e) => ShowPreferences(SettingsDialog.AboutTab);

        private async void ShowPreferences(string? tab = null)
        {
            // Preferences shows each role's apps as installed or not, and picks its default from
            // the installed ones, so they have to have been looked for first.
            await DetectRoleCandidatesAsync();
            new SettingsDialog(App, Roles, tab).Show(Windows);
        }

        private void OnShowNetwork(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.NetworkConfig);

        private void OnShowDisplay(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.DisplayConfig);

        private void OnShowAudio(object? sender, RoutedEventArgs e)
            => OpenRole(ShellRoles.AudioConfig);

        private void OnLogout(object? sender, RoutedEventArgs e) => Logout();

        private void OnRestart(object? sender, RoutedEventArgs e) => Restart();

        private void OnShutDown(object? sender, RoutedEventArgs e) => ShutDown();

        #endregion

        #region Roles

        /// <summary>
        /// Looks for the apps that could serve <paramref name="role"/> (every role when null),
        /// where nothing has looked for them yet.
        /// </summary>
        /// <remarks>
        /// Detection otherwise runs only when the search box opens, so until then every catalog
        /// app counted as not installed: Display said no app was set up even with Conchix
        /// Display sitting in /usr/bin, and opening a file found no text editor. A role's
        /// candidates are a handful of probes, not the whole catalog -- which on Windows means a
        /// wsl.exe per Linux app -- and each is looked for once.
        /// </remarks>
        private Task DetectRoleCandidatesAsync(string? role = null)
        {
            // With no role named -- for Preferences -- the apps behind File types too.
            var pending = App.Tools
                .Where(t => !t.IsDetected && t.IsAvailableHere
                    && (role == null
                        ? t.Roles.Count > 0 || t.Opens.Count > 0
                        : t.Roles.Contains(role, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            return pending.Count == 0 ? Task.CompletedTask : ToolDetector.RefreshAsync(pending);
        }

        /// <summary>
        /// Hands a file to whatever serves the text-editor role.
        /// </summary>
        /// <remarks>
        /// Returns false rather than complaining, so the file browser can carry on being a file
        /// browser; the prompt about configuring one is raised here, where Preferences is reachable.
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
        /// Opens whatever serves <paramref name="role"/>, offering Preferences when nothing does.
        /// </summary>
        /// <remarks>
        /// Nothing available is an ordinary state, not an error -- audio configuration has no
        /// candidate at all until something is installed for it, and on Conchix there is not yet
        /// an audio stack for anything to configure. Saying so and offering the place to fix it
        /// beats a click that does nothing.
        /// </remarks>
        private async void OpenRole(string role, string? argument = null)
        {
            // Before choosing: an app nobody has looked for yet counts as not installed, and the
            // role would fall back past it -- or find nothing at all. A no-op once looked for.
            await DetectRoleCandidatesAsync(role);

            if (Roles.TryInvoke(role, argument))
            {
                return;
            }

            var name = ShellRoles.DisplayName(role);
            if (await Confirm(name, $"No app is set up for {name.ToLowerInvariant()}. Choose one in Preferences?"))
            {
                ShowPreferences();
            }
        }

        #endregion

        #region Files

        /// <summary>What opens each type of file, from the catalog's <c>opens:</c> lists.</summary>
        private FileOpeners Openers => _openers ??= new FileOpeners(
            () => App.Tools, extension => App.Settings.GetFileTypeChoice(extension));

        /// <summary>
        /// Opens a file from Files with whatever opens its type, asking when nothing fits.
        /// </summary>
        private async void OpenFile(string path)
        {
            await DetectOpenersAsync(FileOpeners.Extension(path));

            var choice = Openers.Choose(path);
            switch (choice.Kind)
            {
                case FileOpenKind.App:
                    OpenWithApp(choice.Tool!, path);
                    break;

                case FileOpenKind.TextEditor:
                    OpenInEditor(path);
                    break;

                default:
                    OpenFileWith(path);
                    break;
            }
        }

        /// <summary>
        /// Offers every app that opens the file's type, the text editor, and Software for when
        /// nothing here does.
        /// </summary>
        private async void OpenFileWith(string path)
        {
            var extension = FileOpeners.Extension(path);
            await DetectOpenersAsync(extension);

            var choices = Openers.CandidatesFor(path)
                .Select(tool => new OpenWithChoice(tool.Name, always =>
                {
                    if (always)
                    {
                        App.Settings.SetFileTypeChoice(extension, tool.Id);
                    }

                    OpenWithApp(tool, path);
                }, CanRemember: extension.Length > 0))
                .Append(new OpenWithChoice("Text editor", _ => OpenInEditor(path), CanRemember: false))
                .Append(new OpenWithChoice("Find an app in Software...", _ => OpenRole(ShellRoles.AppManager), CanRemember: false))
                .ToList();

            var dialog = new OpenWithDialog(path, choices);
            if (await dialog.ShowDialog<bool?>(this) == true && dialog.Chosen is { } chosen)
            {
                chosen.Open(dialog.Always);
            }
        }

        /// <summary>
        /// Starts <paramref name="tool"/> on a file, with its <c>openArgs</c> when it has them,
        /// and the path a WSL app can see.
        /// </summary>
        private void OpenWithApp(ToolViewModel tool, string path)
            => Apps.LaunchTool(tool, [tool.RunsUnderWsl ? WslPath.FromWindows(path) : path], tool.OpenArgs);

        /// <summary>Looks for the apps that open <paramref name="extension"/>, where nothing has yet.</summary>
        private Task DetectOpenersAsync(string extension)
        {
            var pending = App.Tools
                .Where(t => !t.IsDetected && t.IsAvailableHere
                    && t.Opens.Any(o => string.Equals(o.Trim(), extension, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            return pending.Count == 0 ? Task.CompletedTask : ToolDetector.RefreshAsync(pending);
        }

        #endregion

        #region Session

        /// <summary>
        /// Ends Conch. Where Conch is the login shell that returns the tty to the login prompt,
        /// which is what logging out means here; anywhere else it is simply Exit.
        /// </summary>
        private async void Logout()
        {
            var ownsSession = CommandLineOptions.Current.OwnsSession;
            if (await Confirm(ownsSession ? "Logout" : "Exit", ownsSession ? "End this session?" : "Exit Conch?"))
            {
                this.Close();
            }
        }

        private void Restart()
            => RunPowerAction("Restart", "Restart the machine?", SessionActions.RestartCommand(Host.Current));

        private void ShutDown()
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

        private async Task<bool> Confirm(string title, string question)
            => await new ConfirmDialog(title, question).ShowDialog<bool?>(this) == true;

        #endregion
    }
}
