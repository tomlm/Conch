using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Conch.Services;
using Conch.Utilities;

namespace Conch.ViewModel
{
    /// <summary>One row in the listing: a folder, a file, or the way back up.</summary>
    public sealed class FileEntryViewModel
    {
        public FileEntryViewModel(string path, string name, bool isDirectory, long size, DateTime modified, bool isParent = false)
        {
            Path = path;
            Name = name;
            IsDirectory = isDirectory;
            IsParent = isParent;
            Size = size;
            Modified = modified;
        }

        public string Path { get; }
        public string Name { get; }
        public bool IsDirectory { get; }

        /// <summary>The synthetic "..", which is a directory but not one of this folder's own.</summary>
        public bool IsParent { get; }

        public long Size { get; }
        public DateTime Modified { get; }

        /// <summary>A trailing slash marks a folder without needing a column for it.</summary>
        public string DisplayName => IsDirectory ? Name + "/" : Name;

        public string SizeText => IsDirectory ? string.Empty : Human(Size);

        public string ModifiedText => IsParent ? string.Empty : Modified.ToString("yyyy-MM-dd HH:mm");

        /// <summary>
        /// Sizes people can read at a glance, which is the only reason this column exists.
        /// </summary>
        private static string Human(long bytes)
        {
            string[] units = ["B", "K", "M", "G", "T"];
            double value = bytes;
            var unit = 0;

            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes}{units[0]}" : $"{value:0.#}{units[unit]}";
        }
    }

    /// <summary>
    /// Backs the Files window: where we are, what is there, and where else we could go.
    /// </summary>
    public partial class FilesViewModel : ObservableObject
    {
        private const string LogCategory = "Files";

        public FilesViewModel(string? startAt = null)
        {
            Roots = new ObservableCollection<DriveRoot>(DriveEnumerator.Enumerate());

            var start = startAt is not null && Directory.Exists(startAt)
                ? startAt
                : Roots.FirstOrDefault()?.Path ?? Directory.GetCurrentDirectory();

            Navigate(start);
        }

        public ObservableCollection<DriveRoot> Roots { get; }

        public ObservableCollection<FileEntryViewModel> Entries { get; } = new();

        [ObservableProperty]
        private string _currentPath = string.Empty;

        [ObservableProperty]
        private FileEntryViewModel? _selected;

        /// <summary>Why the listing is empty, when it is. Blank when it simply has no entries.</summary>
        [ObservableProperty]
        private string? _problem;

        /// <summary>
        /// Shows the contents of <paramref name="path"/>.
        /// </summary>
        /// <remarks>
        /// Folders before files, then by name, which is what every file manager does and what
        /// makes a long listing navigable by eye.
        ///
        /// An entry that cannot be read is skipped rather than abandoning the listing: a single
        /// protected folder in an otherwise ordinary directory must not blank the pane. A
        /// directory that cannot be opened at all is different, and says so.
        /// </remarks>
        public void Navigate(string path)
        {
            try
            {
                var directory = new DirectoryInfo(path);
                if (!directory.Exists)
                {
                    Problem = "That folder is not there any more.";
                    return;
                }

                var entries = new List<FileEntryViewModel>();

                if (directory.Parent != null)
                {
                    entries.Add(new FileEntryViewModel(
                        directory.Parent.FullName, "..", isDirectory: true, 0, default, isParent: true));
                }

                entries.AddRange(Read(directory));

                Entries.Clear();
                foreach (var entry in entries)
                {
                    Entries.Add(entry);
                }

                CurrentPath = directory.FullName;
                Selected = Entries.FirstOrDefault();

                // Counting the ".." row would mean an empty folder never said it was empty,
                // leaving a single mysterious row and no explanation.
                Problem = Entries.All(entry => entry.IsParent) ? "This folder is empty." : null;
            }
            catch (UnauthorizedAccessException)
            {
                Problem = "You do not have permission to open that folder.";
            }
            catch (Exception ex)
            {
                Log.Warning(LogCategory, $"Could not open {path}: {ex.Message}");
                Problem = ex.Message;
            }
        }

        /// <summary>Goes up one level, if there is one.</summary>
        public void GoUp()
        {
            var parent = Directory.GetParent(CurrentPath);
            if (parent != null)
            {
                Navigate(parent.FullName);
            }
        }

        private static IEnumerable<FileEntryViewModel> Read(DirectoryInfo directory)
        {
            var folders = new List<FileEntryViewModel>();
            var files = new List<FileEntryViewModel>();

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                try
                {
                    if (entry is DirectoryInfo)
                    {
                        folders.Add(new FileEntryViewModel(
                            entry.FullName, entry.Name, isDirectory: true, 0, entry.LastWriteTime));
                    }
                    else if (entry is FileInfo file)
                    {
                        files.Add(new FileEntryViewModel(
                            file.FullName, file.Name, isDirectory: false, file.Length, file.LastWriteTime));
                    }
                }
                catch (Exception)
                {
                    // A broken symlink or a file removed mid-listing. One bad entry is not a
                    // reason to show nothing.
                }
            }

            return folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Concat(files.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase));
        }
    }
}
