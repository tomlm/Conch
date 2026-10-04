namespace Conch.Services.Control
{
    /// <summary>How <c>conch tile</c> arranges windows.</summary>
    public enum TileLayout
    {
        /// <summary>As square a grid as the count allows; a short last row is stretched to fill.</summary>
        Grid,

        /// <summary>Side by side.</summary>
        Columns,

        /// <summary>One above the other.</summary>
        Rows,
    }

    /// <summary>A window's place, in screen cells.</summary>
    public readonly record struct Cell(int X, int Y, int Width, int Height);

    /// <summary>
    /// Divides the desktop among windows. Pure arithmetic, so the layouts are tested without a
    /// screen.
    /// </summary>
    public static class Tiling
    {
        /// <summary>
        /// Places for <paramref name="count"/> windows filling <paramref name="width"/> by
        /// <paramref name="height"/>, in reading order. Columns and rows that do not divide
        /// evenly give the spare cells to the first ones, so nothing is left uncovered.
        /// </summary>
        public static IReadOnlyList<Cell> Layout(int count, int width, int height, TileLayout layout)
        {
            if (count <= 0 || width <= 0 || height <= 0)
            {
                return [];
            }

            var (columns, rows) = layout switch
            {
                TileLayout.Columns => (count, 1),
                TileLayout.Rows => (1, count),
                _ => GridShape(count),
            };

            var cells = new List<Cell>(count);
            var rowHeights = Split(height, rows);
            var y = 0;
            for (var row = 0; row < rows && cells.Count < count; row++)
            {
                // The last row may hold fewer windows; they share its whole width.
                var inRow = Math.Min(columns, count - cells.Count);
                var widths = Split(width, inRow);
                var x = 0;
                for (var column = 0; column < inRow; column++)
                {
                    cells.Add(new Cell(x, y, widths[column], rowHeights[row]));
                    x += widths[column];
                }

                y += rowHeights[row];
            }

            return cells;
        }

        private static (int Columns, int Rows) GridShape(int count)
        {
            var columns = (int)Math.Ceiling(Math.Sqrt(count));
            var rows = (int)Math.Ceiling(count / (double)columns);
            return (columns, rows);
        }

        private static int[] Split(int total, int parts)
        {
            var sizes = new int[parts];
            for (var i = 0; i < parts; i++)
            {
                sizes[i] = total / parts + (i < total % parts ? 1 : 0);
            }

            return sizes;
        }
    }
}
