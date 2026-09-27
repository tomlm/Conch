using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucene.Net.Util;

namespace Conch.ViewModel
{
    /// <summary>
    /// View model for a tool
    /// </summary>
    public partial class AppManagerViewModel  : ObservableValidator
    {
        public AppViewModel AppViewModel { get; init; }

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ToolViewModel> _filteredTools = new();

        public AppManagerViewModel(AppViewModel appViewModel)
        {
            AppViewModel = appViewModel;

            ResetFilteredTools(AppViewModel.Tools);

            this.PropertyChanged += AppManagerViewModel_PropertyChanged; 
        }

        private void AppManagerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchText))
            {
                ResetFilteredTools(AppViewModel.Tools.Search(SearchText));
            }
        }

        private void ResetFilteredTools(IEnumerable<ToolViewModel> tools)
        {
            FilteredTools.Clear();
            FilteredTools.AddRange(tools);
        }
    }
}
