using Avalonia.Input;

namespace Conch.Services
{
    /// <summary>A shell action a key combination can be bound to.</summary>
    /// <param name="Id">Stable id, as stored in settings.</param>
    /// <param name="Name">What Preferences calls it.</param>
    /// <param name="Default">The binding out of the box, or empty for none.</param>
    public sealed record HotkeyAction(string Id, string Name, string Default);

    /// <summary>
    /// The shell's own key bindings: which actions exist, their defaults, and turning a key
    /// press into an action.
    /// </summary>
    /// <remarks>
    /// Defaults are chosen to reach a terminal app at all. The Windows/Super key usually does
    /// not -- Windows Terminal and desktop environments keep it for themselves -- so the
    /// defaults follow the Linux desktop instead (Alt+F2 to run, Alt+F1 for the menu,
    /// Ctrl+Alt+T for a terminal), which arrive intact through Windows Terminal, kmscon and
    /// ssh alike. Super combinations can still be bound where the terminal delivers them.
    ///
    /// Maximize follows MDI instead, Ctrl+F10, which is where the window manager's own keys come
    /// from: Ctrl+F4 closes a window and Ctrl+F6 moves between them. Those are the window
    /// manager's, bound in every window's template, and Avalonia runs a window's key bindings
    /// before any key handler sees the press -- so they are listed in <see cref="Reserved"/>
    /// rather than offered for rebinding, and refused as bindings. (Alt+F4 would have been the
    /// obvious close key, and is the one that cannot work: on Windows the OS takes it and closes
    /// the whole terminal.) Files avoids Alt+E because Microsoft Edit opens its Edit menu with
    /// it, and a shell hotkey is taken from every app underneath.
    /// </remarks>
    public static class Hotkeys
    {
        public const string FocusSearch = "focus-search";
        public const string OpenMenu = "open-menu";
        public const string NewTerminal = "new-terminal";
        public const string Files = "files";
        public const string MaximizeWindow = "maximize-window";

        /// <summary>Every bindable action, in the order Preferences lists them.</summary>
        public static readonly IReadOnlyList<HotkeyAction> Actions =
        [
            new(FocusSearch, "Search apps and settings", "Alt+F2"),
            new(OpenMenu, "Open the Conch menu", "Alt+F1"),
            new(NewTerminal, "New terminal", "Ctrl+Alt+T"),
            new(Files, "Files", "Ctrl+Alt+E"),
            new(MaximizeWindow, "Maximize or restore window", "Ctrl+F10"),
        ];

        /// <summary>
        /// Combinations the window manager already owns, and what each does there.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> Reserved = new Dictionary<string, string>
        {
            ["Ctrl+F4"] = "it closes the window",
            ["Ctrl+F6"] = "it moves to the next window",
            ["Ctrl+Shift+F6"] = "it moves to the previous window",
            ["Ctrl+Tab"] = "it moves to the next window",
            ["Ctrl+Shift+Tab"] = "it moves to the previous window",
            ["Alt+OemMinus"] = "it opens the window menu",
        };

        /// <summary>What the window manager does with <paramref name="gesture"/>, or null when it is free.</summary>
        public static string? ReservedFor(KeyGesture gesture)
            => Reserved.TryGetValue(Format(gesture), out var meaning) ? meaning : null;

        /// <summary>The action with <paramref name="id"/>, or null.</summary>
        public static HotkeyAction? Find(string id)
            => Actions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A key combination from its text, or null for empty or unparseable text.
        /// </summary>
        public static KeyGesture? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                // "Super" is what Linux calls the Windows key; Avalonia knows it as Meta. A lone
                // digit is the digit key: Avalonia would read "1" as the enum's value 1, Cancel.
                return KeyGesture.Parse(string.Join('+', text.Trim().Split('+')
                    .Select(part => part.Trim())
                    .Select(part => part.Equals("Super", StringComparison.OrdinalIgnoreCase) ? "Meta"
                        : part.Length == 1 && char.IsAsciiDigit(part[0]) ? "D" + part
                        : part)));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A key combination's canonical text, as stored and shown: "Ctrl+Alt+T".</summary>
        /// <remarks>
        /// Not <see cref="KeyGesture.ToString()"/>, which names the Windows key after whatever
        /// platform it thinks it is on. Conch runs on Windows and Linux at once -- through WSL --
        /// so it says Super, the name a Linux desktop uses and Windows users recognise.
        /// </remarks>
        public static string Format(KeyGesture? gesture)
        {
            if (gesture == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Super");
            parts.Add(gesture.Key switch
            {
                >= Key.D0 and <= Key.D9 => ((char)('0' + (gesture.Key - Key.D0))).ToString(),
                _ => gesture.Key.ToString(),
            });
            return string.Join('+', parts);
        }

        /// <summary>
        /// True when <paramref name="gesture"/> can be a hotkey without taking a key from typing.
        /// </summary>
        /// <remarks>
        /// A hotkey is taken from every app underneath, so a bare letter -- or Shift and a
        /// letter, which is just a capital -- would make that letter impossible to type anywhere.
        /// Function keys are the exception: nobody types F2.
        /// </remarks>
        public static bool IsBindable(KeyGesture gesture)
            => (gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0
               || gesture.Key is >= Key.F1 and <= Key.F24;

        /// <summary>
        /// The combination a key press makes, or null for a press that is only a modifier.
        /// </summary>
        /// <remarks>
        /// Recording a binding sees Alt go down before F2 does; that first press is not a
        /// combination of its own and must not be recorded as one.
        /// </remarks>
        public static KeyGesture? FromKeyPress(Key key, KeyModifiers modifiers)
            => key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
                   or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None
                ? null
                : new KeyGesture(key, modifiers);
    }

    /// <summary>
    /// The bindings in force: each action's combination, from settings where the user chose
    /// one and the default otherwise.
    /// </summary>
    public sealed class HotkeyMap
    {
        private readonly Func<string, string?> _readBinding;

        /// <param name="readBinding">
        /// The stored binding for an action id: null when never set (use the default), empty
        /// when deliberately cleared.
        /// </param>
        public HotkeyMap(Func<string, string?> readBinding)
        {
            _readBinding = readBinding;
        }

        /// <summary>
        /// While set, receives every key press before anything else does, and no hotkey fires.
        /// </summary>
        /// <remarks>
        /// For recording a new binding in Preferences. The shell's key handler is the first to
        /// see any press, so this is the one place a recorder can get Alt+F2 before Alt+F2
        /// focuses the search box, and Esc before the dialog's Cancel button closes it.
        /// </remarks>
        public Action<Key, KeyModifiers>? Capture { get; set; }

        /// <summary>The combination bound to <paramref name="actionId"/>, or null for none.</summary>
        public KeyGesture? GestureFor(string actionId)
        {
            var stored = _readBinding(actionId);
            return Hotkeys.Parse(stored ?? Hotkeys.Find(actionId)?.Default);
        }

        /// <summary>The action a key press triggers, or null.</summary>
        public string? ActionFor(Key key, KeyModifiers modifiers)
        {
            foreach (var action in Hotkeys.Actions)
            {
                var gesture = GestureFor(action.Id);
                if (gesture != null && gesture.Key == key && gesture.KeyModifiers == modifiers)
                {
                    return action.Id;
                }
            }

            return null;
        }

        /// <summary>
        /// The other action already bound to <paramref name="gesture"/>, or null when it is free.
        /// </summary>
        /// <remarks>
        /// Two actions on one combination would mean one of them can never run, and which one
        /// would depend on list order. Preferences refuses the binding and names the clash.
        /// </remarks>
        public HotkeyAction? ConflictFor(string actionId, KeyGesture gesture)
            => Hotkeys.Actions.FirstOrDefault(a =>
                !string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase)
                && GestureFor(a.Id) is { } other
                && other.Key == gesture.Key && other.KeyModifiers == gesture.KeyModifiers);
    }
}
