using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;

namespace Conch.Utilities
{
    /// <summary>
    /// Severity of a <see cref="LogEntry"/>.
    /// </summary>
    public enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// A single diagnostic record.
    /// </summary>
    public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message)
    {
        public override string ToString()
            => $"{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level.ToString().ToUpperInvariant(),-7}] {Category}: {Message}";
    }

    /// <summary>
    /// Diagnostics sink for the shell.
    /// </summary>
    /// <remarks>
    /// Conch renders a TUI over stdout, so writing diagnostics to the console corrupts the
    /// display and the text is never seen. Everything goes to a rolling log file instead, and
    /// is mirrored into <see cref="Entries"/> so the shell can surface it in-process. On an
    /// appliance where Conch is the only UI, the in-shell view is the only way to read these.
    /// </remarks>
    public static class Log
    {
        private const int MaxInMemoryEntries = 500;
        private const long MaxLogBytes = 1024 * 1024;

        private static readonly object _fileLock = new();

        /// <summary>
        /// Directory holding Conch's per-user state (cached catalog, logs).
        /// </summary>
        public static string StateDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Conch");

        /// <summary>
        /// Full path of the current log file.
        /// </summary>
        public static string LogFilePath { get; } = Path.Combine(StateDirectory, "logs", "conch.log");

        /// <summary>
        /// Most recent entries, newest last. Safe to bind to; always mutated on the UI thread.
        /// </summary>
        public static ObservableCollection<LogEntry> Entries { get; } = new();

        /// <summary>
        /// Raised for every entry, on the thread that logged it.
        /// </summary>
        public static event EventHandler<LogEntry>? Logged;

        public static void Info(string category, string message) => Write(LogLevel.Info, category, message);

        public static void Warning(string category, string message) => Write(LogLevel.Warning, category, message);

        public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

        public static void Error(string category, string message, Exception ex)
            => Write(LogLevel.Error, category, $"{message}: {ex.GetType().Name}: {ex.Message}");

        private static void Write(LogLevel level, string category, string message)
        {
            var entry = new LogEntry(DateTimeOffset.Now, level, category, message);

            AppendToFile(entry);

            // Entries is bound to the log window; ObservableCollection is not thread-safe and
            // Avalonia requires collection changes on the UI thread.
            if (Dispatcher.UIThread.CheckAccess())
            {
                AddEntry(entry);
            }
            else
            {
                Dispatcher.UIThread.Post(() => AddEntry(entry));
            }

            Logged?.Invoke(null, entry);
        }

        private static void AddEntry(LogEntry entry)
        {
            Entries.Add(entry);
            while (Entries.Count > MaxInMemoryEntries)
            {
                Entries.RemoveAt(0);
            }
        }

        private static void AppendToFile(LogEntry entry)
        {
            try
            {
                lock (_fileLock)
                {
                    var dir = Path.GetDirectoryName(LogFilePath)!;
                    Directory.CreateDirectory(dir);

                    // Single-generation roll so an appliance that runs for months can't fill the disk.
                    var info = new FileInfo(LogFilePath);
                    if (info.Exists && info.Length > MaxLogBytes)
                    {
                        var previous = LogFilePath + ".1";
                        File.Delete(previous);
                        File.Move(LogFilePath, previous);
                    }

                    File.AppendAllText(LogFilePath, entry + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the shell down. A failure here leaves the in-memory
                // view intact, which is still visible via the log window.
            }
        }
    }
}
