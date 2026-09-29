using Conch.Utilities;
using Conch.ViewModel;

namespace Conch.Services
{
    /// <summary>
    /// What has to be installed, in what order, for one app to work.
    /// </summary>
    /// <remarks>
    /// Prerequisites are ordinary registrations, so a plan is just a list of them ending with
    /// the app itself. Anything already installed is left out: the point of declaring a
    /// prerequisite is that it arrives once, not on every install that mentions it.
    /// </remarks>
    public sealed record InstallPlan(
        IReadOnlyList<ToolViewModel> Steps,
        IReadOnlyList<string> Missing)
    {
        /// <summary>Prerequisites that have to be installed first, in order.</summary>
        public IEnumerable<ToolViewModel> Prerequisites => Steps.Take(Math.Max(0, Steps.Count - 1));

        /// <summary>True when a declared prerequisite names nothing in the catalog.</summary>
        public bool HasUnknownRequirements => Missing.Count > 0;

        /// <summary>
        /// Works out what installing <paramref name="tool"/> actually involves.
        /// </summary>
        /// <remarks>
        /// Depth first, so a prerequisite's own prerequisites come before it. A cycle is
        /// ignored rather than treated as an error: it is a mistake in the catalog, not
        /// something the user can act on, and refusing to install would turn a bad edit to one
        /// registration into a broken app for everybody.
        /// </remarks>
        public static InstallPlan For(ToolViewModel tool, IEnumerable<ToolViewModel> catalog)
        {
            var byId = new Dictionary<string, ToolViewModel>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in catalog)
            {
                byId[candidate.Id] = candidate;
            }

            var steps = new List<ToolViewModel>();
            var missing = new List<string>();
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(ToolViewModel current)
            {
                if (!visiting.Add(current.Id))
                {
                    return;
                }

                foreach (var id in current.Requires)
                {
                    if (!byId.TryGetValue(id, out var requirement))
                    {
                        if (!missing.Contains(id, StringComparer.OrdinalIgnoreCase))
                        {
                            missing.Add(id);
                        }

                        continue;
                    }

                    Visit(requirement);
                }

                // Already there is the common case once a toolchain has been installed once,
                // and reinstalling it on every app that needs it would be both slow and a
                // surprising thing to watch happen.
                if (!current.IsInstalled && steps.All(s => !string.Equals(s.Id, current.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    steps.Add(current);
                }
            }

            Visit(tool);

            // The app goes last even when already installed -- asking to install something is
            // a request to install it, and refusing because detection says it is present would
            // remove the only way to repair a broken install.
            if (steps.All(s => !string.Equals(s.Id, tool.Id, StringComparison.OrdinalIgnoreCase)))
            {
                steps.Add(tool);
            }

            if (missing.Count > 0)
            {
                Log.Warning("Install", $"{tool.Id} requires {string.Join(", ", missing)}, which the catalog does not have.");
            }

            return new InstallPlan(steps, missing);
        }
    }
}
