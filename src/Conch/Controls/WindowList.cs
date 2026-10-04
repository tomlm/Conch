using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Controls
{
    /// <summary>
    /// The top bar's list of open windows: one button each, like a taskbar.
    /// </summary>
    /// <remarks>
    /// Without it a minimized window was a title bar parked somewhere on the desktop, and a
    /// window hidden behind a maximized one could only be found by Ctrl+F6 through all of
    /// them. Clicking a button brings its window forward, restoring it if minimized; clicking
    /// the active window's button minimizes it, as the Windows taskbar does.
    ///
    /// Built from the <see cref="WindowsPanel"/>'s own children, so it needs nothing from the
    /// window manager beyond what it already exposes. Windows that do not fit collapse into
    /// a "+N" button listing the rest.
    /// </remarks>
    public sealed class WindowList : UserControl
    {
        /// <summary>Longest title shown before it is cut short.</summary>
        public const int MaxTitle = 16;

        /// <summary>Columns between buttons.</summary>
        public const int Spacing = 1;

        private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = Spacing };
        private readonly HashSet<ManagedWindow> _watched = new();
        private readonly HashSet<ManagedWindow> _attention = new();
        private WindowsPanel? _panel;
        private bool _rebuildPending;

        public WindowList()
        {
            Content = _row;
            ClipToBounds = true;
        }

        /// <summary>
        /// Starts following <paramref name="panel"/>'s windows.
        /// </summary>
        /// <remarks>
        /// Not in the constructor: the panel's window collection is its template's canvas, which
        /// does not exist until the panel has been laid out once.
        /// </remarks>
        public void Attach(WindowsPanel panel)
        {
            _panel = panel;
            _panel.Windows.CollectionChanged += (_, _) => QueueRebuild();
            Rebuild();
        }

        /// <summary>
        /// Marks <paramref name="window"/> as wanting attention -- a long build finished, a
        /// script needs an answer -- until it is next focused.
        /// </summary>
        /// <remarks>
        /// The taskbar's flashing button. Not for the window already in front, which the user
        /// is looking at.
        /// </remarks>
        public void SetAttention(ManagedWindow window)
        {
            if (window.IsActive && window.WindowState != WindowState.Minimized)
            {
                return;
            }

            _attention.Add(window);
            QueueRebuild();
        }

        /// <summary>A window's label: its title, cut to <see cref="MaxTitle"/>.</summary>
        public static string Label(string? title)
        {
            var text = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title.Trim();
            return text.Length <= MaxTitle ? text : text[..(MaxTitle - 1)] + "…";
        }

        /// <summary>
        /// How many buttons of <paramref name="widths"/> fit in <paramref name="available"/>
        /// columns, keeping room for the overflow button whenever not all of them do.
        /// </summary>
        public static int Fit(IReadOnlyList<int> widths, int available, int overflowWidth)
        {
            var all = widths.Sum() + Spacing * Math.Max(0, widths.Count - 1);
            if (all <= available)
            {
                return widths.Count;
            }

            var used = 0;
            for (var i = 0; i < widths.Count; i++)
            {
                var next = used + widths[i] + Spacing;
                if (next + overflowWidth > available)
                {
                    return i;
                }

                used = next;
            }

            return widths.Count;
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            if ((int)e.NewSize.Width != (int)e.PreviousSize.Width)
            {
                QueueRebuild();
            }
        }

        /// <summary>
        /// Rebuilds once for a burst of changes: activating a window deactivates another, and
        /// each is its own property change.
        /// </summary>
        private void QueueRebuild()
        {
            if (_rebuildPending)
            {
                return;
            }

            _rebuildPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _rebuildPending = false;
                Rebuild();
            }, DispatcherPriority.Background);
        }

        private void Rebuild()
        {
            if (_panel == null)
            {
                return;
            }

            var windows = _panel.Windows.OfType<ManagedWindow>().ToList();
            Watch(windows);

            // Looked at, or gone: either way no longer waiting.
            _attention.RemoveWhere(w => !windows.Contains(w) || (w.IsActive && w.WindowState != WindowState.Minimized));

            var labels = windows.Select(w => (_attention.Contains(w) ? "!" : string.Empty) + Label(w.Title)).ToList();
            var overflow = $"+{windows.Count} ▾";
            var shown = Fit(labels.Select(l => l.Length + 2).ToList(), (int)Bounds.Width, overflow.Length);

            _row.Children.Clear();
            for (var i = 0; i < shown; i++)
            {
                _row.Children.Add(ButtonFor(windows[i], labels[i]));
            }

            if (shown < windows.Count)
            {
                _row.Children.Add(OverflowButton(windows.Skip(shown).ToList()));
            }
        }

        private void Watch(List<ManagedWindow> windows)
        {
            foreach (var window in windows.Where(w => _watched.Add(w)))
            {
                window.PropertyChanged += OnWindowPropertyChanged;
            }

            foreach (var gone in _watched.Except(windows).ToList())
            {
                gone.PropertyChanged -= OnWindowPropertyChanged;
                _watched.Remove(gone);
            }
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ManagedWindow.TitleProperty
                || e.Property == ManagedWindow.IsActiveProperty
                || e.Property == ManagedWindow.WindowStateProperty)
            {
                QueueRebuild();
            }
        }

        /// <remarks>
        /// The active window is bracketed as well as highlighted: the highlight comes from the
        /// theme, and the brackets still say which one it is on a console with too few colours
        /// to tell them apart.
        /// </remarks>
        private Button ButtonFor(ManagedWindow window, string label)
        {
            var active = window.IsActive && window.WindowState != WindowState.Minimized;
            var button = new Button
            {
                Content = active ? $"[{label}]" : $" {label} ",
                Theme = this.FindResource("ThemeNakedButton") as Avalonia.Styling.ControlTheme,
            };
            ToolTip.SetTip(button, window.Title);
            button.Classes.Add("window");
            if (active)
            {
                button.Classes.Add("active");
            }

            if (_attention.Contains(window))
            {
                button.Classes.Add("attention");
            }

            button.Click += (_, _) => Toggle(window);
            return button;
        }

        private Button OverflowButton(IReadOnlyList<ManagedWindow> rest)
        {
            var flyout = new MenuFlyout();
            foreach (var window in rest)
            {
                var item = new MenuItem { Header = window.Title ?? "(untitled)" };
                item.Click += (_, _) => window.Activate();
                flyout.Items.Add(item);
            }

            return new Button
            {
                Content = $"+{rest.Count} ▾",
                Theme = this.FindResource("ThemeNakedButton") as Avalonia.Styling.ControlTheme,
                Flyout = flyout,
            };
        }

        private static void Toggle(ManagedWindow window)
        {
            if (window.IsActive && window.WindowState != WindowState.Minimized)
            {
                window.WindowState = WindowState.Minimized;
            }
            else
            {
                window.Activate();
            }
        }
    }
}
