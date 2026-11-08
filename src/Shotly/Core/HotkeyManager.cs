using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Shotly.Native;

namespace Shotly.Core;

/// <summary>
/// Глобальные горячие клавиши через RegisterHotKey на невидимом message-only окне.
/// Без низкоуровневых хуков клавиатуры: они тратят CPU на каждое нажатие и пугают антивирусы.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        var p = new HwndSourceParameters("ShotlyHotkeySink")
        {
            ParentWindow = HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Регистрирует сочетание вида "Ctrl+Shift+S". Возвращает текст ошибки или null.</summary>
    public string? Register(string gesture, Action action)
    {
        if (!TryParse(gesture, out var modifiers, out var vk))
            return $"«{gesture}»: не удалось разобрать сочетание";

        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers | NativeMethods.MOD_NOREPEAT, vk))
            return BusyMessage(gesture, modifiers, vk);

        _actions[id] = action;
        return null;
    }

    private static readonly uint VK_SNAPSHOT = (uint)KeyInterop.VirtualKeyFromKey(Key.PrintScreen);

    /// <summary>Объяснение, кто обычно занимает сочетание. У PrtScn в Windows 11 свои частые «владельцы».</summary>
    private static string BusyMessage(string gesture, uint modifiers, uint vk)
    {
        var name = $"«{Display(gesture)}»";
        if (vk != VK_SNAPSHOT)
            return $"{name}: сочетание уже занято другой программой";
        if ((modifiers & NativeMethods.MOD_WIN) != 0)
            return $"{name}: это системное сочетание Windows, его нельзя переназначить";
        if (modifiers == 0)
            return $"{name}: клавишу занимают «Ножницы». Выключите в Windows: Параметры → Специальные возможности → " +
                   "Клавиатура → «Использовать клавишу Print Screen для захвата экрана»";
        return $"{name}: сочетание уже занято другой программой (часто это OneDrive, Яндекс Диск, Lightshot или ShareX)";
    }

    /// <summary>Подпись для меню и сообщений: «Ctrl+PrintScreen» → «Ctrl+PrtScn».</summary>
    public static string Display(string gesture)
    {
        if (string.IsNullOrEmpty(gesture))
            return gesture;
        var parts = gesture.Split('+', StringSplitOptions.TrimEntries);
        if (TryParseKey(parts[^1], out var key) && key == Key.PrintScreen)
            parts[^1] = "PrtScn";
        return string.Join("+", parts);
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(gesture))
            return false;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            bool last = i == parts.Length - 1;
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= NativeMethods.MOD_CONTROL; continue;
                case "shift": modifiers |= NativeMethods.MOD_SHIFT; continue;
                case "alt": modifiers |= NativeMethods.MOD_ALT; continue;
                case "win": modifiers |= NativeMethods.MOD_WIN; continue;
            }

            if (!last)
                return false;

            if (!TryParseKey(part, out var key))
                return false;

            vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        }

        return vk != 0;
    }

    private static bool TryParseKey(string part, out Key key)
    {
        switch (part.ToLowerInvariant())
        {
            // Как клавиша подписана на разных клавиатурах. Key.Snapshot и Key.PrintScreen — одно значение.
            case "prtscn" or "prtsc" or "prtscr" or "printscreen" or "print" or "snapshot":
                key = Key.PrintScreen;
                return true;
        }

        if (part.Length == 1 && char.IsDigit(part[0]))
        {
            key = Key.D0 + (part[0] - '0'); // "1" → D1, а не числовое значение enum (Key.Cancel)
            return true;
        }

        return !int.TryParse(part, out _) && Enum.TryParse(part, ignoreCase: true, out key)
               && Enum.IsDefined(key) && key != Key.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            // Выходим из оконной процедуры сразу, а действие выполняем следующей итерацией цикла сообщений.
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, action);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
