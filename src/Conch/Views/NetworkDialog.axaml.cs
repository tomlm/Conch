using Avalonia.Input;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    /// <summary>
    /// Conch's own network configuration, and the default provider for the network-config role.
    /// </summary>
    /// <remarks>
    /// It exists because no terminal network tool takes a mouse. nmtui is built on newt, whose
    /// only mouse path is a GPM socket on a Linux virtual console -- and whose xterm branch sets
    /// its descriptor to -2 and enables nothing, so a window like this one never sees a click.
    /// The alternatives on GitHub call EnableMouseCapture and then read only the keyboard, which
    /// is worse: it takes the terminal's own selection away and gives nothing back.
    ///
    /// Drawn by Conch instead, the mouse works because Consolonia already has it, and it works
    /// the same way as every other window in the shell.
    ///
    /// Scoped like the Files window: this connects and disconnects, and does not edit profiles.
    /// Static addresses, DNS, VPNs and hotspots are nmcli's job, and Settings can point the role
    /// at nmtui for anyone who wants the full editor.
    /// </remarks>
    public partial class NetworkDialog : ManagedWindow
    {
        private readonly NetworkViewModel _viewModel;
        private readonly CancellationTokenSource _closing = new();
        private AppLauncher? _launcher;

        public NetworkDialog()
        {
            InitializeComponent();

            _viewModel = new NetworkViewModel(runInTerminal: RunInTerminal);
            DataContext = _viewModel;

            Opened += OnOpened;
            Closed += OnClosed;
        }

        /// <summary>
        /// Launching needs the panel this dialog is hosted in, which exists only once it is
        /// shown -- so it is resolved when a launch actually happens, not in the constructor.
        /// </summary>
        private AppLauncher Apps => _launcher ??= new AppLauncher(WindowsPanel);

        private async void OnOpened(object? sender, EventArgs e)
        {
            WifiBox.Focus();

            // Without a rescan: the cached scan is usually current, and a window that takes
            // several seconds to first paint reads as a window that is broken.
            await _viewModel.RefreshAsync(cancellationToken: _closing.Token);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            // A refresh may still be in flight, and its answer now has nowhere to go.
            _closing.Cancel();
            _closing.Dispose();
        }

        private void RunInTerminal(string title, string script)
            => Apps.RunScript(title, script, viaWsl: false);

        private async void OnJoin(object? sender, RoutedEventArgs e)
            => await _viewModel.JoinAsync(_viewModel.SelectedWifi, _closing.Token);

        private async void OnRescan(object? sender, RoutedEventArgs e)
            => await _viewModel.RefreshAsync(rescan: true, _closing.Token);

        private async void OnActivate(object? sender, RoutedEventArgs e)
            => await _viewModel.ActivateAsync(_viewModel.SelectedConnection, _closing.Token);

        private async void OnDeactivate(object? sender, RoutedEventArgs e)
            => await _viewModel.DeactivateAsync(_viewModel.SelectedConnection, _closing.Token);

        private void OnClose(object? sender, RoutedEventArgs e) => Close();

        private async void OnWifiKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await _viewModel.JoinAsync(_viewModel.SelectedWifi, _closing.Token);
            }
        }

        private async void OnConnectionKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await _viewModel.ActivateAsync(_viewModel.SelectedConnection, _closing.Token);
            }
        }
    }
}
