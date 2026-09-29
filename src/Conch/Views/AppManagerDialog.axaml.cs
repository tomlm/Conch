using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Conch.Services;
using Conch.Utilities;
using Conch.ViewModel;
using Consolonia.Controls;
using Iciclecreek.Avalonia.WindowManager;

namespace Conch.Views;

public partial class AppManagerDialog : ManagedWindow
{
    private const string LogCategory = "Manager";

    private readonly AppManagerViewModel _viewModel;
    private readonly AppViewModel _appViewModel;
    private AppLauncher? _launcher;

    public AppManagerDialog(AppViewModel appViewModel)
    {
        InitializeComponent();

        _appViewModel = appViewModel;
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

    private async void OnInstall(object? sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SelectedTool;
        if (tool == null || !tool.HasInstall)
        {
            return;
        }

        var plan = InstallPlan.For(tool, _appViewModel.Tools);

        if (plan.HasUnknownRequirements)
        {
            // Almost always a registration naming a prerequisite this build's catalog has not
            // caught up with. Say which, rather than failing partway through an install with a
            // package manager error that names nothing recognisable.
            await MessageBox.ShowDialog(
                $"Install {tool.Name}",
                $"{tool.Name} needs {string.Join(", ", plan.Missing)}, which is not in the catalog.",
                MessageBoxStyle.Ok);
            return;
        }

        var prerequisites = plan.Prerequisites.ToList();
        if (prerequisites.Count > 0)
        {
            // Worth asking. A prerequisite can be far larger than the app -- the .NET SDK is
            // 610 MB against a tool of a few -- and having that start unannounced because
            // someone clicked Install on something small is a bad surprise.
            var answer = await MessageBox.ShowDialog(
                $"Install {tool.Name}",
                $"{tool.Name} needs {string.Join(", ", prerequisites.Select(p => p.Name))}. Install {(prerequisites.Count == 1 ? "it" : "them")} too?",
                MessageBoxStyle.YesNo);

            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        RunPlan(plan.Steps, 0);
    }

    /// <summary>
    /// Runs an install plan a step at a time, stopping at the first failure.
    /// </summary>
    /// <remarks>
    /// Sequential rather than one combined script, so each step gets its own window with its
    /// own output and exit code. Installing the SDK and then the tool are different operations
    /// and failing halfway should say which half.
    ///
    /// Stopping on failure matters more than it looks: without the toolchain, the app's own
    /// install would fail too, and the second error would bury the first one that explained it.
    /// </remarks>
    private void RunPlan(IReadOnlyList<ToolViewModel> steps, int index)
    {
        if (index >= steps.Count)
        {
            return;
        }

        var step = steps[index];
        var label = index == steps.Count - 1 ? $"Install {step.Name}" : $"Install {step.Name} (needed first)";

        Apps.RunScript(label, step.Install, step.RunsUnderWsl, exitCode =>
        {
            _ = ReprobeAsync(step);

            if (exitCode == 0)
            {
                RunPlan(steps, index + 1);
            }
            else
            {
                Log.Warning("Install", $"{step.Id} failed with {exitCode}; stopping before {steps.Count - index - 1} remaining step(s).");
            }
        });
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
