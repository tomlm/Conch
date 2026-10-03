using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services.Roles;
using Conch.Utilities;

namespace Conch.ViewModel
{
    /// <summary>
    /// Backs Conch Preferences: which app serves each role, the shell's hotkeys, how Conch
    /// looks, and what it is running on.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        /// <summary>
        /// The themes Consolonia ships, in the order Settings offers them.
        /// </summary>
        /// <remarks>
        /// Names rather than instances: the chosen one is written to settings and turned back
        /// into a theme at startup, so this list is data and stays free of Avalonia.
        ///
        /// ModernDarkTheme and TurboVisionDarkTheme are deliberately absent. Consolonia ships
        /// both as classes with no XAML behind them, so constructing either throws
        /// "No precompiled XAML found" -- they are declared but not usable, and offering a
        /// theme that cannot be built would be a setting that persists and then breaks startup.
        /// </remarks>
        public static readonly IReadOnlyList<string> ThemeNames =
        [
            "Modern",
            "ModernContrast",
            "TurboVision",
            "TurboVisionGray",
            "TurboVisionElegant",
            "TurboVisionCompatible",
        ];

        private readonly AppViewModel _app;
        private bool _loading;

        public SettingsViewModel(AppViewModel app, RoleRegistry roles)
        {
            _app = app;

            Roles = new ObservableCollection<RoleSettingViewModel>(
                ShellRoles.All.Select(role => new RoleSettingViewModel(
                    role,
                    roles.CandidatesFor(role),
                    app.Settings.GetRoleChoice(role),
                    (r, id) => app.Settings.SetRoleChoice(r, id))));

            Themes = new ObservableCollection<string>(ThemeNames);

            Hotkeys = new ObservableCollection<HotkeyBindingViewModel>(
                Services.Hotkeys.Actions.Select(a => new HotkeyBindingViewModel(a, StartRecording, ClearBinding, ResetBinding)));
            foreach (var row in Hotkeys)
            {
                ShowBinding(row);
            }

            _loading = true;
            SelectedTheme = app.Settings.Theme ?? AppViewModel.DefaultTheme;
            StatusAsText = app.Settings.StatusAsText;
            _loading = false;
        }

        #region Keyboard

        public ObservableCollection<HotkeyBindingViewModel> Hotkeys { get; }

        /// <summary>What the Keyboard tab says under the list: the prompt, or why a binding was refused.</summary>
        [ObservableProperty]
        private string _keyboardMessage = DefaultKeyboardMessage;

        private const string DefaultKeyboardMessage = "Click a binding, then press the new keys.";

        private HotkeyBindingViewModel? _recording;

        /// <summary>
        /// Starts taking the next key press as <paramref name="row"/>'s binding.
        /// </summary>
        /// <remarks>
        /// Through <see cref="HotkeyMap.Capture"/>, which the shell's key handler offers every
        /// press to before anything else: otherwise pressing Alt+F2 to bind it would focus the
        /// search box instead, and Esc would close this window.
        /// </remarks>
        public void StartRecording(HotkeyBindingViewModel row)
        {
            StopRecording();

            _recording = row;
            row.IsRecording = true;
            row.Keys = "press keys...";
            KeyboardMessage = $"Press the new keys for {row.Name}. Esc cancels.";
            _app.Hotkeys.Capture = (key, modifiers) => OnKeyCaptured(row, key, modifiers);
        }

        /// <summary>Stops recording, keeping the binding as it was. Safe to call when not recording.</summary>
        /// <remarks>
        /// Must run when the window closes: a capture left behind would swallow every key press
        /// the shell gets from then on.
        /// </remarks>
        public void StopRecording()
        {
            _app.Hotkeys.Capture = null;
            if (_recording != null)
            {
                _recording.IsRecording = false;
                ShowBinding(_recording);
                _recording = null;
            }
        }

        private void OnKeyCaptured(HotkeyBindingViewModel row, Key key, KeyModifiers modifiers)
        {
            if (key == Key.Escape && modifiers == KeyModifiers.None)
            {
                StopRecording();
                KeyboardMessage = DefaultKeyboardMessage;
                return;
            }

            // Alt on its own, on the way to Alt+F2: keep waiting.
            var gesture = Services.Hotkeys.FromKeyPress(key, modifiers);
            if (gesture == null)
            {
                return;
            }

            StopRecording();
            Bind(row, gesture);
        }

        private void Bind(HotkeyBindingViewModel row, KeyGesture gesture)
        {
            var text = Services.Hotkeys.Format(gesture);

            if (!Services.Hotkeys.IsBindable(gesture))
            {
                KeyboardMessage = $"{text} would stop that key typing. Include Ctrl, Alt or Super.";
                return;
            }

            if (Services.Hotkeys.ReservedFor(gesture) is { } meaning)
            {
                KeyboardMessage = $"{text} belongs to the window manager: {meaning}.";
                return;
            }

            if (_app.Hotkeys.ConflictFor(row.Action.Id, gesture) is { } clash)
            {
                KeyboardMessage = $"{text} is already {clash.Name}.";
                return;
            }

            _app.Settings.SetHotkey(row.Action.Id, text);
            ShowBinding(row);
            KeyboardMessage = DefaultKeyboardMessage;
        }

        private void ClearBinding(HotkeyBindingViewModel row)
        {
            StopRecording();

            // Empty, not removed: removing would bring the default back.
            _app.Settings.SetHotkey(row.Action.Id, string.Empty);
            ShowBinding(row);
            KeyboardMessage = DefaultKeyboardMessage;
        }

        private void ResetBinding(HotkeyBindingViewModel row)
        {
            StopRecording();

            if (Services.Hotkeys.Parse(row.Action.Default) is { } gesture
                && _app.Hotkeys.ConflictFor(row.Action.Id, gesture) is { } clash)
            {
                KeyboardMessage = $"{row.Action.Default} is now {clash.Name}. Change that first.";
                return;
            }

            _app.Settings.SetHotkey(row.Action.Id, null);
            ShowBinding(row);
            KeyboardMessage = DefaultKeyboardMessage;
        }

        private void ShowBinding(HotkeyBindingViewModel row)
        {
            var gesture = _app.Hotkeys.GestureFor(row.Action.Id);
            row.Keys = gesture == null ? "(none)" : Services.Hotkeys.Format(gesture);
        }

        #endregion

        /// <summary>Show the top bar's icons as words, for a console font without them.</summary>
        [ObservableProperty]
        private bool _statusAsText;

        partial void OnStatusAsTextChanged(bool value)
        {
            if (!_loading)
            {
                _app.Settings.StatusAsText = value;
            }
        }

        public ObservableCollection<RoleSettingViewModel> Roles { get; }

        public ObservableCollection<string> Themes { get; }

        [ObservableProperty]
        private string? _selectedTheme;

        partial void OnSelectedThemeChanged(string? value)
        {
            if (_loading || value == null)
            {
                return;
            }

            _app.Settings.Theme = value;
            _app.Theme = value;
        }

        /// <summary>
        /// Why the theme does not change as soon as it is picked.
        /// </summary>
        /// <remarks>
        /// Swapping the theme means replacing Application.Current.Styles[0], and Consolonia's
        /// own gallery has to null out the main window's content across that swap -- "otherwise
        /// Avalonia sets some trash template to WindowsPanel" -- and rebuild it afterwards. In
        /// Conch that content IS the WindowsPanel holding every open app, so a live swap would
        /// close every running program to change a colour. Applying at startup instead costs a
        /// restart and keeps the work.
        /// </remarks>
        public string ThemeNote => "Applied at startup. Open apps would be closed by changing it now.";

        public string Version => Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0] ?? "unknown";

        public string Runtime => $".NET {Environment.Version}";

        public string OperatingSystem => Environment.OSVersion.ToString();

        public string Session => CommandLineOptions.Current.OwnsSession
            ? "Conch owns this session"
            : "Conch is running as an application";

        public string SettingsPath => Path.Combine(Log.StateDirectory, "settings.json");

        public string LogPath => Log.LogFilePath;

        public string CatalogPath => Path.Combine(Log.StateDirectory, "Tools");
    }
}
