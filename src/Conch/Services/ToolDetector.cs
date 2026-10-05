using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Threading;
using Conch.Utilities;
using Conch.ViewModel;

namespace Conch.Services
{
    /// <summary>
    /// Works out which registered apps are actually present on this machine.
    /// </summary>
    public static class ToolDetector
    {
        private const string LogCategory = "Detect";

        /// <summary>
        /// A detect command that hangs must not hold up the catalog.
        /// </summary>
        /// <remarks>
        /// Generous on purpose. The first probe that enters WSL pays for booting the VM, which
        /// can take several seconds; a tighter budget reports installed apps as missing and
        /// offers to install them again. Native probes never spawn a process at all, so this
        /// only costs anything on Windows.
        /// </remarks>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Probes that spawn a process are slow; a few at a time, across every caller, keeps a
        /// large catalog responsive without forking dozens of shells at once.
        /// </summary>
        /// <remarks>
        /// Shared rather than one per call. Each call used to bring its own four, so the
        /// startup pass, Software opening and a catalog refresh landing together ran twelve.
        /// </remarks>
        private static readonly SemaphoreSlim Throttle = new(4);

        /// <summary>The probe under way for each app, which a second asker waits for rather than repeats.</summary>
        private static readonly ConcurrentDictionary<ToolViewModel, Lazy<Task>> InFlight =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Probes every tool and updates its <see cref="ToolViewModel.IsInstalled"/>.
        /// </summary>
        /// <remarks>
        /// An app already being probed is not probed again: the caller waits for the probe
        /// already running. Several things ask at once -- the startup pass, Software opening, a
        /// catalog refresh -- each for "whatever is not detected yet", which while the first
        /// pass runs is everything; without this they each probed the whole catalog, hundreds
        /// of processes in all, and Software took twenty seconds to say what was installed.
        /// </remarks>
        public static async Task RefreshAsync(IEnumerable<ToolViewModel> tools, CancellationToken cancellationToken = default)
        {
            var list = tools.Distinct().ToList();
            if (list.Count == 0)
            {
                return;
            }

            var probes = list.Select(tool =>
            {
                var probe = InFlight.GetOrAdd(tool, t => new Lazy<Task>(() => ProbeAndRecordAsync(t, cancellationToken)));
                var task = probe.Value;

                // Gone once finished -- by value, so a later probe of the same app that has
                // already taken its place is not removed, and a finished one never lingers to
                // answer a RECHECK with an old result.
                _ = task.ContinueWith(_ => InFlight.TryRemove(new KeyValuePair<ToolViewModel, Lazy<Task>>(tool, probe)),
                    TaskScheduler.Default);
                return task;
            });

            await Task.WhenAll(probes).ConfigureAwait(false);

            var count = list.Count(t => t.IsInstalled);
            Log.Info(LogCategory, $"{count} of {list.Count} registered app(s) present.");
        }

        private static async Task ProbeAndRecordAsync(ToolViewModel tool, CancellationToken cancellationToken)
        {
            await Throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var installed = await ProbeAsync(tool, cancellationToken).ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    tool.IsInstalled = installed;
                    tool.IsDetected = true;
                });
            }
            finally
            {
                Throttle.Release();
            }
        }

        /// <summary>
        /// Determines whether a single tool is present.
        /// </summary>
        public static async Task<bool> ProbeAsync(ToolViewModel tool, CancellationToken cancellationToken = default)
        {
            try
            {
                // Nothing to look for, and on Windows without WSL a probe would only start
                // wsl.exe to be told there is no distribution.
                if (!tool.IsAvailableHere)
                {
                    return false;
                }

                // An explicit detect command wins: it is the only way to find an app that is not
                // simply a binary sitting on PATH.
                if (!string.IsNullOrWhiteSpace(tool.Detect))
                {
                    return await RunProbeAsync(ShellCommand.ForScript(tool.Detect!, tool.RunsUnderWsl), cancellationToken)
                        .ConfigureAwait(false);
                }

                if (tool.RunsUnderWsl)
                {
                    // The Windows PATH says nothing about what exists inside WSL.
                    var probe = ShellCommand.ForScript($"command -v {tool.Command}", viaWsl: true);
                    return await RunProbeAsync(probe, cancellationToken).ConfigureAwait(false);
                }

                return PathUtils.IsOnPath(tool.Command);
            }
            catch (Exception ex)
            {
                Log.Error(LogCategory, $"Probe failed for {tool.Id}", ex);
                return false;
            }
        }

        private static async Task<bool> RunProbeAsync(ResolvedCommand command, CancellationToken cancellationToken)
        {
            var startInfo = BackgroundProcess.StartInfo(command.Process, command.Args);

            using var process = BackgroundProcess.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);

                // Distinguish this from a clean "not found": a timeout is a failed probe, and
                // silently reporting absence is how an installed app gets offered for install.
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log.Warning(LogCategory, $"Probe timed out after {ProbeTimeout.TotalSeconds:0}s: {command}");
                }

                return false;
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort; the probe already reported a negative result.
            }
        }
    }
}
