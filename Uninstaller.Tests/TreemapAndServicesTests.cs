using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Uninstaller.Models;
using Uninstaller.Services;

namespace Uninstaller.Tests;

public class TreemapTests
{
    private static FolderNode Node(string name, long size, FolderNode? parent = null, params FolderNode[] children)
    {
        var node = new FolderNode { Name = name, FullPath = name, Parent = parent, Depth = parent is null ? 0 : parent.Depth + 1, Size = size };
        node.Children = [.. children];
        return node;
    }

    private static FolderNode Sample()
    {
        var root = Node("root", 1000);
        var a = Node("a", 600, root);
        var b = Node("b", 300, root);
        var c = Node("c", 0, root);   // vacia: no sale
        a.Children = [Node("a1", 400, a), Node("a2", 200, a)];
        b.Children = [Node("b1", 300, b)];
        root.Children = [a, b, c];   // 100 bytes sueltos en la raiz
        return root;
    }

    [Fact]
    public void Sin_raiz_o_vacia_no_hay_rectangulos()
    {
        var map = new TreemapDrawable();
        var canvas = new RecordingCanvas();
        map.Draw(canvas, new RectF(0, 0, 400, 300));
        Assert.Empty(map.Tiles);

        map.Root = Node("vacia", 0);
        map.Draw(canvas, new RectF(0, 0, 400, 300));
        Assert.Empty(map.Tiles);
        Assert.Null(map.HitTest(new PointF(10, 10)));
    }

    [Fact]
    public void Areas_proporcionales_dentro_del_lienzo_y_dos_niveles()
    {
        var map = new TreemapDrawable { Root = Sample(), FormatSize = b => $"{b} B" };
        var canvas = new RecordingCanvas();
        map.Draw(canvas, new RectF(0, 0, 402, 302));

        var top = map.Tiles.Where(t => t.Level == 0).ToList();
        Assert.Equal(["a", "b"], top.Select(t => t.Node.Name));
        var area = 398f * 298f;
        // Fallo arreglado: los 100 bytes sueltos de la raiz ocupan su 10 %; antes las hijas se
        // estiraban hasta llenarlo todo (a salia con el 67 % en vez del 60 %).
        Assert.Equal(0.6, top[0].Bounds.Width * top[0].Bounds.Height / area, 2);
        Assert.Equal(0.3, top[1].Bounds.Width * top[1].Bounds.Height / area, 2);
        foreach (var tile in map.Tiles)
        {
            Assert.InRange(tile.Bounds.Left, 1.99f, 400.01f);
            Assert.InRange(tile.Bounds.Right, 1.99f, 400.01f);
            Assert.InRange(tile.Bounds.Top, 1.99f, 300.01f);
            Assert.InRange(tile.Bounds.Bottom, 1.99f, 300.01f);
        }
        // Las hijas heredan el color de su madre y van dentro de ella.
        var a = top[0];
        var a1 = map.Tiles.Single(t => t.Node.Name == "a1");
        Assert.Equal(1, a1.Level);
        Assert.Equal(a.Fill, a1.Fill);
        Assert.True(a.Bounds.Contains(a1.Bounds.Center));
        Assert.DoesNotContain(map.Tiles, t => t.Level > 1);

        // Etiquetas escritas para los grandes.
        Assert.Contains(canvas.Strings, s => s.StartsWith("a", StringComparison.Ordinal));
        Assert.Contains(canvas.Strings, s => s.Contains("600 B"));
    }

    [Fact]
    public void HitTest_da_la_mas_profunda()
    {
        var map = new TreemapDrawable { Root = Sample() };
        map.Draw(new RecordingCanvas(), new RectF(0, 0, 400, 300));

        var a1 = map.Tiles.Single(t => t.Node.Name == "a1");
        Assert.Equal("a1", map.HitTest(a1.Bounds.Center)!.Name);
        Assert.Null(map.HitTest(new PointF(-50, -50)));
    }

    [Fact]
    public void Seleccion_y_tema_oscuro_se_pintan()
    {
        var root = Sample();
        var map = new TreemapDrawable { Root = root, Selected = root.Children[0], Dark = true };
        var canvas = new RecordingCanvas();
        map.Draw(canvas, new RectF(0, 0, 400, 300));
        Assert.True(canvas.StrokeWidths.Contains(3));
        Assert.NotEmpty(map.Tiles);
    }

    [Fact]
    public void Lienzo_estrecho_y_bajo()
    {
        // Mas alto que ancho (filas a lo ancho) y rectangulos pequeños sin etiqueta.
        var map = new TreemapDrawable { Root = Sample() };
        map.Draw(new RecordingCanvas(), new RectF(0, 0, 60, 400));
        Assert.Equal(2, map.Tiles.Count(t => t.Level == 0));

        map.Draw(new RecordingCanvas(), new RectF(0, 0, 400, 30));   // poco alto: sin segundo nivel con cabecera
        Assert.NotEmpty(map.Tiles);

        map.Draw(new RecordingCanvas(), new RectF(0, 0, 5, 5));      // demasiado pequeño
        Assert.Empty(map.Tiles);
    }

    [Fact]
    public void Muchas_carpetas_parecidas()
    {
        var root = Node("root", 0);
        var children = Enumerable.Range(1, 30).Select(i => Node("n" + i, 1000 + i * 37 % 500, root)).ToList();
        root.Children = children;
        root.Size = children.Sum(c => c.Size);
        var map = new TreemapDrawable { Root = root };

        map.Draw(new RecordingCanvas(), new RectF(0, 0, 800, 500));

        var tiles = map.Tiles.Where(t => t.Level == 0).ToList();
        Assert.Equal(30, tiles.Count);
        var total = tiles.Sum(t => t.Bounds.Width * t.Bounds.Height);
        Assert.Equal(796 * 496, total, 0);
        // squarified: nada de tiras finisimas
        Assert.All(tiles, t => Assert.True(Math.Max(t.Bounds.Width / t.Bounds.Height, t.Bounds.Height / t.Bounds.Width) < 6));
    }

    /// <summary>Lienzo que no pinta: apunta lo que se le pide.</summary>
    private sealed class RecordingCanvas : ICanvas
    {
        public List<string> Strings { get; } = [];
        public HashSet<float> StrokeWidths { get; } = [];

        public float DisplayScale { get; set; } = 1;
        public float StrokeSize { get => 0; set => StrokeWidths.Add(value); }
        public float MiterLimit { set { } }
        public Color StrokeColor { set { } }
        public LineCap StrokeLineCap { set { } }
        public LineJoin StrokeLineJoin { set { } }
        public float[] StrokeDashPattern { set { } }
        public float StrokeDashOffset { set { } }
        public Color FillColor { set { } }
        public Color FontColor { set { } }
        public IFont Font { set { } }
        public float FontSize { set { } }
        public float Alpha { set { } }
        public bool Antialias { set { } }
        public BlendMode BlendMode { set { } }

        public void DrawString(string value, float x, float y, HorizontalAlignment horizontalAlignment) => Strings.Add(value);
        public void DrawString(string value, float x, float y, float width, float height, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment, TextFlow textFlow = TextFlow.ClipBounds, float lineSpacingAdjustment = 0) => Strings.Add(value);
        public void DrawText(Microsoft.Maui.Graphics.Text.IAttributedText value, float x, float y, float width, float height) { }
        public void DrawLine(float x1, float y1, float x2, float y2) { }
        public void DrawArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise, bool closed) { }
        public void DrawRectangle(float x, float y, float width, float height) { }
        public void DrawRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
        public void DrawEllipse(float x, float y, float width, float height) { }
        public void DrawPath(PathF path) { }
        public void DrawImage(Microsoft.Maui.Graphics.IImage image, float x, float y, float width, float height) { }
        public void FillArc(float x, float y, float width, float height, float startAngle, float endAngle, bool clockwise) { }
        public void FillRectangle(float x, float y, float width, float height) { }
        public void FillRoundedRectangle(float x, float y, float width, float height, float cornerRadius) { }
        public void FillEllipse(float x, float y, float width, float height) { }
        public void FillPath(PathF path, WindingMode windingMode) { }
        public void SubtractFromClip(float x, float y, float width, float height) { }
        public void ClipPath(PathF path, WindingMode windingMode = WindingMode.NonZero) { }
        public void ClipRectangle(float x, float y, float width, float height) { }
        public void Translate(float tx, float ty) { }
        public void Rotate(float degrees, float x, float y) { }
        public void Rotate(float degrees) { }
        public void Scale(float sx, float sy) { }
        public void ConcatenateTransform(System.Numerics.Matrix3x2 transform) { }
        public void SaveState() { }
        public bool RestoreState() => true;
        public void ResetState() { }
        public void SetShadow(SizeF offset, float blur, Color color) { }
        public void SetFillPaint(Paint paint, RectF rectangle) { }
        public void SetFillImage(Microsoft.Maui.Graphics.IImage image) { }
        public SizeF GetStringSize(string value, IFont font, float fontSize) => new(value.Length * fontSize / 2, fontSize);
        public SizeF GetStringSize(string value, IFont font, float fontSize, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment) => GetStringSize(value, font, fontSize);
    }
}

public sealed partial class LocalizationTests : IDisposable
{
    private readonly CultureInfo _ui = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _ui;

    private static Dictionary<string, string> Table(string name) =>
        (Dictionary<string, string>)typeof(LocalizationService).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private sealed class Settings : ISettingsService
    {
        public string Language { get; set; } = "";
        public bool ShowSystemApps { get; set; }
        public string SortMode { get; set; } = "install";
        public bool TrayOnMinimize { get; set; }
    }

    [Fact]
    public void Mismas_claves_y_huecos_en_los_dos_idiomas()
    {
        var en = Table("English");
        var es = Table("Spanish");
        Assert.Empty(en.Keys.Except(es.Keys));
        Assert.Empty(es.Keys.Except(en.Keys));
        foreach (var (key, value) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), $"en:{key}");
            Assert.False(string.IsNullOrWhiteSpace(es[key]), $"es:{key}");
            Assert.True(Holes(value).SetEquals(Holes(es[key])), $"{key}: huecos distintos");
        }
    }

    private static HashSet<string> Holes(string text) => Hole().Matches(text).Select(m => m.Value).ToHashSet();

    [GeneratedRegex(@"\{\d+[^}]*\}")]
    private static partial Regex Hole();

    [Fact]
    public void Idioma_elegido_respaldo_y_clave_desconocida()
    {
        var service = new LocalizationService(new Settings { Language = "es" }, NullLogger<LocalizationService>.Instance);
        var changes = 0;
        service.LanguageChanged += (_, _) => changes++;

        Assert.Equal("es", service.CurrentLanguage);
        Assert.Equal("es", service.CurrentCulture.Name);
        Assert.Equal(Table("Spanish")["DiskAge1"], service["DiskAge1"]);
        Assert.Equal("NoExiste", service["NoExiste"]);

        service.SetLanguage("es");   // el mismo: no avisa
        service.SetLanguage("fr");   // no soportado: ingles
        Assert.Equal(1, changes);
        Assert.Equal(Table("English")["DiskAge1"], service["DiskAge1"]);

        var spanish = Table("Spanish");
        service.SetLanguage("es");
        var saved = spanish["DiskAge1"];
        spanish.Remove("DiskAge1");
        try
        {
            Assert.Equal(Table("English")["DiskAge1"], service["DiskAge1"]);
        }
        finally
        {
            spanish["DiskAge1"] = saved;
        }
    }

    [Theory]
    [InlineData("es-ES", "es")]
    [InlineData("en-GB", "en")]
    [InlineData("ja-JP", "en")]
    public void Sin_eleccion_sigue_al_sistema(string system, string expected)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(system);
        var service = new LocalizationService(new Settings { Language = "fr" }, NullLogger<LocalizationService>.Instance);
        service.SetLanguage(LocalizationService.SystemLanguage);
        Assert.Equal(expected, service.CurrentLanguage);
    }
}

public class SettingsServiceTests
{
    [Fact]
    public void Valores_por_defecto_y_guardado()
    {
        Preferences.Clear();
        var settings = new SettingsService();
        Assert.Equal(LocalizationService.SystemLanguage, settings.Language);
        Assert.False(settings.ShowSystemApps);
        Assert.True(settings.TrayOnMinimize);
        Assert.Equal("install", settings.SortMode);

        settings.Language = "es";
        settings.ShowSystemApps = true;
        settings.TrayOnMinimize = false;
        settings.SortMode = "size";
        var again = new SettingsService();
        Assert.Equal(("es", true, false, "size"), (again.Language, again.ShowSystemApps, again.TrayOnMinimize, again.SortMode));

        settings.Language = null!;
        settings.SortMode = null!;
        Assert.Equal((LocalizationService.SystemLanguage, "install"), (again.Language, again.SortMode));
    }
}
