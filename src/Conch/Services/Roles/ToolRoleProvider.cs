using Conch.ViewModel;

namespace Conch.Services.Roles
{
    /// <summary>
    /// A role served by an app from the catalog.
    /// </summary>
    /// <remarks>
    /// Launching goes through the same path as launching the app by hand, so everything
    /// already settled there -- the login-shell PATH, argument escaping, keeping a failed
    /// window on screen -- applies here too rather than being reimplemented.
    /// </remarks>
    public sealed class ToolRoleProvider : IRoleProvider
    {
        private readonly ToolViewModel _tool;
        private readonly Action<ToolViewModel, IReadOnlyList<string>?> _launch;

        public ToolRoleProvider(ToolViewModel tool, Action<ToolViewModel, IReadOnlyList<string>?> launch)
        {
            _tool = tool;
            _launch = launch;
        }

        public string Id => _tool.Id;

        public string Name => _tool.Name;

        public bool IsBuiltIn => false;

        public bool IsAvailable => _tool.IsInstalled;

        /// <summary>
        /// Launches the app, handing <paramref name="argument"/> to the first placeholder in
        /// its <c>args</c> template.
        /// </summary>
        /// <remarks>
        /// The argument is passed as a template value rather than appended to the command, so
        /// a registration decides where the path goes and an app that takes none is unaffected.
        /// </remarks>
        public void Invoke(string? argument)
            => _launch(_tool, argument is null ? null : new[] { argument });
    }
}
