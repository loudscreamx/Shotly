using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Shotly.Localization;

namespace Shotly.UI;

/// <summary>
/// Фирменный стиль Shotly (брендбук v1.0, палитра Ember): цвета, шрифты и знак Orbit.
/// Всё, что рисуется кодом (оверлей, панель, трей, уведомления), берёт цвета и шрифты отсюда,
/// а стили стандартных контролов лежат в Themes/Shotly.xaml и ссылаются на те же значения.
/// </summary>
public static class Brand
{
    // Палитра Ember.
    public static readonly Color Ember = Color.FromRgb(0xF2, 0x54, 0x1B);
    public static readonly Color Amber = Color.FromRgb(0xFF, 0xB0, 0x00);
    public static readonly Color Coal = Color.FromRgb(0x1C, 0x19, 0x17);
    public static readonly Color Cream = Color.FromRgb(0xFF, 0xF7, 0xED);
    public static readonly Color EmberDeep = Color.FromRgb(0xC2, 0x41, 0x0C);
    public static readonly Color Ash = Color.FromRgb(0x78, 0x71, 0x6C);

    // Тёмная тема брендбука: фон, поверхность, текст, приглушённый текст, линии, ссылки.
    public static readonly Color Night = Color.FromRgb(0x14, 0x11, 0x10);
    public static readonly Color Surface = Color.FromRgb(0x1E, 0x1A, 0x18);
    public static readonly Color Raised = Color.FromRgb(0x26, 0x21, 0x20);
    public static readonly Color Text = Color.FromRgb(0xF5, 0xEE, 0xE8);
    public static readonly Color Muted = Color.FromRgb(0xA8, 0x9F, 0x98);
    public static readonly Color Line = Color.FromRgb(0x2E, 0x28, 0x26);
    public static readonly Color Link = Color.FromRgb(0xFF, 0x7A, 0x45);

    public static readonly Brush EmberBrush = Solid(Ember);
    public static readonly Brush AmberBrush = Solid(Amber);
    public static readonly Brush CoalBrush = Solid(Coal);
    public static readonly Brush TextBrush = Solid(Text);
    public static readonly Brush MutedBrush = Solid(Muted);
    public static readonly Brush LineBrush = Solid(Line);

    /// <summary>
    /// Onest для текста интерфейса, Unbounded для заголовков. Шрифты встроены в exe (Assets/Fonts, лицензия OFL),
    /// а если их по какой-то причине не удалось загрузить, WPF возьмёт следующий шрифт из списка.
    /// <remarks>
    /// В фирменных шрифтах нет иероглифов, поэтому для китайского, японского и корейского сразу за ними
    /// идёт системный шрифт этого языка: латиница и цифры остаются в Onest, иероглифы — в правильном начертании
    /// (иначе Windows подставит первый попавшийся шрифт). Набор пересобирается при смене языка.
    /// </remarks>
    public static FontFamily TextFont => CurrentFonts.Text;
    public static FontFamily DisplayFont => CurrentFonts.Display;

    public static Typeface TextFace => CurrentFonts.Face;
    public static Typeface TextFaceMedium => CurrentFonts.FaceMedium;
    public static Typeface TextFaceSemiBold => CurrentFonts.FaceSemiBold;

    /// <summary>Ключ ресурса со шрифтом текста для стилей Themes/Shotly.xaml (DynamicResource: меняется вместе с языком).</summary>
    public const string TextFontKey = "Font.Text";

    private static FontSet? _fonts;

    private static FontSet CurrentFonts
    {
        get
        {
            var language = Loc.Current;
            if (_fonts is null || _fonts.Language != language)
                _fonts = new FontSet(language);
            return _fonts;
        }
    }

    private sealed class FontSet
    {
        public FontSet(LanguageInfo language)
        {
            Language = language;
            var local = language.WpfFont.Length > 0 ? language.WpfFont + ", " : string.Empty;
            Text = Font("Onest", local + "Segoe UI Variable Text, Segoe UI");
            Display = Font("Unbounded", local + "Segoe UI Variable Display, Segoe UI");
            Face = new Typeface(Text, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            FaceMedium = new Typeface(Text, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
            FaceSemiBold = new Typeface(Text, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        }

        public LanguageInfo Language { get; }
        public FontFamily Text { get; }
        public FontFamily Display { get; }
        public Typeface Face { get; }
        public Typeface FaceMedium { get; }
        public Typeface FaceSemiBold { get; }
    }

    private static readonly Lazy<DrawingImage> MarkImage = new(CreateMark);

    /// <summary>Знак Orbit (вектор, 256×256): верх Ember, низ Amber.</summary>
    public static ImageSource Mark => MarkImage.Value;

    private static FontFamily Font(string embedded, string fallback) =>
        new(new Uri("pack://application:,,,/Shotly;component/"), $"./Assets/Fonts/#{embedded}, {fallback}");

    public static SolidColorBrush Solid(Color color, byte alpha = 0xFF)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Тёмный заголовок окна в цвет фона. Windows 10 20H1+ понимает тёмный режим,
    /// Windows 11 ещё и цвет заголовка; на старых версиях вызов просто ничего не меняет.
    /// </summary>
    public static void ApplyDarkTitleBar(Window window)
    {
        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                int caption = Night.R | (Night.G << 8) | (Night.B << 16); // COLORREF: 0x00BBGGRR
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                int text = Text.R | (Text.G << 8) | (Text.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Без DWM заголовок останется системным.
            }
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Apply();
        else
            window.SourceInitialized += (_, _) => Apply();
    }

    /// <summary>
    /// Знак из брендбука. В SVG каждая половина обрезана маской со штрихом 6.06: так между половинами
    /// остаётся тонкий зазор. Здесь то же самое: из контура вычитается его обводка.
    /// </summary>
    private static DrawingImage CreateMark()
    {
        const string top = "M78.50 263.64C69.59 261.31 58.31 255.22 52.61 249.65C40.59 237.91 34.47 223.56 33.36 204.50C32.28 186.03 36.27 169.04 46.51 148.50C67.78 105.82 110.61 73.49 160.00 62.82C172.17 60.19 200.20 59.21 213.35 60.96C242.96 64.88 270.40 76.96 288.75 94.13C293.78 98.84 294.43 101.58 290.25 100.52C278.84 97.63 260.12 97.16 249.09 99.48C226.78 104.17 209.24 114.10 178.78 139.27C144.32 167.73 119.70 191.28 118.14 197.25C116.18 204.76 120.56 211.65 128.78 213.97C133.99 215.45 148.60 216.78 168.30 217.57C182.88 218.16 185.06 218.46 184.79 219.83C184.34 222.20 168.83 235.26 157.90 242.48C145.31 250.80 134.23 256.30 121.15 260.72C111.36 264.03 109.41 264.34 97.00 264.64C88.11 264.84 81.79 264.51 78.50 263.64Z";
        const string bottom = "M159.00 348.54C158.18 348.34 154.12 347.64 150.00 346.97C128.58 343.53 108.58 334.27 89.75 319.09C80.90 311.95 80.08 310.00 85.95 310.00C92.26 310.00 110.35 306.17 119.80 302.84C144.90 293.98 181.61 271.29 218.00 242.14C242.96 222.15 249.70 212.73 244.44 205.22C240.20 199.16 233.64 198.10 194.23 197.11C176.49 196.67 161.65 195.99 161.27 195.60C160.02 194.36 162.47 191.65 172.58 183.13C217.82 144.97 263.71 134.18 300.12 153.12C341.88 174.85 348.07 228.17 314.13 273.85C287.46 309.75 245.45 337.07 203.16 346.04C194.30 347.92 163.60 349.66 159.00 348.54Z";

        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(0.660066, 0.660066));
        transform.Children.Add(new TranslateTransform(6.2178, -6.9835));
        transform.Freeze();

        Geometry Half(string data)
        {
            var shape = Geometry.Parse(data);
            var edge = shape.GetWidenedPathGeometry(new Pen(Brushes.Black, 6.06) { LineJoin = PenLineJoin.Round });
            var inset = Geometry.Combine(shape, edge, GeometryCombineMode.Exclude, transform);
            inset.Freeze();
            return inset;
        }

        var group = new DrawingGroup();
        // Прозрачный квадрат фиксирует поле 256×256, как viewBox в SVG.
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
        group.Children.Add(new GeometryDrawing(EmberBrush, null, Half(top)));
        group.Children.Add(new GeometryDrawing(AmberBrush, null, Half(bottom)));
        group.Freeze();

        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
