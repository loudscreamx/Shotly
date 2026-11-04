using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shotly.Core;

public enum ImageFormatKind { Png, Jpg }

/// <summary>Что делать со снимком, если мини-панель не показывается (окно, экран, повтор области).</summary>
public enum QuickAction { Copy, Save, CopyAndSave }

public sealed class HotkeySettings
{
    public string Region { get; set; } = "Ctrl+Shift+S";
    public string Window { get; set; } = "Ctrl+Shift+W";
    public string FullScreen { get; set; } = "Ctrl+Shift+F";
    public string RepeatLast { get; set; } = "Ctrl+Shift+R";
}

public sealed class AppSettings
{
    public string SaveFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Shotly");

    public ImageFormatKind Format { get; set; } = ImageFormatKind.Png;
    public int JpegQuality { get; set; } = 90;
    public QuickAction QuickAction { get; set; } = QuickAction.CopyAndSave;
    public HotkeySettings Hotkeys { get; set; } = new();

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
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(FilePath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings());
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
