using Avalonia.Controls;
using Avalonia.Interactivity;
using Conch.Services.Roles;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views
{
    /// <summary>
    /// Conch Preferences: which app serves each role, the shell's hotkeys, how Conch looks, and
    /// what it is running on.
    /// </summary>
    public partial class SettingsDialog : ManagedWindow
    {
        /// <summary>The tab About Conch opens to.</summary>
        public const string AboutTab = "About";

        /// <param name="tab">The header of the tab to open on; the first when null.</param>
        public SettingsDialog(AppViewModel appViewModel, RoleRegistry roles, string? tab = null)
        {
            InitializeComponent();

            // Choices are written as they are made, so there is no Save button and nothing to
            // lose by closing the window.
            var viewModel = new SettingsViewModel(appViewModel, roles);
            DataContext = viewModel;

            // A binding half-recorded when the window goes would otherwise keep every key press.
            Closed += (_, _) => viewModel.StopRecording();

            if (tab != null)
            {
                Tabs.SelectedItem = Tabs.Items.OfType<TabItem>()
                    .FirstOrDefault(t => string.Equals(t.Header as string, tab, StringComparison.Ordinal));
            }
        }

        private void OnClose(object? sender, RoutedEventArgs e) => Close();
    }
}
