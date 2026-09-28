using Avalonia;
using Conch.Infrastructure;
using Conch.Utilities;
using Consolonia;
using Consolonia.PlatformSupport;

namespace Conch
{
    public static class Program
    {
        /// <summary>
        /// Draw the mouse pointer ourselves rather than leaving it to the terminal.
        /// </summary>
        private const string SoftwareCursorArgument = "--software-cursor";

        private static bool _softwareCursor;

        private static void Main(string[] args)
        {
            // Conch draws over stdout, so an unhandled exception trace would be scrambled and
            // then lost with the console. Record it before the process goes down.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    Log.Error("Shell", "Unhandled exception", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Log.Error("Shell", "Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            _softwareCursor = args.Contains(SoftwareCursorArgument, StringComparer.Ordinal);

            Log.Info("Shell", $"Conch starting ({Environment.OSVersion}, .NET {Environment.Version}).");

            BuildAvaloniaApp()
                .StartWithConsoleLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            AppBuilder builder = AppBuilder.Configure<App>()
                .UseConsolonia();

            return SelectConsole(builder)
                .LogToException();
        }

        /// <summary>
        /// Chooses the console, honouring <c>--software-cursor</c>.
        /// </summary>
        /// <remarks>
        /// The override only applies where curses is the backend, which covers every console
        /// that can lack a pointer of its own. On Windows the terminal always draws one, and
        /// reproducing Consolonia's choice between the ANSI and legacy console outputs here
        /// would duplicate logic that is not ours to keep in step.
        /// </remarks>
        private static AppBuilder SelectConsole(AppBuilder builder)
        {
            if (!_softwareCursor)
            {
                return builder.UseAutoDetectedConsole();
            }

            if (OperatingSystem.IsWindows())
            {
                Log.Warning("Shell",
                    $"{SoftwareCursorArgument} ignored on Windows; the terminal draws its own pointer.");
                return builder.UseAutoDetectedConsole();
            }

            Log.Info("Shell", "Drawing the mouse pointer in software.");

            // UseAutoDetectedConsole builds the console itself, so asking for a different one
            // means repeating what it does for this platform: the console, then the clipboard
            // and colour-mode detection it would have chained on.
            return builder
                .UseConsole(new SoftwareCursorConsole())
                .UseAutoDetectClipboard()
                .UseAutoDetectConsoleColorMode();
        }
    }
}
