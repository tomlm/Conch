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

            Log.Info("Shell", $"Conch starting ({Environment.OSVersion}, .NET {Environment.Version}).");

            BuildAvaloniaApp()
                .StartWithConsoleLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UseConsolonia()
                .UseAutoDetectedConsole()
                .LogToException();
        }
    }
}