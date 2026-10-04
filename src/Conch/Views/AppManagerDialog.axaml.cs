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
        Closing += OnClosing;
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

    // PATH first: an install done outside Conch -- in a terminal -- may have added a folder
    // to it, and the probes that follow look commands up on PATH.
    private Task RecheckAsync()
        => _viewModel.RecheckAsync(async tools =>
        {
            await LoginEnvironment.RefreshPathAsync();
            await ToolDetector.RefreshAsync(tools);
        });

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
            await ConfirmDialog.Inform(
                $"Install {tool.Name}",
                $"{tool.Name} needs {string.Join(", ", plan.Missing)}, which is not in the catalog.")
                .ShowDialog<bool?>(this);
            return;
        }

        var prerequisites = plan.Prerequisites.ToList();
        if (prerequisites.Count > 0)
        {
            // Worth asking. A prerequisite can be far larger than the app -- the .NET SDK is
            // 610 MB against a tool of a few -- and having that start unannounced because
            // someone clicked Install on something small is a bad surprise.
            var answer = await new ConfirmDialog(
                $"Install {tool.Name}",
                $"{tool.Name} needs {string.Join(", ", prerequisites.Select(p => p.Name))}. Install {(prerequisites.Count == 1 ? "it" : "them")} too?")
                .ShowDialog<bool?>(this);

            if (answer != true)
            {
                return;
            }
        }

        var steps = plan.Steps
            .Select((step, i) => new TaskStep(
                i == plan.Steps.Count - 1 ? $"Installing {step.Name}" : $"Installing {step.Name} (needed first)",
                step, step.Install))
            .ToList();

        await RunTaskAsync(steps, $"{tool.Name} installed.");
    }

    private async void OnUninstall(object? sender, RoutedEventArgs e)
    {
        var tool = _viewModel.SelectedTool;
        if (tool == null || !tool.HasUninstall)
        {
            return;
        }

        await RunTaskAsync([new TaskStep($"Removing {tool.Name}", tool, tool.Uninstall)], $"{tool.Name} removed.");
    }

    private sealed record TaskStep(string Title, ToolViewModel Tool, string Script);

    /// <summary>How long a successful task's output stays open before the panel folds away.</summary>
    private static readonly TimeSpan CollapseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Runs the steps of an install or uninstall in the panel, one at a time, stopping at the
    /// first failure.
    /// </summary>
    /// <remarks>
    /// Sequential rather than one combined script, so each step has its own exit code. Installing
    /// the SDK and then the tool are different operations, and failing halfway should say which
    /// half. Stopping matters more than it looks: without the toolchain the app's own install
    /// fails too, and its error would bury the one that explained it.
    ///
    /// Every step is followed by a fresh probe, so the buttons follow what actually happened
    /// rather than what the package manager's exit code claimed.
    /// </remarks>
    private async Task RunTaskAsync(IReadOnlyList<TaskStep> steps, string succeeded)
    {
        if (_viewModel.IsTaskRunning || steps.Count == 0)
        {
            return;
        }

        // Resolved now, while the dialog is still in its panel: a step may finish after it has
        // closed, and the rest of the plan then runs in windows of its own.
        var apps = Apps;

        foreach (var step in steps)
        {
            _viewModel.BeginTask(step.Title);
            Log.Info(LogCategory, $"{step.Title}: {step.Script}");

            var exitCode = await RunStepAsync(apps, step).ConfigureAwait(true);

            // A step can add to PATH -- the .NET SDK brings /etc/profile.d/dotnet.sh, which is
            // what puts ~/.dotnet/tools there -- and the probe below looks the app up on PATH.
            // Refreshed first, or a tool that installed cleanly reads as not installed.
            await LoginEnvironment.RefreshPathAsync().ConfigureAwait(true);
            await ReprobeAsync(step.Tool).ConfigureAwait(true);

            if (exitCode != 0)
            {
                var outcome = exitCode is int code
                    ? $"{step.Title} failed (exit code {code})."
                    : $"{step.Title} ended, but how is unknown.";
                Log.Warning(LogCategory, outcome);
                _viewModel.EndTask(outcome);
                return;
            }
        }

        var token = _viewModel.EndTask(succeeded);
        Log.Info(LogCategory, succeeded);

        await Task.Delay(CollapseDelay).ConfigureAwait(true);
        _viewModel.CollapseAfterSuccess(token);
    }

    private Task<int?> RunStepAsync(AppLauncher apps, TaskStep step)
    {
        if (!_closed)
        {
            return TaskPanel.RunAsync(ShellCommand.ForScript(step.Script, step.Tool.RunsUnderWsl));
        }

        // The dialog closed partway through a plan: the remaining steps carry on in windows.
        var exit = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        apps.RunScript(step.Title, step.Script, step.Tool.RunsUnderWsl, code => exit.TrySetResult(code));
        return exit.Task;
    }

    private bool _closed;

    /// <summary>
    /// Moves a running task into a window of its own as the dialog closes.
    /// </summary>
    /// <remarks>
    /// Killing an install halfway can leave a package manager mid-transaction, and refusing to
    /// close is worse than letting the work carry on somewhere else. This has to happen in
    /// Closing, not Closed: the session must leave the panel's terminal before that terminal
    /// leaves the visual tree, which is when it would kill the process.
    /// </remarks>
    private void OnClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (e.Cancel)
        {
            return;
        }

        _closed = true;

        var title = _viewModel.TaskTitle;
        var apps = Apps;
        TaskPanel.HandOver(connection => apps.Adopt(connection, title));
    }

    private void OnToggleLog(object? sender, RoutedEventArgs e)
        => _viewModel.IsTaskPanelOpen = !_viewModel.IsTaskPanelOpen;

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
