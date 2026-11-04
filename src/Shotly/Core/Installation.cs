using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Shotly.Core;

/// <summary>
/// «Установка» без установщика и без прав администратора: запись в
/// HKCU\...\Uninstall, чтобы Shotly был виден в «Установленных приложениях» и удалялся оттуда.
/// </summary>
public static class Installation
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Shotly";

    public static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Не удалось определить путь к Shotly.exe.");

    public static string Version
    {
        get
        {
            var v = typeof(Installation).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
        }
    }

    /// <summary>Создаёт или обновляет запись (путь к exe мог поменяться, версия — вырасти).</summary>
    public static void Register()
    {
        var exe = ExePath;
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
        key.SetValue("DisplayName", "Shotly");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "Shotly");
        key.SetValue("DisplayIcon", $"\"{exe}\",0");
        key.SetValue("InstallLocation", Path.GetDirectoryName(exe) ?? string.Empty);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (key.GetValue("InstallDate") is null)
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        try
        {
            key.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
        }
        catch (IOException)
        {
            // Размер необязателен.
        }
    }

    /// <summary>
    /// Удаляет всё, что Shotly оставляет в системе: автозапуск, запись в «Установленных приложениях»,
    /// настройки и лог. Снимки пользователя не трогает. Сам exe удаляется скриптом после выхода процесса.
    /// </summary>
    /// <returns>false, если exe не получится удалить автоматически (его нужно удалить вручную).</returns>
    public static bool Uninstall(SettingsStore store)
    {
        var exe = ExePath;
        var exeDir = Path.GetDirectoryName(exe) ?? string.Empty;
        var logDir = Path.GetDirectoryName(Log.FilePath)!;

        TryRun(() => Autostart.Set(false));
        TryRun(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false));
        TryRun(() => DeleteDirectory(store.Directory));

        // Папку, в которой лежит сам exe, удаляет скрипт после выхода процесса.
        bool logDirHoldsExe = IsSameOrInside(exeDir, logDir) || IsSameOrInside(exeDir, store.Directory);

        bool scheduled;
        try
        {
            ScheduleDelete(exe, logDirHoldsExe ? logDir : null);
            scheduled = true;
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось запланировать удаление exe", ex);
            scheduled = false;
        }

        // Лог удаляется последним, и после этого ничего не пишется, иначе папка появится снова.
        if (!logDirHoldsExe)
        {
            try { DeleteDirectory(logDir); }
            catch (Exception) { /* лог больше некуда писать */ }
        }
        return scheduled;
    }

    private static bool IsSameOrInside(string path, string dir)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var d = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Скрытый скрипт ждёт выхода процесса (пока он жив, exe занят) и удаляет файл.
    /// </summary>
    private static void ScheduleDelete(string exe, string? extraDir)
    {
        var extra = extraDir is null ? string.Empty
            : $"\nRemove-Item -LiteralPath {Quote(extraDir)} -Recurse -Force -ErrorAction SilentlyContinue";
        RunAfterExit($@"
for ($i = 0; $i -lt 30; $i++) {{
  Remove-Item -LiteralPath {Quote(exe)} -Force -ErrorAction SilentlyContinue
  if (-not (Test-Path -LiteralPath {Quote(exe)})) {{ break }}
  Start-Sleep -Seconds 1
}}{extra}");
    }

    /// <summary>
    /// Запускает PowerShell-скрипт, который сначала ждёт завершения текущего процесса (без ограничения:
    /// процесс может ждать, пока пользователь закроет последнее сообщение).
    /// PowerShell и -EncodedCommand (UTF-16) выбраны вместо .cmd: пути с кириллицей
    /// (C:\Users\Мария\...) в cmd-файле ломаются из-за кодовой страницы консоли.
    /// </summary>
    internal static void RunAfterExit(string body)
    {
        int pid = Environment.ProcessId;
        var script = $@"
$p = Get-Process -Id {pid} -ErrorAction SilentlyContinue
if ($p) {{ $p.WaitForExit() }}
{body}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    /// <summary>Строка в одинарных кавычках PowerShell: внутри ничего не раскрывается, ' удваивается.</summary>
    internal static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    private static void DeleteDirectory(string dir)
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static void TryRun(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error("Удаление: шаг не выполнен", ex); }
    }
}
