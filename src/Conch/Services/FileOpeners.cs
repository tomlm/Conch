using System.Text;
using Conch.ViewModel;

namespace Conch.Services
{
    /// <summary>How Files should open a file.</summary>
    public enum FileOpenKind
    {
        /// <summary>With <see cref="FileOpenChoice.Tool"/>.</summary>
        App,

        /// <summary>With whatever serves the text-editor role.</summary>
        TextEditor,

        /// <summary>Nothing fits on its own; ask, through Open with.</summary>
        Ask,
    }

    /// <summary>The outcome of <see cref="FileOpeners.Choose"/>.</summary>
    public sealed record FileOpenChoice(FileOpenKind Kind, ToolViewModel? Tool = null);

    /// <summary>
    /// Decides what opens a file: the apps that declare its extension in <c>opens:</c>, the
    /// user's choice among them, and the text editor for text nothing else claims.
    /// </summary>
    /// <remarks>
    /// The knowledge lives in the catalog, not here: an app says which extensions it opens,
    /// so there is no table of types to keep up to date, and a new viewer in the catalog is a
    /// new opener without a Conch release. Nothing here touches Avalonia.
    /// </remarks>
    public sealed class FileOpeners
    {
        /// <summary>How much of a file is read to decide whether it is text.</summary>
        public const int SniffBytes = 8192;

        private readonly Func<IEnumerable<ToolViewModel>> _tools;
        private readonly Func<string, string?> _readChoice;
        private readonly Func<string, bool> _looksLikeText;

        /// <param name="tools">The catalog.</param>
        /// <param name="readChoice">The app id chosen for an extension, or null.</param>
        /// <param name="looksLikeText">Whether a file is text; <see cref="LooksLikeText"/> outside tests.</param>
        public FileOpeners(
            Func<IEnumerable<ToolViewModel>> tools,
            Func<string, string?> readChoice,
            Func<string, bool>? looksLikeText = null)
        {
            _tools = tools;
            _readChoice = readChoice;
            _looksLikeText = looksLikeText ?? LooksLikeText;
        }

        /// <summary>
        /// The type a file or link is opened by. For a file, its last extension, lower-cased,
        /// with its dot -- so <c>notes.tar.gz</c> is <c>.gz</c>; empty for a file with none. For
        /// a link, its scheme with the colon: <c>https:</c>.
        /// </summary>
        /// <remarks>
        /// Links ride on the same machinery so a browser is just an app that opens
        /// <c>https:</c>, chosen the way a JSON viewer is.
        /// </remarks>
        public static string Extension(string path)
        {
            if (IsLink(path))
            {
                return path[..(path.IndexOf("://", StringComparison.Ordinal) + 1)].ToLowerInvariant();
            }

            return Path.GetExtension(path).ToLowerInvariant();
        }

        /// <summary>True for <c>scheme://...</c>.</summary>
        public static bool IsLink(string path)
        {
            var separator = path.IndexOf("://", StringComparison.Ordinal);
            return separator > 1 && path[..separator].All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.');
        }

        /// <summary>The installed apps that open <paramref name="path"/>'s type, by name.</summary>
        public IReadOnlyList<ToolViewModel> CandidatesFor(string path) => CandidatesForExtension(Extension(path));

        /// <summary>The installed apps that open <paramref name="extension"/>, by name.</summary>
        public IReadOnlyList<ToolViewModel> CandidatesForExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
            {
                return [];
            }

            return _tools()
                .Where(t => t.IsInstalled && t.IsAvailableHere
                    && t.Opens.Any(o => string.Equals(o.Trim(), extension, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Every extension some installed app opens, with its candidates: what Preferences
        /// offers a choice for when there is more than one.
        /// </summary>
        public IReadOnlyList<(string Extension, IReadOnlyList<ToolViewModel> Candidates)> KnownExtensions()
            => _tools()
                .Where(t => t.IsInstalled && t.IsAvailableHere)
                .SelectMany(t => t.Opens)
                .Select(o => o.Trim().ToLowerInvariant())
                .Where(o => (o.StartsWith('.') && o.Length > 1) || (o.EndsWith(':') && o.Length > 1))
                .Distinct()
                .Order(StringComparer.Ordinal)
                .Select(e => (e, CandidatesForExtension(e)))
                .ToList();

        /// <summary>
        /// What opens <paramref name="path"/>.
        /// </summary>
        /// <remarks>
        /// In order: the app chosen for the extension, while it is still installed; the app
        /// that opens it, or the first by name when several do; the text editor, for a file
        /// nothing claims that turns out to be text, which is what Files always did; and
        /// otherwise nothing, so Files can ask rather than hand a binary to nano.
        /// </remarks>
        public FileOpenChoice Choose(string path)
        {
            var candidates = CandidatesFor(path);

            if (candidates.Count > 0)
            {
                var chosenId = _readChoice(Extension(path));
                var chosen = candidates.FirstOrDefault(t => string.Equals(t.Id, chosenId, StringComparison.OrdinalIgnoreCase));
                return new FileOpenChoice(FileOpenKind.App, chosen ?? candidates[0]);
            }

            // A link nothing opens has no contents here to judge.
            return !IsLink(path) && _looksLikeText(path)
                ? new FileOpenChoice(FileOpenKind.TextEditor)
                : new FileOpenChoice(FileOpenKind.Ask);
        }

        /// <summary>
        /// True when <paramref name="path"/> reads as text: no NUL byte and valid UTF-8 in its
        /// first <see cref="SniffBytes"/>.
        /// </summary>
        /// <remarks>
        /// The test git and <c>file</c> use. An empty file is text -- it is about to be written --
        /// and one that cannot be read is not, so it is asked about rather than handed to an
        /// editor that will fail on it too.
        /// </remarks>
        public static bool LooksLikeText(string path)
        {
            byte[] buffer;
            int read;
            try
            {
                using var stream = File.OpenRead(path);
                buffer = new byte[SniffBytes];
                read = stream.Read(buffer, 0, buffer.Length);
            }
            catch (Exception)
            {
                return false;
            }

            return LooksLikeText(buffer.AsSpan(0, read));
        }

        /// <inheritdoc cref="LooksLikeText(string)"/>
        public static bool LooksLikeText(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IndexOf((byte)0) >= 0)
            {
                return false;
            }

            try
            {
                // flush: false, so a character cut in half by the end of the sample is not
                // mistaken for invalid UTF-8.
                new UTF8Encoding(false, throwOnInvalidBytes: true).GetDecoder()
                    .GetCharCount(bytes, flush: false);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
