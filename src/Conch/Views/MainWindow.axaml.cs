using Avalonia.Controls;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.ViewModel;

namespace Conch.Views
{
    public partial class MainWindow : Window
    {
        private AppLauncher? _launcher;

        public MainWindow()
        {
            InitializeComponent();
        }

        private AppLauncher Apps => _launcher ??= new AppLauncher(Windows);

        private void OnExit(object sender, RoutedEventArgs e)
        {
            this.Close();

            //var lifetime = Application.Current!.ApplicationLifetime as IControlledApplicationLifetime;
            //lifetime!.Shutdown();
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

        private void OnNewTerminal(object? sender, RoutedEventArgs e)
        {
            Apps.LaunchShell();
        }

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
