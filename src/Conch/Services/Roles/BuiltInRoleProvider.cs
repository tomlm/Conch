namespace Conch.Services.Roles
{
    /// <summary>
    /// A role served by a window built into Conch.
    /// </summary>
    /// <remarks>
    /// Holds a callback rather than a window type so this stays free of Avalonia and can be
    /// tested without a UI. The shell supplies the callback when it registers the built-in,
    /// which is also what keeps this class from needing to know how a window is shown.
    /// </remarks>
    public sealed class BuiltInRoleProvider : IRoleProvider
    {
        private readonly Action<string?> _open;
        private readonly Func<bool>? _isAvailable;

        /// <param name="isAvailable">
        /// Whether this can serve the role on this machine, or null for one that always can.
        /// </param>
        public BuiltInRoleProvider(string id, string name, Action<string?> open, Func<bool>? isAvailable = null)
        {
            Id = id;
            Name = name;
            _open = open;
            _isAvailable = isAvailable;
        }

        public string Id { get; }

        public string Name { get; }

        public bool IsBuiltIn => true;

        /// <summary>
        /// True unless the built-in says otherwise.
        /// </summary>
        /// <remarks>
        /// Shipping with the shell usually means there is nothing to install and nothing to
        /// check. Not always: the network window is drawn by Conch but speaks to
        /// NetworkManager, which does not exist on Windows or macOS, and a built-in that cannot
        /// work here should fall back like any other unavailable provider rather than open a
        /// window with nothing in it.
        /// </remarks>
        public bool IsAvailable => _isAvailable?.Invoke() ?? true;

        public void Invoke(string? argument) => _open(argument);
    }
}
