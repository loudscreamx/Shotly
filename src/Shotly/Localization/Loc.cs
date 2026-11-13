using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Shotly.Core;

namespace Shotly.Localization;

/// <summary>
/// Язык интерфейса. <paramref name="Code"/> совпадает с именем файла в Localization/Languages и с именем культуры .NET.
/// <paramref name="WpfFont"/> и <paramref name="GdiFont"/> нужны языкам, для которых в фирменных шрифтах нет букв
/// (китайский, японский, корейский): иначе Windows подставит первый попавшийся шрифт, и иероглифы будут в чужом начертании.
/// </summary>
public sealed record LanguageInfo(string Code, string WpfFont = "", string GdiFont = "")
{
    /// <summary>Название языка на нём самом, как в списке в настройках.</summary>
    public string NativeName => Loc.NameOf(this);
}

/// <summary>
/// Загрузка переводов и выбор языка. Тексты — встроенные в exe JSON-файлы, по одному на язык.
/// Если в переводе нет ключа, берётся английский текст, а если нет и его — имя ключа (так пропуск заметен сразу).
/// </summary>
public static class Loc
{
    /// <summary>Порядок — как в списке в настройках. Новый язык: добавить сюда и положить Languages/&lt;код&gt;.json.</summary>
    public static readonly IReadOnlyList<LanguageInfo> Languages = new LanguageInfo[]
    {
        new("en"),
        new("ru"),
        new("uk"),
        new("de"),
        new("fr"),
        new("es"),
        new("it"),
        new("pt-BR"),
        new("pl"),
        new("tr"),
        new("zh-Hans", "Microsoft YaHei UI", "Microsoft YaHei UI"),
        new("ja", "Yu Gothic UI, Meiryo UI", "Yu Gothic UI"),
        new("ko", "Malgun Gothic", "Malgun Gothic"),
    };

    public static readonly LanguageInfo English = Languages[0];

    /// <summary>Язык Windows на момент запуска (до того, как Shotly поменял культуру потока).</summary>
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Языки, для которых своего перевода нет, но русский обычно понятнее английского:
    /// в этих странах Windows часто стоит на русском, а если нет — интерфейс на русском привычнее.
    /// </summary>
    private static readonly HashSet<string> PreferRussian = new(StringComparer.OrdinalIgnoreCase) { "be", "kk", "ky", "tg" };

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, string> _strings = new();
    private static Dictionary<string, string> _fallback = new();

    static Loc()
    {
        _fallback = Load(English.Code);
        Current = SystemLanguage;
        _strings = Load(Current.Code);
        ApplyCulture();
    }

    public static LanguageInfo Current { get; private set; }

    /// <summary>Какой язык подходит к языку Windows; если такого перевода нет — английский.</summary>
    public static LanguageInfo SystemLanguage => FromCulture(SystemCulture);

    /// <summary>Язык из настроек: код языка или пустая строка («как в Windows»).</summary>
    public static LanguageInfo Resolve(string? setting)
    {
        if (!string.IsNullOrWhiteSpace(setting))
            foreach (var lang in Languages)
                if (string.Equals(lang.Code, setting.Trim(), StringComparison.OrdinalIgnoreCase))
                    return lang;
        return SystemLanguage;
    }

    /// <summary>Переключает язык. Возвращает true, если он поменялся (тогда открытый интерфейс нужно перерисовать).</summary>
    public static bool Apply(string? setting)
    {
        var next = Resolve(setting);
        if (next == Current)
            return false;
        Current = next;
        _strings = Load(next.Code);
        ApplyCulture();
        return true;
    }

    internal static LanguageInfo FromCulture(CultureInfo culture)
    {
        var name = culture.Name;
        if (name.Length == 0)
            return English;

        // Китайский: перевод только на упрощённое письмо. Тайвань, Гонконг и Макао пишут традиционным — им английский.
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            bool traditional = name.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase)
                               || name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase);
            return traditional ? English : Find("zh-Hans") ?? English;
        }

        // Точное совпадение (pt-BR), затем по языку без региона (pt-PT → pt-BR, de-AT → de).
        if (Find(name) is { } exact)
            return exact;
        var two = culture.TwoLetterISOLanguageName;
        foreach (var lang in Languages)
            if (string.Equals(lang.Code.Split('-')[0], two, StringComparison.OrdinalIgnoreCase))
                return lang;
        if (PreferRussian.Contains(two))
            return Find("ru") ?? English;
        return English;
    }

    private static LanguageInfo? Find(string code)
    {
        foreach (var lang in Languages)
            if (string.Equals(lang.Code, code, StringComparison.OrdinalIgnoreCase))
                return lang;
        return null;
    }

    /// <summary>Строка по имени свойства в <see cref="L"/>.</summary>
    public static string Get([CallerMemberName] string key = "")
    {
        if (_strings.TryGetValue(key, out var value) || _fallback.TryGetValue(key, out value))
            return value;
        return key;
    }

    /// <summary>Строка с подстановкой {0}, {1}… Битый перевод не должен ронять приложение: тогда берём английский.</summary>
    public static string Format(string key, params object[] args)
    {
        var template = Get(key);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            Log.Error($"Перевод {Current.Code}/{key}: неверные подстановки");
            return _fallback.TryGetValue(key, out var en) ? string.Format(CultureInfo.InvariantCulture, en, args) : template;
        }
    }

    internal static string NameOf(LanguageInfo lang) =>
        Load(lang.Code).TryGetValue("LanguageName", out var name) ? name : lang.Code;

    /// <summary>Культура интерфейса влияет на переносы и подбор глифов в WPF. Числа и даты остаются как в Windows.</summary>
    private static void ApplyCulture()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(Current.Code);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
        }
    }

    private static Dictionary<string, string> Load(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var cached))
                return cached;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                using var stream = typeof(Loc).Assembly.GetManifestResourceStream($"Shotly.Languages.{code}.json");
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd());
                    if (parsed is not null)
                        result = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                }
                else
                {
                    Log.Error($"Нет файла перевода {code}.json");
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Log.Error($"Не удалось прочитать перевод {code}.json", ex);
            }

            Cache[code] = result;
            return result;
        }
    }
}
