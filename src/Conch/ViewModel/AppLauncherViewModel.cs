using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Conch.ViewModel
{
    /// <summary>
    /// Backs the launcher: the apps present on this machine, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <see cref="AppManagerViewModel"/>. Launching is a
    /// frequent act that only ever concerns what is installed; browsing and installing is
    /// occasional, spans the whole catalog, and changes the machine. Showing both through
    /// one list is what makes the manager a poor launcher.
    /// </remarks>
    public partial class AppLauncherViewModel : ObservableObject
    {
        public AppViewModel AppViewModel { get; }

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private ObservableCollection<ToolViewModel> _apps = new();

        [ObservableProperty]
        private ToolViewModel? _selectedApp;

        /// <summary>
        /// True once detection has run and found nothing, so the view can say so rather
        /// than showing an empty grid that looks broken.
        /// </summary>
        [ObservableProperty]
        private bool _isEmpty;

        /// <summary>
        /// True while detection is still deciding what is here.
        /// </summary>
        [ObservableProperty]
        private bool _isChecking = true;

        public AppLauncherViewModel(AppViewModel appViewModel)
        {
            AppViewModel = appViewModel;

            // Detection runs in the background, so apps arrive after the window opens.
            foreach (var tool in AppViewModel.Tools)
            {
                tool.PropertyChanged += Tool_PropertyChanged;
            }

            AppViewModel.Tools.CollectionChanged += Tools_CollectionChanged;
            PropertyChanged += AppLauncherViewModel_PropertyChanged;

            Refresh();
        }

        private void Tools_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            foreach (var tool in e.OldItems?.Cast<ToolViewModel>() ?? Enumerable.Empty<ToolViewModel>())
            {
                tool.PropertyChanged -= Tool_PropertyChanged;
            }

            foreach (var tool in e.NewItems?.Cast<ToolViewModel>() ?? Enumerable.Empty<ToolViewModel>())
            {
                tool.PropertyChanged += Tool_PropertyChanged;
            }

            Refresh();
        }

        private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ToolViewModel.IsInstalled) or nameof(ToolViewModel.IsDetected))
            {
                Refresh();
            }
        }

        private void AppLauncherViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SearchText))
            {
                Refresh();
            }
        }

        /// <summary>
        /// Rebuilds the visible list from what is installed, honouring the filter.
        /// </summary>
        public void Refresh()
        {
            var installed = AppViewModel.Tools.Where(t => t.IsInstalled);

            var matching = string.IsNullOrWhiteSpace(SearchText)
                ? installed
                : installed.Where(t => Matches(t, SearchText));

            var selected = SelectedApp;

            Apps.Clear();
            foreach (var tool in matching.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Apps.Add(tool);
            }

            SelectedApp = selected != null && Apps.Contains(selected) ? selected : Apps.FirstOrDefault();

            IsChecking = AppViewModel.Tools.Any(t => !t.IsDetected);
            IsEmpty = !IsChecking && AppViewModel.Tools.All(t => !t.IsInstalled);
        }

        /// <summary>
        /// Substring match over the fields someone would type to find an app.
        /// </summary>
        /// <remarks>
        /// Not the catalog's full-text index, which the manager uses. Here the list is
        /// only what is installed and the filter should narrow as each key is pressed,
        /// so plain matching is both enough and more predictable.
        /// </remarks>
        private static bool Matches(ToolViewModel tool, string query)
        {
            return Contains(tool.Name, query)
                || Contains(tool.Id, query)
                || Contains(tool.Command, query)
                || Contains(tool.Description, query)
                || tool.Keywords.Any(k => Contains(k, query));
        }

        private static bool Contains(string? value, string query)
            => value != null && value.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }
}
