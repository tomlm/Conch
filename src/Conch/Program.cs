using Avalonia;
using Conch.Utilities;
using Consolonia;

namespace Conch
{
    public static class Program
    {
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

            // Read, not consumed: args still go on to Avalonia below, so a flag meant for the
            // toolkit is not swallowed here.
            var options = CommandLineOptions.Parse(args);
            CommandLineOptions.SetCurrent(options);

            Log.Info("Shell", $"Conch starting ({Environment.OSVersion}, .NET {Environment.Version}).");

            if (options.OwnsSession)
            {
                Log.Info("Shell", "Running as the session; shutdown and restart are available.");
            }

            BuildAvaloniaApp()
                .StartWithConsoleLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            // UseSkia before UseConsolonia, as Consolonia's own gallery does: Consolonia keeps
            // every drawing operation for itself and hands Skia only what it cannot do, which is
            // decoding PNG/JPEG/GIF -- an app's screenshot in the app manager. Without it,
            // loading any such picture raises BitmapsNotSupported. The terminals are not
            // affected: their Skia path asks the drawing context for a Skia lease, Consolonia's
            // context has none, and they keep drawing as text.
            return AppBuilder.Configure<App>()
                .UseSkia()
                .UseConsolonia()
                .UseAutoDetectedConsole()
                .LogToException();
        }
    }
}