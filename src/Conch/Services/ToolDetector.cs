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
        /// Probes every tool and updates its <see cref="ToolViewModel.IsInstalled"/>.
        /// </summary>
        public static async Task RefreshAsync(IEnumerable<ToolViewModel> tools, CancellationToken cancellationToken = default)
        {
            var list = tools.ToList();
            if (list.Count == 0)
            {
                return;
            }

            // Probes that spawn a process are slow; a few at a time keeps a large catalog
            // responsive without forking dozens of shells at once.
            using var throttle = new SemaphoreSlim(4);

            var probes = list.Select(async tool =>
            {
                await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
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
                    throttle.Release();
                }
            });

            await Task.WhenAll(probes).ConfigureAwait(false);

            var count = list.Count(t => t.IsInstalled);
            Log.Info(LogCategory, $"{count} of {list.Count} registered app(s) present.");
        }

        /// <summary>
        /// Determines whether a single tool is present.
        /// </summary>
        public static async Task<bool> ProbeAsync(ToolViewModel tool, CancellationToken cancellationToken = default)
        {
            try
            {
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
            var startInfo = new ProcessStartInfo
            {
                FileName = command.Process,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in command.Args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
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
