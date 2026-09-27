using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace Conduit.ViewModel
{
    public partial class AppViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _theme;

        public ToolsViewModel Tools { get; init; }

        public HashSet<string> InstalledToolIds { get; } = new HashSet<string>();

        public AppViewModel()
        {
            Tools = new ToolsViewModel();
            Theme = "Light";
        }
    }
}
