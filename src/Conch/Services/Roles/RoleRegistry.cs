using Conch.Utilities;

namespace Conch.Services.Roles
{
    /// <summary>
    /// Answers "what serves this role", and serves it.
    /// </summary>
    /// <remarks>
    /// Candidates come from two places -- windows built into Conch, and installed apps whose
    /// registration declares the role -- and the shell treats them alike. Settings stores only
    /// the user's <em>choice</em>, an id, never an implementation, which is what lets a chosen
    /// app be uninstalled without leaving the role broken.
    /// </remarks>
    public sealed class RoleRegistry
    {
        private const string LogCategory = "Roles";

        private readonly List<IRoleProvider> _builtIns = new();
        private readonly Dictionary<string, List<string>> _builtInRoles =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Func<string, IEnumerable<IRoleProvider>> _toolProviders;
        private readonly Func<string, string?> _readChoice;
        private readonly Func<string, string?> _preferredDefault;

        /// <param name="toolProviders">Installed apps declaring a given role.</param>
        /// <param name="readChoice">The stored choice for a role, or null when unset.</param>
        /// <param name="preferredDefault">
        /// The app id a role should default to on this machine, or null for none. See
        /// <see cref="ShellRoles.PreferredDefault"/>.
        /// </param>
        public RoleRegistry(
            Func<string, IEnumerable<IRoleProvider>> toolProviders,
            Func<string, string?> readChoice,
            Func<string, string?>? preferredDefault = null)
        {
            _toolProviders = toolProviders;
            _readChoice = readChoice;
            _preferredDefault = preferredDefault ?? (_ => null);
        }

        /// <summary>
        /// Offers <paramref name="provider"/> as the built-in for <paramref name="role"/>.
        /// </summary>
        public void RegisterBuiltIn(string role, IRoleProvider provider)
        {
            if (!_builtIns.Any(p => p.Id == provider.Id))
            {
                _builtIns.Add(provider);
            }

            if (!_builtInRoles.TryGetValue(role, out var ids))
            {
                _builtInRoles[role] = ids = new List<string>();
            }

            if (!ids.Contains(provider.Id))
            {
                ids.Add(provider.Id);
            }
        }

        /// <summary>
        /// Everything that could serve <paramref name="role"/>, built-ins first.
        /// </summary>
        /// <remarks>
        /// Built-ins lead because they are the default and always work; an uninstalled app is
        /// still listed, so the choice does not vanish from Settings the moment it stops being
        /// available and leave the user wondering what they had picked.
        /// </remarks>
        public IReadOnlyList<IRoleProvider> CandidatesFor(string role)
        {
            var candidates = new List<IRoleProvider>();

            if (_builtInRoles.TryGetValue(role, out var builtInIds))
            {
                candidates.AddRange(builtInIds
                    .Select(id => _builtIns.First(p => p.Id == id)));
            }

            // The preferred default leads the apps, and the order does the rest: Resolve takes
            // the first available candidate when nothing is chosen, and Settings shows that same
            // first available one, so the two cannot disagree about what the default is. A stable
            // sort, so the other apps keep the catalog's order.
            var preferred = _preferredDefault(role);
            candidates.AddRange(_toolProviders(role)
                .OrderBy(p => string.Equals(p.Id, preferred, StringComparison.OrdinalIgnoreCase) ? 0 : 1));
            return candidates;
        }

        /// <summary>
        /// The provider that should serve <paramref name="role"/>, or null when nothing can.
        /// </summary>
        /// <remarks>
        /// The stored choice wins while it is still available. When it is not -- the app was
        /// uninstalled, or the registration left the catalog -- the role falls back rather than
        /// failing, because a file explorer that stops opening because something unrelated was
        /// removed is a worse outcome than quietly opening a different one. The fallback is
        /// logged, so the change is at least traceable.
        /// </remarks>
        public IRoleProvider? Resolve(string role)
        {
            var candidates = CandidatesFor(role);
            if (candidates.Count == 0)
            {
                return null;
            }

            var chosenId = _readChoice(role);
            if (!string.IsNullOrEmpty(chosenId))
            {
                var chosen = candidates.FirstOrDefault(p => p.Id == chosenId);
                if (chosen is { IsAvailable: true })
                {
                    return chosen;
                }

                Log.Info(LogCategory,
                    chosen is null
                        ? $"'{chosenId}' is set for {role} but is not in the catalog; falling back."
                        : $"'{chosenId}' is set for {role} but is not installed; falling back.");
            }

            return candidates.FirstOrDefault(p => p.IsAvailable);
        }

        /// <summary>
        /// Serves <paramref name="role"/>, returning false when nothing is available for it.
        /// </summary>
        /// <remarks>
        /// False is an ordinary outcome, not a failure: a role with no candidate is the normal
        /// state of, say, network configuration on a machine with no network tool installed.
        /// The caller offers Settings rather than reporting an error.
        /// </remarks>
        public bool TryInvoke(string role, string? argument = null)
        {
            var provider = Resolve(role);
            if (provider == null)
            {
                Log.Info(LogCategory, $"Nothing available to serve {role}.");
                return false;
            }

            Log.Info(LogCategory, $"{role} -> {provider.Id}");
            provider.Invoke(argument);
            return true;
        }
    }
}
