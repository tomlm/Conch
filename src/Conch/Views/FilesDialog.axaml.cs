using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.Services.Roles;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    /// <summary>
    /// Conch's own file browser, and the default provider for the file-explorer role.
    /// </summary>
    /// <remarks>
    /// A browser rather than a file manager: it goes places and opens things, and does not
    /// rename, delete, copy or move. The catalog already carries real file managers for that,
    /// and half a delete is worse than none.
    ///
    /// It exists because the role needs a default that works on every host. The catalog's file
    /// managers are Linux-only, and Consolonia's own pickers cannot help -- they are internal,
    /// and they navigate only through ".." and down, so they can never reach a second drive.
    /// </remarks>
    public partial class FilesDialog : ManagedWindow
    {
        private readonly FilesViewModel _viewModel;
        private readonly Action<string>? _openFile;
        private readonly Action<string>? _openWith;

        /// <summary>Choosing rather than opening: a file, or a folder when true; null when browsing.</summary>
        private bool? _pickFolder;

        /// <param name="startAt">Where to open, or null for the first root.</param>
        /// <param name="openFile">
        /// Opens a chosen file with whatever opens its type. Injected rather than resolved here
        /// so this window does not need to know about the catalog or the registry.
        /// </param>
        /// <param name="openWith">Asks which app to open a file with.</param>
        public FilesDialog(string? startAt = null, Action<string>? openFile = null, Action<string>? openWith = null)
        {
            InitializeComponent();

            _openFile = openFile;
            _openWith = openWith;
            _viewModel = new FilesViewModel(startAt);
            DataContext = _viewModel;

            // Posted: the window manager focuses a window's first control as it opens -- here
            // the drive list, where Down switches drives instead of moving through files.
            Opened += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                EntriesBox.Focus();
                if (_viewModel.Selected == null && _viewModel.Entries.Count > 0)
                {
                    _viewModel.Selected = _viewModel.Entries[0];
                }
            }, DispatcherPriority.Background);
        }

        /// <remarks>
        /// The drive list starts unfocusable and is made focusable here, after the window
        /// manager has chosen what to focus. It picks the first focusable control when a window
        /// loads and goes back to it on every activation -- and first in the layout is the drive
        /// list, where Down switches drives instead of moving through the files.
        /// </remarks>
        protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
        {
            base.OnLoaded(e);
            RootsBox.Focusable = true;
        }

        private void OnRootChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (RootsBox.SelectedItem is DriveRoot root)
            {
                _viewModel.Navigate(root.Path);
            }
        }

        private void OnEntryActivated(object? sender, TappedEventArgs e) => Activate(_viewModel.Selected);

        /// <summary>
        /// Files as a chooser, for <c>conch pick</c>: shown as a dialog, it closes with the
        /// path chosen, or null.
        /// </summary>
        public static FilesDialog ForPicking(string? startAt, bool folder)
        {
            var dialog = new FilesDialog(startAt)
            {
                _pickFolder = folder,
                Title = folder ? "Choose a folder" : "Choose a file",
            };

            dialog.OpenButton.Content = "SELECT";
            dialog.OpenWithButton.IsVisible = false;
            return dialog;
        }

        private void OnOpen(object? sender, RoutedEventArgs e)
        {
            // Choosing a folder: the one selected, or else the one being looked at.
            if (_pickFolder == true)
            {
                var selected = _viewModel.Selected;
                Close(selected is { IsDirectory: true, IsParent: false } ? selected.Path : _viewModel.CurrentPath);
                return;
            }

            Activate(_viewModel.Selected);
        }

        /// <summary>The row a right-click menu item belongs to, made the selection so the window agrees.</summary>
        private FileEntryViewModel? RowOf(object? sender)
        {
            if ((sender as Control)?.DataContext is FileEntryViewModel entry)
            {
                _viewModel.Selected = entry;
                return entry;
            }

            return null;
        }

        private void OnOpenRow(object? sender, RoutedEventArgs e)
        {
            if (RowOf(sender) != null)
            {
                OnOpen(sender, e);
            }
        }

        private void OnOpenWithRow(object? sender, RoutedEventArgs e)
        {
            if (RowOf(sender) != null)
            {
                OnOpenWith(sender, e);
            }
        }

        private void OnOpenWith(object? sender, RoutedEventArgs e)
        {
            if (_viewModel.Selected is { IsDirectory: false } entry)
            {
                _openWith?.Invoke(entry.Path);
            }
        }

        private void OnUp(object? sender, RoutedEventArgs e) => _viewModel.GoUp();

        private void OnClose(object? sender, RoutedEventArgs e) => Close();

        private void OnListKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    Activate(_viewModel.Selected);
                    e.Handled = true;
                    break;

                // The key every file browser has used to go up since long before this one.
                case Key.Back:
                    _viewModel.GoUp();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>
        /// A folder is somewhere to go; a file is something to open.
        /// </summary>
        private void Activate(FileEntryViewModel? entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry.IsDirectory)
            {
                _viewModel.Navigate(entry.Path);
                return;
            }

            if (_pickFolder == false)
            {
                Close(entry.Path);
                return;
            }

            if (_pickFolder == true)
            {
                return;
            }

            // Left open on purpose. Opening a file is the start of doing something with it, and
            // closing the browser underneath would undo the navigation that got you here.
            _openFile?.Invoke(entry.Path);
        }
    }
}
