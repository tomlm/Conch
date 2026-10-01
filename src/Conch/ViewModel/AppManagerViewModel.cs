using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Conch.ViewModel
{
    /// <summary>
    /// Backs the app manager: the filtered catalog, the current selection, and which actions
    /// that selection allows.
    /// </summary>
    public partial class AppManagerViewModel : ObservableValidator
    {
        public AppViewModel AppViewModel { get; init; }

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ToolViewModel> _filteredTools = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRun))]
        [NotifyPropertyChangedFor(nameof(CanInstall))]
        [NotifyPropertyChangedFor(nameof(CanUninstall))]
        private ToolViewModel? _selectedTool;

        public bool CanRun => SelectedTool != null;

        public bool CanInstall => SelectedTool is { HasInstall: true, IsInstalled: false };

        public bool CanUninstall => SelectedTool is { HasUninstall: true, IsInstalled: true };

        public AppManagerViewModel(AppViewModel appViewModel)
        {
            AppViewModel = appViewModel;

            ResetFilteredTools(AppViewModel.Tools);

            this.PropertyChanged += AppManagerViewModel_PropertyChanged;
        }

        partial void OnSelectedToolChanged(ToolViewModel? oldValue, ToolViewModel? newValue)
        {
            // Detection runs in the background and finishes after selection, so the buttons have
            // to follow the selected tool's own state, not just the fact that it changed.
            if (oldValue != null)
            {
                oldValue.PropertyChanged -= SelectedTool_PropertyChanged;
            }

            if (newValue != null)
            {
                newValue.PropertyChanged += SelectedTool_PropertyChanged;
            }
        }

        private void SelectedTool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ToolViewModel.IsInstalled) or nameof(ToolViewModel.IsDetected))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanUninstall));
            }
        }

        private void AppManagerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchText))
            {
                ResetFilteredTools(AppViewModel.Tools.Search(SearchText));
            }
        }

        private void ResetFilteredTools(IEnumerable<ToolViewModel> tools)
        {
            var selected = SelectedTool;

            FilteredTools.Clear();

            // Only what this machine can have: an app for another OS, or a Linux app on Windows
            // with no WSL to run it in, could only ever fail to install.
            foreach (var tool in tools.Where(t => t.IsAvailableHere))
            {
                FilteredTools.Add(tool);
            }

            // Keep the selection across a search when the tool is still in the results.
            SelectedTool = selected != null && FilteredTools.Contains(selected)
                ? selected
                : FilteredTools.FirstOrDefault();
        }
    }
}
