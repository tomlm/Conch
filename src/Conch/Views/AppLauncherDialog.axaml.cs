using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.Utilities;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

/// <summary>
/// Launches installed apps. Browsing and installing lives in <see cref="AppManagerDialog"/>.
/// </summary>
public partial class AppLauncherDialog : ManagedWindow
{
    private const string LogCategory = "Launcher";

    private readonly AppLauncherViewModel _viewModel;
    private readonly AppViewModel _appViewModel;
    private Services.AppLauncher? _apps;

    public AppLauncherDialog(AppViewModel appViewModel)
    {
        InitializeComponent();

        _appViewModel = appViewModel;
        _viewModel = new AppLauncherViewModel(appViewModel);
        DataContext = _viewModel;

        Opened += OnOpened;
    }

    /// <summary>
    /// Launching happens in the panel hosting this window, which is only known once shown.
    /// </summary>
    private Services.AppLauncher Apps => _apps ??= new Services.AppLauncher(WindowsPanel);

    private async void OnOpened(object? sender, EventArgs e)
    {
        // Typing should filter immediately; that is the point of a launcher.
        FilterBox.Focus();

        try
        {
            await ToolDetector.RefreshAsync(_appViewModel.Tools);
        }
        catch (Exception ex)
        {
            Log.Error(LogCategory, "Detection sweep failed", ex);
        }

        _viewModel.Refresh();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnManage(object? sender, RoutedEventArgs e)
    {
        // The discovery chain out of an empty launcher: nothing here, go and get some.
        var dialog = new AppManagerDialog(_appViewModel);
        dialog.Show(WindowsPanel);
        Close();
    }

    private void OnAppDoubleTapped(object? sender, TappedEventArgs e) => Launch();

    private void OnLaunch(object? sender, RoutedEventArgs e) => Launch();

    private async void Launch()
    {
        var tool = _viewModel.SelectedApp;
        if (tool == null)
        {
            return;
        }

        // Only interrupt for a prompt when the registration declares placeholders; most
        // apps take none and should start on the first keystroke.
        if (!ArgumentTemplate.RequiresPrompt(tool.Args))
        {
            Apps.LaunchTool(tool);
            Close();
            return;
        }

        var dialog = new ArgumentsDialog(tool);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
        {
            Apps.LaunchTool(tool, dialog.Values);
            Close();
        }
    }
}
