using System.Windows.Controls;
using System.Windows.Input;
using Shotly.Core;

namespace Shotly.UI;

/// <summary>
/// Поле, которое записывает нажатое сочетание клавиш («Ctrl+Shift+S») вместо ввода текста.
/// Backspace или Delete без модификаторов очищают поле.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Arrow;
        ToolTip = "Нажмите новое сочетание клавиш. Backspace — очистить.";
    }

    public string Gesture
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return; // ждём основную клавишу

        if (mods == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            Text = string.Empty;
            return;
        }

        // Tab/Shift+Tab переходят между полями, Enter и Esc — «Сохранить» и «Отмена».
        if ((key == Key.Tab && mods is ModifierKeys.None or ModifierKeys.Shift)
            || (key is Key.Enter or Key.Escape && mods == ModifierKeys.None))
        {
            e.Handled = false;
            return;
        }

        var parts = new System.Collections.Generic.List<string>();
        if ((mods & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((mods & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((mods & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((mods & ModifierKeys.Windows) != 0) parts.Add("Win");

        bool standalone = key is >= Key.F1 and <= Key.F24 or Key.PrintScreen;
        if (parts.Count == 0 && !standalone)
            return; // одиночная буква перехватила бы обычный набор текста во всех программах

        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        var gesture = string.Join("+", parts);
        if (HotkeyManager.TryParse(gesture, out _, out _))
            Text = gesture;
    }
}
