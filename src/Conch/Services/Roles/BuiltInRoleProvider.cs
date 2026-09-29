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

        public BuiltInRoleProvider(string id, string name, Action<string?> open)
        {
            Id = id;
            Name = name;
            _open = open;
        }

        public string Id { get; }

        public string Name { get; }

        public bool IsBuiltIn => true;

        /// <summary>Always. A built-in ships with the shell, so there is nothing to install.</summary>
        public bool IsAvailable => true;

        public void Invoke(string? argument) => _open(argument);
    }
}
