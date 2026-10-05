using System.Text.Json;
using System.Text.Json.Serialization;
using Conch.Utilities;

namespace Conch.Services
{
    /// <summary>
    /// The user's shell preferences, stored as JSON next to the log and the catalog cache.
    /// </summary>
    /// <remarks>
    /// Takes its directory as a constructor argument so it can be pointed at a temporary path
    /// in tests; nothing here touches Avalonia.
    /// </remarks>
    public sealed class ShellSettings
    {
        private const string LogCategory = "Settings";
        private const string FileName = "settings.json";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _path;
        private SettingsDocument _document = new();

        public ShellSettings(string directory)
        {
            _path = Path.Combine(directory, FileName);
        }

        /// <summary>Settings in the usual place, under <see cref="Log.StateDirectory"/>.</summary>
        public static ShellSettings Default() => new(Log.StateDirectory);

        /// <summary>The Consolonia theme to apply at startup, or null for the built-in default.</summary>
        public string? Theme
        {
            get => _document.Theme;
            set
            {
                _document.Theme = value;
                Save();
            }
        }

        /// <summary>
        /// Show the top bar's icons as words, for fonts without the glyphs.
        /// </summary>
        public bool StatusAsText
        {
            get => _document.StatusAsText;
            set
            {
                _document.StatusAsText = value;
                Save();
            }
        }

        /// <summary>
        /// The stored binding for a hotkey action: null when never set (the default applies),
        /// empty when the user cleared it.
        /// </summary>
        public string? GetHotkey(string actionId)
            => _document.Hotkeys.TryGetValue(actionId, out var gesture) ? gesture : null;

        /// <summary>
        /// Stores a binding. Empty clears the action; null forgets the choice, so the default
        /// applies again.
        /// </summary>
        public void SetHotkey(string actionId, string? gesture)
        {
            if (gesture == null)
            {
                _document.Hotkeys.Remove(actionId);
            }
            else
            {
                _document.Hotkeys[actionId] = gesture;
            }

            Save();
        }

        /// <summary>What is pinned to the top of search, in the order it was pinned.</summary>
        public IReadOnlyList<string> Pinned => _document.Pinned;

        /// <summary>Pins <paramref name="id"/> to the top of search, or unpins it.</summary>
        public void SetPinned(string id, bool pinned)
        {
            _document.Pinned.RemoveAll(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
            if (pinned)
            {
                _document.Pinned.Add(id);
            }

            Save();
        }

        /// <summary>The app id chosen to open files with <paramref name="extension"/>, or null.</summary>
        public string? GetFileTypeChoice(string extension)
            => _document.FileTypes.TryGetValue(extension.ToLowerInvariant(), out var id) ? id : null;

        /// <summary>Records the app that opens <paramref name="extension"/>; null clears it.</summary>
        /// <remarks>
        /// Keys are lower-cased here rather than trusted to the dictionary's comparer, which
        /// does not survive being read back from JSON.
        /// </remarks>
        public void SetFileTypeChoice(string extension, string? appId)
        {
            extension = extension.ToLowerInvariant();
            if (string.IsNullOrEmpty(appId))
            {
                _document.FileTypes.Remove(extension);
            }
            else
            {
                _document.FileTypes[extension] = appId;
            }

            Save();
        }

        /// <summary>The provider id chosen for <paramref name="role"/>, or null when unset.</summary>
        public string? GetRoleChoice(string role)
            => _document.Roles.TryGetValue(role, out var id) ? id : null;

        /// <summary>
        /// Records the provider chosen for a role. A null id clears the choice, returning the
        /// role to whatever it would resolve to on its own.
        /// </summary>
        public void SetRoleChoice(string role, string? providerId)
        {
            if (string.IsNullOrEmpty(providerId))
            {
                _document.Roles.Remove(role);
            }
            else
            {
                _document.Roles[role] = providerId;
            }

            Save();
        }

        /// <summary>
        /// Reads the file, falling back to defaults when it is absent or unreadable.
        /// </summary>
        /// <remarks>
        /// A file that will not parse is moved aside rather than overwritten. Settings are
        /// small but they are the user's, and destroying them to recover from a parse error --
        /// which is usually a half-written file or a hand edit with a trailing comma -- leaves
        /// nothing to look at afterwards and no way back.
        /// </remarks>
        public ShellSettings Load()
        {
            if (!File.Exists(_path))
            {
                return this;
            }

            try
            {
                var json = File.ReadAllText(_path);
                _document = JsonSerializer.Deserialize<SettingsDocument>(json, SerializerOptions)
                            ?? new SettingsDocument();
            }
            catch (Exception ex)
            {
                _document = new SettingsDocument();

                var spoiled = _path + ".bad";
                try
                {
                    File.Move(_path, spoiled, overwrite: true);
                    Log.Warning(LogCategory,
                        $"Could not read settings ({ex.Message}). Kept the file as {Path.GetFileName(spoiled)} and started from defaults.");
                }
                catch (Exception moveFailure)
                {
                    Log.Warning(LogCategory,
                        $"Could not read settings ({ex.Message}), and could not set the file aside ({moveFailure.Message}). Using defaults.");
                }
            }

            return this;
        }

        /// <summary>Raised after any setting changes, so what shows it can catch up.</summary>
        public event EventHandler? Changed;

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_document, SerializerOptions));
            }
            catch (Exception ex)
            {
                // Losing a preference is not worth taking the shell down for.
                Log.Warning(LogCategory, $"Could not save settings: {ex.Message}");
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The file's shape.
        /// </summary>
        /// <remarks>
        /// Unknown members are ignored by default, which is deliberate: a newer Conch may write
        /// keys this build has never heard of, and an older one reading them must not throw.
        /// <see cref="Version"/> exists so a future change that cannot be handled that way has
        /// something to branch on.
        /// </remarks>
        private sealed class SettingsDocument
        {
            // 2 added status-as-text and hotkeys, 3 file-types. Each is an addition an older
            // file simply lacks, so it loads with the defaults and nothing needs migrating.
            public int Version { get; set; } = 3;

            public string? Theme { get; set; }

            public Dictionary<string, string> Roles { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            public bool StatusAsText { get; set; }

            public Dictionary<string, string> Hotkeys { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Ids pinned to the top of search: catalog ids, or conch:... for the shell's own.</summary>
            public List<string> Pinned { get; set; } = [];

            /// <summary>Extension, with its dot, to the id of the app that opens it.</summary>
            public Dictionary<string, string> FileTypes { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
