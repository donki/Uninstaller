using Uninstaller.Models;

namespace Uninstaller.Services;

/// <summary>
/// Mapa de rectangulos (treemap) del arbol de carpetas, como el de TreeSize: cada carpeta es un
/// rectangulo proporcional a su tamaño, subdividido con sus hijas hasta dos niveles. Reparto
/// «squarified» (Bruls, Huizing y van Wijk): los rectangulos salen lo mas cuadrados posible, que
/// es lo que los hace legibles. Se dibuja en un GraphicsView y responde al toque.
/// </summary>
public sealed class TreemapDrawable : IDrawable
{
    /// <summary>Rectangulo ya colocado, para pintar y para saber que hay bajo el dedo.</summary>
    public sealed record Tile(FolderNode Node, RectF Bounds, int Level, Color Fill);

    private readonly List<Tile> _tiles = [];
    private static readonly Color[] Palette =
    [
        Color.FromArgb("#3525CD"), Color.FromArgb("#0E9F6E"), Color.FromArgb("#D97706"), Color.FromArgb("#DC2626"),
        Color.FromArgb("#0284C7"), Color.FromArgb("#7C3AED"), Color.FromArgb("#DB2777"), Color.FromArgb("#65A30D"),
        Color.FromArgb("#0891B2"), Color.FromArgb("#B45309"), Color.FromArgb("#4F46E5"), Color.FromArgb("#059669"),
    ];

    public FolderNode? Root { get; set; }
    public FolderNode? Selected { get; set; }
    public bool Dark { get; set; }
    public Func<long, string>? FormatSize { get; set; }

    public IReadOnlyList<Tile> Tiles => _tiles;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        _tiles.Clear();
        canvas.FillColor = Dark ? Color.FromArgb("#14161F") : Color.FromArgb("#F3F4F8");
        canvas.FillRectangle(dirtyRect);
        if (Root is null || Root.Size <= 0)
            return;

        var area = new RectF(dirtyRect.X + 2, dirtyRect.Y + 2, dirtyRect.Width - 4, dirtyRect.Height - 4);
        Layout(Root, area, 0, null);

        canvas.FontSize = 12;
        foreach (var tile in _tiles.OrderBy(t => t.Level))
        {
            var r = tile.Bounds;
            if (r.Width < 1 || r.Height < 1)
                continue;
            canvas.FillColor = tile.Level == 0 ? tile.Fill : tile.Fill.WithAlpha(0.55f);
            canvas.FillRoundedRectangle(r, 3);
            canvas.StrokeColor = Dark ? Color.FromArgb("#14161F") : Colors.White;
            canvas.StrokeSize = tile.Level == 0 ? 2 : 1;
            canvas.DrawRoundedRectangle(r, 3);
            if (ReferenceEquals(tile.Node, Selected))
            {
                canvas.StrokeColor = Colors.White;
                canvas.StrokeSize = 3;
                canvas.DrawRoundedRectangle(r, 3);
            }
            // Etiqueta si cabe: nombre y, con sitio, el tamaño.
            if (tile.Level == 0 && r.Width > 60 && r.Height > 22)
            {
                canvas.FontColor = Colors.White;
                canvas.FontSize = 12;
                var size = FormatSize?.Invoke(tile.Node.Size) ?? string.Empty;
                var label = r.Height > 40 ? tile.Node.Display : $"{tile.Node.Display} · {size}";
                canvas.DrawString(label, new RectF(r.X + 6, r.Y + 4, r.Width - 12, 16), HorizontalAlignment.Left, VerticalAlignment.Top);
                if (r.Height > 40)
                    canvas.DrawString(size, new RectF(r.X + 6, r.Y + 20, r.Width - 12, 16), HorizontalAlignment.Left, VerticalAlignment.Top);
            }
            else if (tile.Level == 1 && r.Width > 70 && r.Height > 30)
            {
                canvas.FontColor = Colors.White.WithAlpha(0.9f);
                canvas.FontSize = 11;
                canvas.DrawString(tile.Node.Display, new RectF(r.X + 4, r.Bottom - 18, r.Width - 8, 14), HorizontalAlignment.Left, VerticalAlignment.Top);
            }
        }
    }

    /// <summary>Que carpeta hay en ese punto: la mas profunda que lo contiene.</summary>
    public FolderNode? HitTest(PointF point)
    {
        Tile? best = null;
        foreach (var t in _tiles)
        {
            if (t.Bounds.Contains(point) && (best is null || t.Level > best.Level))
                best = t;
        }
        return best?.Node;
    }

    private void Layout(FolderNode parent, RectF area, int level, Color? inherited)
    {
        if (level > 1 || area.Width < 8 || area.Height < 8)
            return;
        // Hijas con tamaño mas «lo suelto» (los ficheros propios) como un bloque sin nombre.
        var items = parent.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).ToList();
        if (items.Count == 0)
            return;
        var total = (double)items.Sum(c => c.Size);
        var ownFiles = parent.Size - items.Sum(c => c.Size);
        if (ownFiles > 0)
            total += ownFiles;
        var inner = level == 0 ? area : new RectF(area.X + 3, area.Y + 3, area.Width - 6, area.Height - 6);
        var rects = Squarify(items.Select(c => c.Size / total).ToList(), inner);
        for (var i = 0; i < items.Count; i++)
        {
            var color = inherited ?? Palette[i % Palette.Length];
            var r = rects[i];
            _tiles.Add(new Tile(items[i], r, level, color));
            if (level == 0)
                Layout(items[i], new RectF(r.X, r.Y + (r.Height > 44 ? 22 : 0), r.Width, r.Height - (r.Height > 44 ? 22 : 0)), level + 1, color);
        }
    }

    /// <summary>Reparto squarified de fracciones (suman ≤ 1) dentro de un rectangulo.</summary>
    private static List<RectF> Squarify(List<double> fractions, RectF bounds)
    {
        var result = new List<RectF>(new RectF[fractions.Count]);
        var indices = Enumerable.Range(0, fractions.Count).ToList();
        var x = bounds.X; var y = bounds.Y; var w = bounds.Width; var h = bounds.Height;
        var totalArea = w * h;
        var remaining = fractions.Sum();
        var row = new List<int>();
        var start = 0;
        while (start < indices.Count)
        {
            var shortSide = Math.Min(w, h);
            row.Clear();
            var rowArea = 0.0;
            var i = start;
            double worst = double.MaxValue;
            while (i < indices.Count)
            {
                var a = fractions[indices[i]] / remaining * (w * h);
                var candidate = Worst(row.Select(k => fractions[indices[k]] / remaining * (w * h)).Append(a).ToList(), shortSide);
                if (row.Count > 0 && candidate > worst)
                    break;
                row.Add(i);
                rowArea += a;
                worst = candidate;
                i++;
            }
            // Coloca la fila a lo largo del lado corto.
            if (w >= h)
            {
                var rowWidth = (float)(rowArea / h);
                var cy = y;
                foreach (var k in row)
                {
                    var a = fractions[indices[k]] / remaining * (w * h);
                    var rh = (float)(a / rowWidth);
                    result[indices[k]] = new RectF(x, cy, rowWidth, rh);
                    cy += rh;
                }
                x += rowWidth; w -= rowWidth;
            }
            else
            {
                var rowHeight = (float)(rowArea / w);
                var cx = x;
                foreach (var k in row)
                {
                    var a = fractions[indices[k]] / remaining * (w * h);
                    var rw = (float)(a / rowHeight);
                    result[indices[k]] = new RectF(cx, y, rw, rowHeight);
                    cx += rw;
                }
                y += rowHeight; h -= rowHeight;
            }
            var used = row.Sum(k => fractions[indices[k]]);
            remaining -= used;
            start = i;
            if (w <= 0 || h <= 0 || remaining <= 0)
            {
                for (var j = start; j < indices.Count; j++)
                    result[indices[j]] = new RectF(x, y, 0, 0);
                break;
            }
        }
        return result;
    }

    private static double Worst(List<double> areas, double side)
    {
        if (areas.Count == 0)
            return double.MaxValue;
        var sum = areas.Sum();
        var max = areas.Max();
        var min = areas.Min();
        var s2 = side * side;
        return Math.Max(s2 * max / (sum * sum), sum * sum / (s2 * min));
    }
}
