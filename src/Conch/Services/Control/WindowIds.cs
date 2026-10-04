using System.Runtime.CompilerServices;

namespace Conch.Services.Control
{
    /// <summary>
    /// Short ids for windows -- <c>w1</c>, <c>w2</c> -- stable for as long as the window lives.
    /// </summary>
    /// <remarks>
    /// What a script names a window by. Handed out on first ask and never reused within a
    /// session, so a script holding the id of a window that has closed is told it is gone
    /// rather than acting on whichever window took its place. Weakly held: a closed window is
    /// not kept alive by having had an id.
    /// </remarks>
    public sealed class WindowIds
    {
        private readonly ConditionalWeakTable<object, string> _ids = new();
        private int _next;

        /// <summary>The id of <paramref name="window"/>, giving it one if it has none.</summary>
        public string IdOf(object window)
            => _ids.GetValue(window, _ => "w" + Interlocked.Increment(ref _next));

        /// <summary>The window among <paramref name="windows"/> with <paramref name="id"/>, or null.</summary>
        public T? Find<T>(IEnumerable<T> windows, string id) where T : class
            => windows.FirstOrDefault(w => string.Equals(IdOf(w), id, StringComparison.OrdinalIgnoreCase));
    }
}
