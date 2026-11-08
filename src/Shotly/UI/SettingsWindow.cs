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
    private readonly RadioButton _png = new() { Content = "PNG", Margin = new Thickness(0, 0, 20, 0), GroupName = "fmt" };
    private readonly RadioButton _jpg = new() { Content = "JPG", GroupName = "fmt" };
    private readonly Slider _quality = new() { Minimum = 50, Maximum = 100, Width = 160, IsSnapToTickEnabled = true, TickFrequency = 5 };
    private readonly TextBlock _qualityLabel = new() { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium };
    private readonly ComboBox _quick = new() { MinWidth = 220 };
    private readonly HotkeyBox _hkRegion = new();
    private readonly HotkeyBox _hkWindow = new();
    private readonly HotkeyBox _hkFull = new();
    private readonly HotkeyBox _hkRepeat = new();
    private readonly CheckBox _copyOnSelect = new() { Content = "Сразу копировать выделенную область в буфер", Margin = new Thickness(0, 1, 0, 1) };
    private readonly CheckBox _autostart = new() { Content = "Запускать при входе в Windows", Margin = new Thickness(0, 1, 0, 1) };
    private readonly CheckBox _checkUpdates = new() { Content = "Проверять обновления при запуске", Margin = new Thickness(0, 1, 0, 1) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 0) };

    public SettingsWindow(ISettingsHost host, AppSettings current, bool autostart)
    {
        _host = host;
        _draft = current.Clone();

        Title = "Настройки Shotly";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        // Оформление по брендбуку: тёмный фон, текст Onest, заголовки Unbounded, акцент Ember.
        FontFamily = Brand.TextFont;
        FontSize = 13;
        Background = (Brush)FindResource("Brush.Night");
        Foreground = Brand.TextBrush;
        _status.Foreground = (Brush)FindResource("Brush.Error");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Brand.ApplyDarkTitleBar(this);

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
        _copyOnSelect.IsChecked = _draft.CopyOnSelect;
        _autostart.IsChecked = autostart;
        _checkUpdates.IsChecked = _draft.CheckUpdates;

        Content = BuildLayout();
    }

    private UIElement BuildLayout()
    {
        var root = new StackPanel();
        root.Children.Add(Header());

        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };
        root.Children.Add(body);

        var browse = new Button { Content = "Обзор…", Margin = new Thickness(8, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var folderRow = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);

        var format = new StackPanel { Orientation = Orientation.Horizontal };
        format.Children.Add(_png);
        format.Children.Add(_jpg);

        var quality = new StackPanel { Orientation = Orientation.Horizontal };
        quality.Children.Add(_quality);
        quality.Children.Add(_qualityLabel);

        body.Children.Add(Card("Сохранение",
            Row("Папка", folderRow),
            Row("Формат", format),
            Row("Качество JPG", quality),
            Row("Окно и экран", _quick, "Что делать со снимком окна, экрана и повтором области"),
            Row(string.Empty, _copyOnSelect)));

        body.Children.Add(Card("Горячие клавиши",
            Row("Область", _hkRegion),
            Row("Активное окно", _hkWindow),
            Row("Весь экран", _hkFull),
            Row("Повтор области", _hkRepeat),
            Row(string.Empty, PrintScreenHint())));

        var check = new Button { Content = "Проверить сейчас" };
        check.Click += (_, _) => _host.CheckForUpdatesInteractive();
        var version = new TextBlock
        {
            Text = $"Версия {Installation.Version}",
            Foreground = Brand.MutedBrush,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var updates = new StackPanel { Orientation = Orientation.Horizontal };
        updates.Children.Add(check);
        updates.Children.Add(version);

        body.Children.Add(Card("Система",
            Row(string.Empty, _autostart),
            Row(string.Empty, _checkUpdates),
            Row("Обновления", updates)));

        body.Children.Add(_status);

        var save = new Button { Content = "Сохранить", IsDefault = true, MinWidth = 110, Style = (Style)FindResource("Style.Primary") };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Отмена", IsCancel = true, MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);

        // «Удалить» слева, подальше от «Сохранить», чтобы не нажать случайно.
        var uninstall = new Button { Content = "Удалить Shotly…" };
        uninstall.Click += (_, _) => _host.Uninstall();

        var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0), LastChildFill = false };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(uninstall);
        body.Children.Add(footer);

        // На маленьком экране с крупным масштабом окно не должно уходить за край.
        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = Math.Max(400, SystemParameters.WorkArea.Height - 60),
        };
    }

    /// <summary>Шапка окна: знак Orbit, надпись Shotly (Unbounded Bold, трекинг −2%, как в логотипе) и подпись.</summary>
    private static UIElement Header()
    {
        var mark = new Image { Source = Brand.Mark, Width = 44, Height = 44, Margin = new Thickness(0, 0, 10, 0) };
        RenderOptions.SetBitmapScalingMode(mark, BitmapScalingMode.HighQuality);

        var name = new TextBlock
        {
            Text = "Shotly",
            FontFamily = Brand.DisplayFont,
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            Foreground = Brand.TextBrush,
            Margin = new Thickness(0, 0, 0, 1),
        };
        name.SetValue(TextBlock.LineHeightProperty, 26.0);
        var caption = new TextBlock { Text = "Настройки", Foreground = Brand.MutedBrush, FontSize = 13 };

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(name);
        titles.Children.Add(caption);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 14, 20, 14) };
        row.Children.Add(mark);
        row.Children.Add(titles);

        // Тонкая полоса из двух фирменных цветов под шапкой: верх знака Ember, низ Amber.
        var stripe = new Grid { Height = 2 };
        stripe.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        stripe.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ember = new Border { Background = Brand.EmberBrush };
        var amber = new Border { Background = Brand.AmberBrush };
        Grid.SetColumn(amber, 1);
        stripe.Children.Add(ember);
        stripe.Children.Add(amber);

        var header = new StackPanel { Background = Brand.CoalBrush };
        header.Children.Add(row);
        header.Children.Add(stripe);
        return header;
    }

    /// <summary>
    /// Одиночная PrtScn в Windows 11 по умолчанию открывает «Ножницы», и Shotly её не получит.
    /// Поэтому умолчания Shotly — с модификаторами, а здесь подсказка, где это выключить.
    /// </summary>
    private static TextBlock PrintScreenHint()
    {
        var hint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brand.MutedBrush,
            FontSize = 12,
            LineHeight = 17,
            Margin = new Thickness(0, 2, 0, 0),
            Text = "Одиночную PrtScn в Windows 11 занимают «Ножницы». Чтобы отдать её Shotly, выключите в Windows " +
                   "«Использовать клавишу Print Screen для захвата экрана». ",
        };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Открыть параметры"));
        link.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:easeofaccess-keyboard")
                    { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("Не удалось открыть параметры клавиатуры", ex); }
        };
        hint.Inlines.Add(link);
        return hint;
    }

    /// <summary>Группа настроек: карточка на поверхности чуть светлее фона, заголовок Unbounded.</summary>
    private Border Card(string title, params UIElement[] rows)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = Brand.DisplayFont,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = Brand.TextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (var row in rows)
            panel.Children.Add(row);

        return new Border
        {
            Background = (Brush)FindResource("Brush.Surface"),
            BorderBrush = Brand.LineBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel,
        };
    }

    private static UIElement Row(string label, UIElement control, string? hint = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(128) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, ToolTip = hint, Foreground = Brand.MutedBrush };
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
        _draft.CopyOnSelect = _copyOnSelect.IsChecked == true;

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
