using System.Text;
using System.Text.RegularExpressions;

namespace Conch.Utilities
{
    /// <summary>
    /// One <c>%N</c> placeholder in a registration's <c>args</c> template.
    /// </summary>
    public sealed record ArgumentParameter(int Index, bool IsOptional);

    /// <summary>
    /// Parses and fills the <c>args</c> templates used by app registrations.
    /// </summary>
    /// <remarks>
    /// A template is a command-line fragment in which <c>%1</c>..<c>%9</c> stand for values
    /// supplied at launch. A trailing <c>?</c> (<c>%1?</c>) marks the placeholder optional; an
    /// optional placeholder left empty is dropped from the final argument list, while a required
    /// one left empty is an error.
    /// </remarks>
    public static partial class ArgumentTemplate
    {
        [GeneratedRegex(@"%([1-9])(\?)?")]
        private static partial Regex PlaceholderRegex { get; }

        /// <summary>
        /// The distinct placeholders in <paramref name="template"/>, ordered by index.
        /// </summary>
        public static IReadOnlyList<ArgumentParameter> GetParameters(string? template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return Array.Empty<ArgumentParameter>();
            }

            return PlaceholderRegex.Matches(template)
                .Select(m => new ArgumentParameter(int.Parse(m.Groups[1].Value), m.Groups[2].Success))
                .GroupBy(p => p.Index)
                // A placeholder is only required if every occurrence of it is required.
                .Select(g => new ArgumentParameter(g.Key, g.All(p => p.IsOptional)))
                .OrderBy(p => p.Index)
                .ToList();
        }

        /// <summary>
        /// True when <paramref name="template"/> needs values before the app can be launched.
        /// </summary>
        public static bool RequiresPrompt(string? template) => GetParameters(template).Count > 0;

        /// <summary>
        /// Builds the final argument list by substituting <paramref name="values"/> positionally
        /// into <paramref name="template"/>.
        /// </summary>
        /// <param name="values">
        /// Positional values; <c>values[0]</c> fills <c>%1</c>. Values beyond the highest
        /// placeholder are appended, so a one-placeholder template still accepts several files.
        /// </param>
        /// <remarks>
        /// The template is tokenized first and values are substituted into tokens, rather than
        /// pasted into the string and re-parsed. That keeps a value containing spaces as a single
        /// argument instead of letting it split itself apart.
        /// </remarks>
        public static bool TryBuild(string? template, IReadOnlyList<string> values, out List<string> args, out string? error)
        {
            args = new List<string>();
            error = null;

            var parameters = GetParameters(template);

            foreach (var parameter in parameters)
            {
                var supplied = parameter.Index <= values.Count ? values[parameter.Index - 1] : string.Empty;
                if (!parameter.IsOptional && string.IsNullOrWhiteSpace(supplied))
                {
                    error = $"Argument %{parameter.Index} is required.";
                    return false;
                }
            }

            foreach (var token in Tokenize(template ?? string.Empty))
            {
                var matches = PlaceholderRegex.Matches(token);
                if (matches.Count == 0)
                {
                    args.Add(token);
                    continue;
                }

                // A token that is nothing but a placeholder disappears when its value is empty;
                // one that embeds a placeholder (--file=%1) keeps its literal text.
                var wholeToken = matches.Count == 1 && matches[0].Length == token.Length;
                if (wholeToken)
                {
                    var index = int.Parse(matches[0].Groups[1].Value);
                    var value = index <= values.Count ? values[index - 1] : string.Empty;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        args.Add(value);
                    }
                    continue;
                }

                // An embedded placeholder with no value takes its whole token with it: a flag
                // like --file= is not a usable argument once the filename is gone. A required
                // placeholder cannot reach here empty; that already failed above.
                var complete = true;
                var replaced = PlaceholderRegex.Replace(token, m =>
                {
                    var index = int.Parse(m.Groups[1].Value);
                    var value = index <= values.Count ? values[index - 1] : string.Empty;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        complete = false;
                    }
                    return value;
                });

                if (complete && !string.IsNullOrWhiteSpace(replaced))
                {
                    args.Add(replaced);
                }
            }

            // Extra values beyond the template's placeholders trail the command, so that a
            // registration declaring "%1?" still opens several files.
            var highest = parameters.Count == 0 ? 0 : parameters[^1].Index;
            for (var i = highest; i < values.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    args.Add(values[i]);
                }
            }

            return true;
        }

        /// <summary>
        /// Splits a command-line fragment on whitespace, honouring double quotes.
        /// </summary>
        public static List<string> Tokenize(string commandLine)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;
            var hasToken = false;

            foreach (var c in commandLine)
            {
                if (c == '"')
                {
                    // Track that a token exists even if the quotes turn out to be empty,
                    // so an explicit "" is passed through as an empty argument.
                    inQuotes = !inQuotes;
                    hasToken = true;
                }
                else if (char.IsWhiteSpace(c) && !inQuotes)
                {
                    if (hasToken)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        hasToken = false;
                    }
                }
                else
                {
                    current.Append(c);
                    hasToken = true;
                }
            }

            if (hasToken)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }
    }
}
