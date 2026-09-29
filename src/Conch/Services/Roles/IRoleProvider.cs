namespace Conch.Services.Roles
{
    /// <summary>
    /// Something that can serve a shell role: either a window built into Conch or an
    /// installed app from the catalog.
    /// </summary>
    /// <remarks>
    /// The point of the interface is that the shell never has to know which it got. Asking
    /// for <see cref="ShellRoles.FileExplorer"/> opens the built-in Files window or launches
    /// ranger, depending only on what the user chose in Settings, and the caller is written
    /// once either way.
    /// </remarks>
    public interface IRoleProvider
    {
        /// <summary>
        /// Stable identity, stored in settings as the user's choice. Built-ins use a
        /// <c>builtin.</c> prefix so they cannot collide with a registration id, which the
        /// schema restricts to lowercase letters, digits and dots.
        /// </summary>
        string Id { get; }

        /// <summary>Name shown in Settings.</summary>
        string Name { get; }

        /// <summary>True for a window built into Conch rather than a catalog app.</summary>
        bool IsBuiltIn { get; }

        /// <summary>
        /// Whether this can run right now. Built-ins always can; an app has to be installed.
        /// A provider that is merely unavailable is still offered in Settings, marked, so the
        /// list does not silently shrink when something is uninstalled.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Serve the role, optionally on <paramref name="argument"/> -- a folder for a file
        /// explorer, a file for an editor. A provider that takes no argument ignores it.
        /// </summary>
        void Invoke(string? argument);
    }
}
