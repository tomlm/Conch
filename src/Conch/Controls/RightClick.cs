using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Conch.Controls
{
    /// <summary>
    /// Opens a menu on right-click inside a list, choosing the row under the pointer first.
    /// </summary>
    /// <remarks>
    /// Avalonia opens a ContextMenu from a right-click the control itself handles, and inside a
    /// managed window it never gets the chance: the window manager takes the press to activate
    /// the window, so ContextRequested is never raised and right-click did nothing. Listening for
    /// the release with handledEventsToo sees it regardless.
    /// </remarks>
    public static class RightClick
    {
        /// <param name="menuFor">The menu for the row's item, or null for none.</param>
        public static void Attach(ListBox list, Func<object, ContextMenu?> menuFor)
        {
            list.AddHandler(InputElement.PointerReleasedEvent, (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Right)
                {
                    return;
                }

                var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
                if (row?.DataContext is not { } item)
                {
                    return;
                }

                list.SelectedItem = item;
                if (menuFor(item) is not { } menu)
                {
                    return;
                }

                e.Handled = true;

                // Once this click is over: opened during it, the menu was closed again by the
                // rest of the same click before it was ever drawn.
                Dispatcher.UIThread.Post(() => menu.Open(row), DispatcherPriority.Background);
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }
}
