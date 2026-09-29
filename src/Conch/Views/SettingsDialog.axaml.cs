using Avalonia.Interactivity;
using Conch.Services.Roles;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    /// <summary>
    /// Which app serves each role, how Conch looks, and what it is running on.
    /// </summary>
    public partial class SettingsDialog : ManagedWindow
    {
        public SettingsDialog(AppViewModel appViewModel, RoleRegistry roles)
        {
            InitializeComponent();

            // Choices are written as they are made, so there is no Save button and nothing to
            // lose by closing the window.
            DataContext = new SettingsViewModel(appViewModel, roles);
        }

        private void OnClose(object? sender, RoutedEventArgs e) => Close();
    }
}
