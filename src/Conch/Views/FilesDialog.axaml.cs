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
        private readonly Func<string, bool>? _openFile;

        /// <param name="startAt">Where to open, or null for the first root.</param>
        /// <param name="openFile">
        /// Hands a chosen file to whatever serves the text-editor role, returning false when
        /// nothing does. Injected rather than resolved here so this window does not need to
        /// know about the registry.
        /// </param>
        public FilesDialog(string? startAt = null, Func<string, bool>? openFile = null)
        {
            InitializeComponent();

            _openFile = openFile;
            _viewModel = new FilesViewModel(startAt);
            DataContext = _viewModel;

            Opened += (_, _) => EntriesBox.Focus();
        }

        private void OnRootChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (RootsBox.SelectedItem is DriveRoot root)
            {
                _viewModel.Navigate(root.Path);
            }
        }

        private void OnEntryActivated(object? sender, TappedEventArgs e) => Activate(_viewModel.Selected);

        private void OnOpen(object? sender, RoutedEventArgs e) => Activate(_viewModel.Selected);

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

            // Left open on purpose. Opening a file is the start of doing something with it, and
            // closing the browser underneath would undo the navigation that got you here.
            _openFile?.Invoke(entry.Path);
        }
    }
}
