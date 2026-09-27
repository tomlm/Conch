using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.Utilities;
using Conch.ViewModel;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

public partial class AppManagerDialog : ManagedWindow
{
    private const string LogCategory = "Manager";

    private readonly AppManagerViewModel _viewModel;
    private AppLauncher? _launcher;

    public AppManagerDialog(AppViewModel appViewModel)
    {
        InitializeComponent();

        _viewModel = new AppManagerViewModel(appViewModel);
        DataContext = _viewModel;

        Opened += OnOpened;
    }

    /// <summary>
    /// Launching has to happen in the panel that hosts this dialog, which is only known once the
    /// window is shown.
    /// </summary>
    private AppLauncher Apps => _launcher ??= new AppLauncher(WindowsPanel);

    private async void OnOpened(object? sender, EventArgs e)
    {
        SearchBox.Focus();
        await RecheckAsync();
    }

    private async Task RecheckAsync()
    {
        try
        {
            await ToolDetector.RefreshAsync(_viewModel.AppViewModel.Tools);
        }
        catch (Exception ex)
        {
            Log.Error(LogCategory, "Detection sweep failed", ex);
        }
    }

    private async void OnRecheck(object? sender, RoutedEventArgs e) => await RecheckAsync();

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SelectedTool;
        if (tool == null)
        {
            return;
        }

        // Only interrupt with a prompt when the registration actually declares placeholders;
        // most apps take none and should launch on the first keystroke.
        if (!ArgumentTemplate.RequiresPrompt(tool.Args))
        {
            Apps.LaunchTool(tool);
            return;
        }

        var dialog = new ArgumentsDialog(tool);
        var result = await dialog.ShowDialog<bool?>(this);
        if (result == true)
        {
            Apps.LaunchTool(tool, dialog.Values);
        }
    }

    private void OnInstall(object? sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SelectedTool;
        if (tool == null || !tool.HasInstall)
        {
            return;
        }

        Apps.RunScript($"Install {tool.Name}", tool.Install, tool.RunsUnderWsl, exitCode => { _ = ReprobeAsync(tool); });
    }

    private void OnUninstall(object? sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SelectedTool;
        if (tool == null || !tool.HasUninstall)
        {
            return;
        }

        Apps.RunScript($"Uninstall {tool.Name}", tool.Uninstall, tool.RunsUnderWsl, exitCode => { _ = ReprobeAsync(tool); });
    }

    /// <summary>
    /// Re-probes one tool after a package command, so the buttons reflect what actually happened
    /// rather than the exit code the package manager reported.
    /// </summary>
    private static async Task ReprobeAsync(ToolViewModel tool)
    {
        try
        {
            await ToolDetector.RefreshAsync(new[] { tool });
        }
        catch (Exception ex)
        {
            Log.Error(LogCategory, $"Re-probe failed for {tool.Id}", ex);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
