using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Conch.ViewModel
{
    /// <summary>What a search result is, which decides where it ranks and how it is labelled.</summary>
    public enum SearchItemKind
    {
        /// <summary>Part of the shell that behaves like an app: Files, Terminal, App Manager.</summary>
        Shell,

        /// <summary>An installed app from the catalog.</summary>
        App,

        /// <summary>A settings page: Network, Display, Conch Preferences.</summary>
        Setting,

        /// <summary>Something the shell does: Logout, Restart.</summary>
        Command,

        /// <summary>The typed text, run as a command line.</summary>
        Run,
    }

    /// <summary>One thing the search box can start.</summary>
    /// <param name="Terms">Further text to match besides the title: id, command, keywords.</param>
    /// <param name="Invoke">What choosing it does.</param>
    public sealed record SearchItem(
        SearchItemKind Kind,
        string Title,
        string Detail,
        IReadOnlyList<string> Terms,
        Action Invoke)
    {
        /// <summary>The short tag shown beside the title.</summary>
        /// <remarks>Only settings are tagged: nearly everything else is an app, and a tag on
        /// every row is a column of noise.</remarks>
        public string KindLabel => Kind == SearchItemKind.Setting ? "settings" : string.Empty;
    }

    /// <summary>
    /// Backs the search box in the top bar: the one place to start anything.
    /// </summary>
    /// <remarks>
    /// Replaces two surfaces that did half the job each -- the Apps window (installed apps
    /// only) and the Command dialog (a typed command line only) -- with the Start-menu /
    /// Spotlight shape people already know: type, and the apps, settings and commands that
    /// match appear, with "Run ..." for anything else.
    ///
    /// Items carry their own action, supplied by the shell, so nothing here knows about
    /// windows or launching and all of it can be tested without Avalonia.
    /// </remarks>
    public partial class ShellSearchViewModel : ObservableObject
    {
        /// <summary>Enough to fill the drop-down; more is noise nobody scrolls to.</summary>
        public const int MaxResults = 50;

        private readonly Func<IEnumerable<SearchItem>> _fixedItems;
        private readonly Func<IEnumerable<SearchItem>> _apps;
        private readonly Func<string, Action> _run;

        /// <param name="fixedItems">The shell's own entries: built-ins, settings, commands.</param>
        /// <param name="apps">Installed apps, read fresh on every search.</param>
        /// <param name="run">What running a typed command line does.</param>
        public ShellSearchViewModel(
            Func<IEnumerable<SearchItem>> fixedItems,
            Func<IEnumerable<SearchItem>> apps,
            Func<string, Action> run)
        {
            _fixedItems = fixedItems;
            _apps = apps;
            _run = run;
        }

        public ObservableCollection<SearchItem> Results { get; } = new();

        [ObservableProperty]
        private string _query = string.Empty;

        [ObservableProperty]
        private SearchItem? _selectedResult;

        partial void OnQueryChanged(string value) => Refresh();

        /// <summary>Recomputes the results for the current query.</summary>
        public void Refresh()
        {
            Results.Clear();
            foreach (var item in Search(Query))
            {
                Results.Add(item);
            }

            SelectedResult = Results.FirstOrDefault();
        }

        /// <summary>
        /// The results for <paramref name="query"/>, best first.
        /// </summary>
        /// <remarks>
        /// Empty shows the shell's apps and every installed app, A to Z: what the Apps window
        /// used to show, without having to know what to type.
        ///
        /// Otherwise, by how well the title matches -- starts with it, has a word starting
        /// with it, contains it, or only matches in the other terms -- then apps before
        /// settings before commands, then alphabetically. "Run ..." comes last whenever no
        /// title is exactly what was typed, so a command line is always one Enter away and an
        /// app's own name never loses to it.
        /// </remarks>
        public IReadOnlyList<SearchItem> Search(string? query)
        {
            var text = (query ?? string.Empty).Trim();
            var apps = _apps().OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase);

            if (text.Length == 0)
            {
                return _fixedItems().Where(i => i.Kind == SearchItemKind.Shell)
                    .Concat(apps)
                    .Take(MaxResults)
                    .ToList();
            }

            var ranked = _fixedItems().Concat(apps)
                .Select(item => (item, rank: Rank(item, text)))
                .Where(x => x.rank >= 0)
                .OrderBy(x => x.rank)
                .ThenBy(x => KindOrder(x.item.Kind))
                .ThenBy(x => x.item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => x.item)
                .Take(MaxResults - 1)
                .ToList();

            if (!ranked.Any(i => string.Equals(i.Title, text, StringComparison.CurrentCultureIgnoreCase)))
            {
                ranked.Add(new SearchItem(SearchItemKind.Run, $"Run \"{text}\"", "in a terminal", [], _run(text)));
            }

            return ranked;
        }

        /// <summary>The shortest query that may match inside a word rather than at its start.</summary>
        public const int MinMidWordQuery = 3;

        /// <summary>How well <paramref name="item"/> matches: lower is better, -1 is no match.</summary>
        /// <remarks>
        /// By the start of words, as Start-menu and Spotlight search do: "na" finds nano, not
        /// Termi-na-l and Ma-na-ger. Inside a word only once the query is long enough to mean
        /// something there, and never inside descriptions, where two letters match everything.
        /// </remarks>
        public static int Rank(SearchItem item, string query)
        {
            var comparison = StringComparison.CurrentCultureIgnoreCase;

            if (item.Title.StartsWith(query, comparison))
            {
                return 0;
            }

            if (Words(item.Title).Any(w => w.StartsWith(query, comparison)))
            {
                return 1;
            }

            if (query.Length >= MinMidWordQuery && item.Title.Contains(query, comparison))
            {
                return 2;
            }

            if (item.Terms.SelectMany(Words).Any(w => w.StartsWith(query, comparison)))
            {
                return 3;
            }

            // Last, below keywords: a description mentions words in passing. "keyb" should find
            // Preferences, whose keyword is keyboard, before a chat client "driven entirely from
            // the keyboard".
            if (Words(item.Detail).Any(w => w.StartsWith(query, comparison)))
            {
                return 4;
            }

            return -1;
        }

        private static IEnumerable<string> Words(string text)
            => text.Split([' ', '-', '.', '_', '/', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries);

        private static int KindOrder(SearchItemKind kind) => kind switch
        {
            SearchItemKind.Shell => 0,
            SearchItemKind.App => 0,
            SearchItemKind.Setting => 1,
            SearchItemKind.Command => 2,
            _ => 3,
        };

        /// <summary>The search item for an installed app.</summary>
        public static SearchItem ForApp(ToolViewModel tool, Action launch)
            => new(SearchItemKind.App, tool.Name, tool.Description,
                [tool.Id, tool.Command, .. tool.Keywords], launch);
    }
}
