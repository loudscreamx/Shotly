using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Shotly.Core;

namespace Shotly.UI;

/// <summary>Действия, которые окно настроек просит выполнить у приложения.</summary>
public interface ISettingsHost
{
    /// <summary>Применить настройки. Возвращает список ошибок (например, занятые сочетания), пустой — всё хорошо.</summary>
    System.Collections.Generic.IReadOnlyList<string> Apply(AppSettings settings, bool autostart);
    void CheckForUpdatesInteractive();
    void Uninstall();
}

/// <summary>
/// Окно настроек. Создаётся только когда его открыли и закрывается полностью,
/// чтобы в трее не висело ни одного окна.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly ISettingsHost _host;
    private readonly AppSettings _draft;

    private readonly TextBox _folder = new() { MinWidth = 260 };
    private readonly RadioButton _png = new() { Content = "PNG", Margin = new Thickness(0, 0, 16, 0), GroupName = "fmt" };
    private readonly RadioButton _jpg = new() { Content = "JPG", GroupName = "fmt" };
    private readonly Slider _quality = new() { Minimum = 50, Maximum = 100, Width = 160, IsSnapToTickEnabled = true, TickFrequency = 5 };
    private readonly TextBlock _qualityLabel = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _quick = new() { MinWidth = 220 };
    private readonly HotkeyBox _hkRegion = new();
    private readonly HotkeyBox _hkWindow = new();
    private readonly HotkeyBox _hkFull = new();
    private readonly HotkeyBox _hkRepeat = new();
    private readonly CheckBox _autostart = new() { Content = "Запускать при входе в Windows" };
    private readonly CheckBox _checkUpdates = new() { Content = "Проверять обновления при запуске" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 0) };

    public SettingsWindow(ISettingsHost host, AppSettings current, bool autostart)
    {
        _host = host;
        _draft = current.Clone();

        Title = "Настройки Shotly";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        ShowInTaskbar = true;

        _folder.Text = _draft.SaveFolder;
        _png.IsChecked = _draft.Format == ImageFormatKind.Png;
        _jpg.IsChecked = _draft.Format == ImageFormatKind.Jpg;
        _quality.Value = Math.Clamp(_draft.JpegQuality, 50, 100);
        _quality.ValueChanged += (_, _) => UpdateQualityLabel();
        _png.Checked += (_, _) => UpdateQualityLabel();
        _jpg.Checked += (_, _) => UpdateQualityLabel();
        UpdateQualityLabel();

        _quick.Items.Add(new ComboBoxItem { Content = "Копировать и сохранить", Tag = QuickAction.CopyAndSave });
        _quick.Items.Add(new ComboBoxItem { Content = "Только копировать", Tag = QuickAction.Copy });
        _quick.Items.Add(new ComboBoxItem { Content = "Только сохранить", Tag = QuickAction.Save });
        foreach (ComboBoxItem item in _quick.Items)
            if ((QuickAction)item.Tag == _draft.QuickAction)
                _quick.SelectedItem = item;

        _hkRegion.Gesture = _draft.Hotkeys.Region;
        _hkWindow.Gesture = _draft.Hotkeys.Window;
        _hkFull.Gesture = _draft.Hotkeys.FullScreen;
        _hkRepeat.Gesture = _draft.Hotkeys.RepeatLast;
        _autostart.IsChecked = autostart;
        _checkUpdates.IsChecked = _draft.CheckUpdates;

        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(Section("Сохранение"));
        var browse = new Button { Content = "Обзор…", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var folderRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);
        root.Children.Add(Row("Папка", folderRow));

        var format = new StackPanel { Orientation = Orientation.Horizontal };
        format.Children.Add(_png);
        format.Children.Add(_jpg);
        root.Children.Add(Row("Формат", format));

        var quality = new StackPanel { Orientation = Orientation.Horizontal };
        quality.Children.Add(_quality);
        quality.Children.Add(_qualityLabel);
        root.Children.Add(Row("Качество JPG", quality));
        root.Children.Add(Row("Окно и экран", _quick, "Что делать со снимком окна, экрана и повтором области"));

        root.Children.Add(Section("Горячие клавиши"));
        root.Children.Add(Row("Область", _hkRegion));
        root.Children.Add(Row("Активное окно", _hkWindow));
        root.Children.Add(Row("Весь экран", _hkFull));
        root.Children.Add(Row("Повтор области", _hkRepeat));

        root.Children.Add(Section("Система"));
        root.Children.Add(Row(string.Empty, _autostart));
        root.Children.Add(Row(string.Empty, _checkUpdates));

        var check = new Button { Content = "Проверить сейчас", Padding = new Thickness(12, 2, 12, 2) };
        check.Click += (_, _) => _host.CheckForUpdatesInteractive();
        var version = new TextBlock
        {
            Text = $"Версия {Installation.Version}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var updates = new StackPanel { Orientation = Orientation.Horizontal };
        updates.Children.Add(check);
        updates.Children.Add(version);
        root.Children.Add(Row("Обновления", updates));

        var uninstall = new Button { Content = "Удалить Shotly…", Padding = new Thickness(12, 2, 12, 2), HorizontalAlignment = HorizontalAlignment.Left };
        uninstall.Click += (_, _) => _host.Uninstall();
        root.Children.Add(Row(string.Empty, uninstall));

        root.Children.Add(_status);

        var save = new Button { Content = "Сохранить", IsDefault = true, MinWidth = 100, Padding = new Thickness(12, 4, 12, 4) };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 100, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        return root;
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 6),
    };

    private static UIElement Row(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint };
        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private void UpdateQualityLabel()
    {
        _qualityLabel.Text = $"{(int)_quality.Value}%";
        _quality.IsEnabled = _jpg.IsChecked == true;
    }

    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Папка для снимков" };
        if (Directory.Exists(_folder.Text))
            dialog.InitialDirectory = _folder.Text;
        if (dialog.ShowDialog(this) == true)
            _folder.Text = dialog.FolderName;
    }

    private void Save()
    {
        var folder = _folder.Text.Trim();
        if (folder.Length == 0 || !Path.IsPathFullyQualified(folder))
        {
            _status.Text = "Укажите полный путь к папке, например C:\\Users\\…\\Pictures\\Shotly.";
            return;
        }

        var gestures = new[] { _hkRegion.Gesture, _hkWindow.Gesture, _hkFull.Gesture, _hkRepeat.Gesture };
        for (int i = 0; i < gestures.Length; i++)
            for (int j = i + 1; j < gestures.Length; j++)
                if (gestures[i].Length > 0 && string.Equals(gestures[i], gestures[j], StringComparison.OrdinalIgnoreCase))
                {
                    _status.Text = $"Сочетание {gestures[i]} назначено двум действиям.";
                    return;
                }

        _draft.SaveFolder = folder;
        _draft.Format = _jpg.IsChecked == true ? ImageFormatKind.Jpg : ImageFormatKind.Png;
        _draft.JpegQuality = (int)_quality.Value;
        _draft.QuickAction = _quick.SelectedItem is ComboBoxItem { Tag: QuickAction q } ? q : QuickAction.CopyAndSave;
        _draft.Hotkeys.Region = _hkRegion.Gesture;
        _draft.Hotkeys.Window = _hkWindow.Gesture;
        _draft.Hotkeys.FullScreen = _hkFull.Gesture;
        _draft.Hotkeys.RepeatLast = _hkRepeat.Gesture;
        _draft.CheckUpdates = _checkUpdates.IsChecked == true;

        var errors = _host.Apply(_draft, _autostart.IsChecked == true);
        if (errors.Count > 0)
        {
            // Настройки сохранены, но о проблемах нужно сказать, не закрывая окно.
            _status.Text = string.Join("\n", errors);
            return;
        }

        Close();
    }
}
