using System;
using System.Windows;
using Microsoft.Win32;
using Shotly.Native;

namespace Shotly.Core;

/// <summary>
/// Параметр Windows 11 «Использовать клавишу Print Screen для захвата экрана».
/// Пока он включён, одиночную PrtScn забирает система и открывает «Ножницы», причём
/// RegisterHotKey для неё всё равно завершается успешно — ошибки регистрации нет, а нажатия до Shotly не доходят.
/// </summary>
public static class SnippingKey
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    private const string SettingsUri = "ms-settings:easeofaccess-keyboard";

    /// <summary>
    /// Включён ли перехват PrtScn «Ножницами». Если значения нет, действует умолчание системы:
    /// в Windows 11 (сборка 22000+) с обновлений 2023 года — включено, в Windows 10 — выключено.
    /// </summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
                if (key?.GetValue(ValueName) is int value)
                    return value != 0;
            }
            catch (Exception ex)
            {
                Log.Error("Не удалось прочитать параметр клавиши Print Screen", ex);
            }
            return Environment.OSVersion.Version.Build >= 22000;
        }
    }

    /// <summary>Выключает параметр для текущего пользователя (то же, что переключатель в «Параметрах»).</summary>
    public static void Disable()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);

        // Сообщаем оболочке, что параметры изменились, чтобы она перечитала их без перезахода в систему.
        NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE, IntPtr.Zero,
            KeyPath, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        Log.Info("Параметр «Использовать клавишу Print Screen для захвата экрана» выключен по просьбе пользователя");
    }

    /// <summary>Есть ли среди сочетаний одиночная PrtScn без модификаторов.</summary>
    public static bool IsUsedBy(HotkeySettings h)
    {
        foreach (var gesture in new[] { h.Region, h.Window, h.FullScreen, h.RepeatLast })
            if (IsBarePrintScreen(gesture))
                return true;
        return false;
    }

    public static bool IsBarePrintScreen(string? gesture) =>
        !string.IsNullOrWhiteSpace(gesture)
        && HotkeyManager.TryParse(gesture, out var modifiers, out var vk)
        && modifiers == 0
        && vk == HotkeyManager.VkSnapshot;

    /// <summary>Конфликт есть: Shotly назначена одиночная PrtScn, а Windows отдаёт её «Ножницам».</summary>
    public static bool Conflicts(HotkeySettings h) => IsUsedBy(h) && IsEnabled;

    /// <summary>
    /// Спрашивает разрешения и выключает параметр. Возвращает true, если клавиша теперь свободна для Shotly.
    /// Без согласия пользователя ничего в системе не меняет.
    /// </summary>
    public static bool OfferToDisable(Window? owner)
    {
        const string text =
            "Клавиша PrtScn сейчас открывает «Ножницы» Windows, поэтому Shotly её не получает.\n\n" +
            "Выключить в Windows параметр «Использовать клавишу Print Screen для захвата экрана»? " +
            "Меняется только он и только для вашей учётной записи. Вернуть можно в Параметры → " +
            "Специальные возможности → Клавиатура.";
        var answer = owner is not null
            ? MessageBox.Show(owner, text, "Shotly", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes)
            : MessageBox.Show(text, "Shotly", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
            return false;

        try
        {
            Disable();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось выключить параметр клавиши Print Screen", ex);
            const string failed = "Не получилось изменить параметр автоматически. Сейчас откроются параметры клавиатуры: " +
                                  "выключите там «Использовать клавишу Print Screen для захвата экрана».";
            if (owner is not null)
                MessageBox.Show(owner, failed, "Shotly", MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                MessageBox.Show(failed, "Shotly", MessageBoxButton.OK, MessageBoxImage.Warning);
            OpenSettings();
            return false;
        }
    }

    /// <summary>Открывает страницу «Параметры → Специальные возможности → Клавиатура».</summary>
    public static void OpenSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SettingsUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось открыть параметры клавиатуры", ex);
        }
    }
}
