using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shotly.Core;

public enum ImageFormatKind { Png, Jpg }

/// <summary>Что делать со снимком, если мини-панель не показывается (окно, экран, повтор области).</summary>
public enum QuickAction { Copy, Save, CopyAndSave }

/// <summary>
/// Сочетания по умолчанию собраны вокруг PrtScn. Одиночную PrtScn и Win+PrtScn оставляем Windows:
/// первую в Windows 11 занимают «Ножницы», вторая — системный снимок экрана в «Изображения».
/// </summary>
public sealed class HotkeySettings
{
    /// <summary>Номер раскладки по умолчанию. Растёт, когда меняются умолчания, чтобы перенести старые настройки.</summary>
    public const int CurrentLayout = 2;

    public string Region { get; set; } = "Ctrl+PrintScreen";
    public string Window { get; set; } = "Alt+PrintScreen";
    public string FullScreen { get; set; } = "Shift+PrintScreen";
    public string RepeatLast { get; set; } = "Ctrl+Shift+PrintScreen";

    /// <summary>
    /// Раскладка, под которую записаны сочетания. В старых settings.json поля нет (0):
    /// тогда нетронутые старые умолчания (Ctrl+Shift+S/W/F/R) меняются на новые, а свои сочетания остаются.
    /// </summary>
    public int Layout { get; set; }

    /// <summary>Сочетания до раскладки 2.</summary>
    internal static readonly HotkeySettings Legacy = new()
    {
        Region = "Ctrl+Shift+S",
        Window = "Ctrl+Shift+W",
        FullScreen = "Ctrl+Shift+F",
        RepeatLast = "Ctrl+Shift+R",
    };
}

public sealed class AppSettings
{
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shotly");

    public ImageFormatKind Format { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 90;
    public QuickAction QuickAction { get; set; } = QuickAction.CopyAndSave;
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Проверять обновления при запуске (один запрос к GitHub, без фоновых таймеров).</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>
    /// Репозиторий GitHub (owner/name) с релизами Shotly.exe, если его задали вручную в settings.json.
    /// Пусто — используется источник, вшитый в сборку (он может смениться с новой версией).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UpdateRepository { get; set; }

    [JsonIgnore]
    public string EffectiveUpdateRepository =>
        string.IsNullOrWhiteSpace(UpdateRepository) ? DefaultUpdateRepository : UpdateRepository;

    /// <summary>
    /// Источник обновлений по умолчанию. CI может подставить открытый репозиторий для exe
    /// через -p:UpdateRepository=owner/name (попадает в AssemblyMetadata).
    /// </summary>
    public static string DefaultUpdateRepository { get; } = ReadDefaultRepository();

    private static string ReadDefaultRepository()
    {
        foreach (var a in typeof(AppSettings).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false))
            if (a is System.Reflection.AssemblyMetadataAttribute { Key: "UpdateRepository", Value: { Length: > 0 } v })
                return v;
        return "loudscreamx/Shotly";
    }

    /// <summary>Копия для окна настроек: правки применяются, только если нажать «Сохранить».</summary>
    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.Hotkeys = new HotkeySettings
        {
            Region = Hotkeys.Region,
            Window = Hotkeys.Window,
            FullScreen = Hotkeys.FullScreen,
            RepeatLast = Hotkeys.RepeatLast,
            Layout = Hotkeys.Layout,
        };
        return c;
    }

    /// <summary>Последняя выделенная область в физических пикселях и конфигурация мониторов, в которой она была сделана.</summary>
    public PxRect? LastRegion { get; set; }
    public string? LastRegionLayout { get; set; }
}

/// <summary>Настройки в %AppData%\Shotly\settings.json.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shotly");
        FilePath = Path.Combine(Directory, "settings.json");
    }

    public string Directory { get; }
    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var defaults = new AppSettings();
                defaults.Hotkeys.Layout = HotkeySettings.CurrentLayout;
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(FilePath);
            var settings = Normalize(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings());
            if (MigrateHotkeys(settings.Hotkeys))
                Save(settings);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Битый файл не должен ронять приложение: откладываем его в сторону и стартуем с умолчаниями.
            Log.Error("Не удалось прочитать настройки, используются значения по умолчанию", ex);
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch { /* не критично */ }
            return new AppSettings();
        }
    }

    /// <summary>Подставляет умолчания вместо null и мусора, которые мог оставить ручной правкой пользователь.</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        var d = new AppSettings();
        s.Hotkeys ??= new HotkeySettings();
        s.Hotkeys.Region ??= d.Hotkeys.Region;
        s.Hotkeys.Window ??= d.Hotkeys.Window;
        s.Hotkeys.FullScreen ??= d.Hotkeys.FullScreen;
        s.Hotkeys.RepeatLast ??= d.Hotkeys.RepeatLast;
        if (string.IsNullOrWhiteSpace(s.SaveFolder))
            s.SaveFolder = d.SaveFolder;
        s.JpegQuality = Math.Clamp(s.JpegQuality, 1, 100);
        return s;
    }

    /// <summary>Переносит сочетания на текущую раскладку по умолчанию. Возвращает true, если что-то поменялось.</summary>
    private static bool MigrateHotkeys(HotkeySettings h)
    {
        if (h.Layout >= HotkeySettings.CurrentLayout)
            return false;

        var old = HotkeySettings.Legacy;
        var d = new HotkeySettings();
        static bool same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // Меняем только нетронутые старые умолчания. Если новое умолчание уже занято
        // своим сочетанием пользователя на другом действии, оставляем старое, чтобы не было дублей.
        var current = new[] { h.Region, h.Window, h.FullScreen, h.RepeatLast };
        var legacy = new[] { old.Region, old.Window, old.FullScreen, old.RepeatLast };
        var next = new[] { d.Region, d.Window, d.FullScreen, d.RepeatLast };
        var result = (string[])current.Clone();
        for (int i = 0; i < current.Length; i++)
        {
            if (!same(current[i], legacy[i]))
                continue;
            bool taken = false;
            for (int j = 0; j < current.Length; j++)
                if (j != i && !same(current[j], legacy[j]) && same(current[j], next[i]))
                    taken = true;
            if (!taken)
                result[i] = next[i];
        }
        (h.Region, h.Window, h.FullScreen, h.RepeatLast) = (result[0], result[1], result[2], result[3]);

        h.Layout = HotkeySettings.CurrentLayout;
        Log.Info($"Горячие клавиши перенесены на раскладку {HotkeySettings.CurrentLayout}: " +
                 $"{h.Region}, {h.Window}, {h.FullScreen}, {h.RepeatLast}");
        return true;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, FilePath, overwrite: true); // запись через временный файл: не оставит полуфайл при сбое
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Не удалось сохранить настройки", ex);
        }
    }
}
