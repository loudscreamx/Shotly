using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shotly.Core;

/// <summary>Буфер обмена и сохранение в файл.</summary>
public sealed class OutputService
{
    private readonly Func<AppSettings> _settings;

    public OutputService(Func<AppSettings> settings) => _settings = settings;

    /// <summary>
    /// Буфер обмена может быть на мгновение занят другой программой (менеджеры буфера, RDP),
    /// поэтому пробуем несколько раз, прежде чем сообщить об ошибке.
    /// </summary>
    public void CopyToClipboard(BitmapSource image)
    {
        // 24-битный кадр: у 32-битного DIB без альфы некоторые программы вставляют картинку прозрачной.
        var rgb = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        rgb.Freeze();

        const int attempts = 10;
        for (int i = 1; ; i++)
        {
            try
            {
                Clipboard.SetImage(rgb);
                return;
            }
            catch (Exception ex) when (ex is COMException or ExternalException && i < attempts)
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>Сохраняет снимок в папку из настроек. Возвращает полный путь к файлу.</summary>
    public string Save(BitmapSource image)
    {
        var s = _settings();
        Directory.CreateDirectory(s.SaveFolder);

        var ext = s.Format == ImageFormatKind.Jpg ? ".jpg" : ".png";
        var baseName = $"Shotly_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        var path = Path.Combine(s.SaveFolder, baseName + ext);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(s.SaveFolder, $"{baseName}_{n}{ext}");

        BitmapEncoder encoder;
        if (s.Format == ImageFormatKind.Jpg)
        {
            encoder = new JpegBitmapEncoder { QualityLevel = Math.Clamp(s.JpegQuality, 1, 100) };
            image = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        }
        else
        {
            encoder = new PngBitmapEncoder();
        }

        encoder.Frames.Add(BitmapFrame.Create(image));

        // FileMode.CreateNew: никогда не перезаписываем чужой файл, даже если он появился между проверкой и записью.
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
        return path;
    }
}
