using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services;

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

        /// <summary>Only an app that is there can be run; anything else would open a window onto "command not found".</summary>
        public bool CanRun => SelectedTool is { IsInstalled: true };

        /// <summary>True while a detection sweep is running.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanRecheck))]
        private bool _isChecking;

        public bool CanRecheck => !IsChecking;

        /// <summary>What the last detection sweep found, or that one is running.</summary>
        [ObservableProperty]
        private string _checkStatus = string.Empty;

        /// <summary>
        /// Probes every app again and says what it found.
        /// </summary>
        /// <remarks>
        /// Each app goes back to "Checking..." first. Without that, a recheck that confirmed
        /// what was already shown changed nothing on screen, and read as a button that did
        /// nothing -- which is the common case, since most rechecks find no change.
        /// </remarks>
        public async Task RecheckAsync(Func<IReadOnlyList<ToolViewModel>, Task> detect)
        {
            if (IsChecking)
            {
                return;
            }

            var tools = AppViewModel.Tools.Where(t => t.IsAvailableHere).ToList();

            IsChecking = true;
            CheckStatus = $"Checking {tools.Count} apps...";
            foreach (var tool in tools)
            {
                tool.IsDetected = false;
            }

            try
            {
                await detect(tools).ConfigureAwait(true);
                var installed = tools.Count(t => t.IsInstalled);
                CheckStatus = $"{installed} of {tools.Count} apps installed.";
            }
            catch (Exception ex)
            {
                Utilities.Log.Error("Manager", "Detection sweep failed", ex);
                CheckStatus = "Checking failed; see the log.";
            }
            finally
            {
                IsChecking = false;
            }
        }

        public bool CanInstall => SelectedTool is { HasInstall: true, IsInstalled: false };

        public bool CanUninstall => SelectedTool is { HasUninstall: true, IsInstalled: true };

        /// <summary>The selected app's screenshot, once fetched and decoded.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasScreenshotImage))]
        private IImage? _screenshotImage;

        public bool HasScreenshotImage => ScreenshotImage != null;

        /// <summary>
        /// What the screenshot area says instead of a picture -- loading, none, or why not.
        /// Empty once the picture is showing.
        /// </summary>
        [ObservableProperty]
        private string _screenshotStatus = string.Empty;

        private readonly Func<string, CancellationToken, Task<byte[]?>> _fetchScreenshot;
        private readonly Func<byte[], IImage> _decodeScreenshot;

        /// <summary>
        /// Decoded at full size, by the plain constructor and not by <c>Bitmap.DecodeToWidth</c>.
        /// </summary>
        /// <remarks>
        /// DecodeToWidth would have bounded the memory, and crashes the render loop instead.
        /// Consolonia's LoadBitmapToWidth asks Skia for a WRITEABLE bitmap, and every draw then
        /// resizes it to the cells it occupies -- which Skia's ResizeBitmap refuses for anything
        /// but an immutable one ("Invalid source bitmap type", Avalonia.Skia 12.0.4). The plain
        /// constructor goes through LoadBitmap, which yields the immutable kind. Scaling down
        /// afterwards is not a way round it either: Consolonia's wrapper reports half the real
        /// height, so a resize computed from it would squash the picture.
        ///
        /// The cost is bounded anyway: ScreenshotCache refuses files over 16 MB, and only the
        /// selected app's picture is held.
        /// </remarks>
        private static IImage DecodeFullSize(byte[] bytes)
            => new Bitmap(new MemoryStream(bytes));

        private CancellationTokenSource? _screenshotLoad;

        /// <summary>The screenshot load for the current selection, for whoever needs to wait on it.</summary>
        public Task ScreenshotLoad { get; private set; } = Task.CompletedTask;

        /// <param name="fetchScreenshot">Gets a screenshot's bytes; the network and its cache by default.</param>
        /// <param name="decodeScreenshot">Turns bytes into a picture; Skia by way of Avalonia by default.</param>
        /// <remarks>
        /// Constructor parameters rather than settable properties because the constructor already
        /// selects the first app, and that selection's screenshot load must use them too.
        /// </remarks>
        public AppManagerViewModel(
            AppViewModel appViewModel,
            Func<string, CancellationToken, Task<byte[]?>>? fetchScreenshot = null,
            Func<byte[], IImage>? decodeScreenshot = null)
        {
            AppViewModel = appViewModel;
            _fetchScreenshot = fetchScreenshot ?? ScreenshotCache.GetAsync;
            _decodeScreenshot = decodeScreenshot ?? DecodeFullSize;

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

            ScreenshotLoad = LoadScreenshotAsync(newValue);
        }

        /// <summary>
        /// Shows <paramref name="tool"/>'s screenshot, abandoning any load still running for the
        /// one selected before.
        /// </summary>
        /// <remarks>
        /// Abandoned rather than left to finish: moving down a list of a hundred apps would
        /// otherwise fetch every picture it passed, and the last to arrive -- not the one now
        /// selected -- would be the one left showing.
        /// </remarks>
        private async Task LoadScreenshotAsync(ToolViewModel? tool)
        {
            _screenshotLoad?.Cancel();
            _screenshotLoad?.Dispose();
            var load = _screenshotLoad = new CancellationTokenSource();

            SetScreenshot(null);

            if (tool == null)
            {
                ScreenshotStatus = string.Empty;
                return;
            }

            if (!tool.HasScreenshot)
            {
                ScreenshotStatus = "No screenshot.";
                return;
            }

            ScreenshotStatus = "Loading screenshot...";

            var bytes = await _fetchScreenshot(tool.Screenshot, load.Token).ConfigureAwait(true);
            if (load.IsCancellationRequested)
            {
                return;
            }

            if (bytes == null)
            {
                ScreenshotStatus = "The screenshot could not be fetched.";
                return;
            }

            try
            {
                SetScreenshot(_decodeScreenshot(bytes));
                ScreenshotStatus = string.Empty;
            }
            catch (Exception ex)
            {
                // A file that is not a picture, or a format Skia does not read.
                Utilities.Log.Warning("Screenshots", $"{tool.Screenshot}: {ex.Message}");
                ScreenshotStatus = "The screenshot could not be shown.";
            }
        }

        private void SetScreenshot(IImage? image)
        {
            var previous = ScreenshotImage;
            ScreenshotImage = image;

            // Bitmaps hold native memory, and nothing else references the one being replaced.
            (previous as IDisposable)?.Dispose();
        }

        private void SelectedTool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ToolViewModel.IsInstalled) or nameof(ToolViewModel.IsDetected))
            {
                OnPropertyChanged(nameof(CanRun));
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
