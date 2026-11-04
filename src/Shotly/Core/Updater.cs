using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shotly.Core;

public sealed record UpdateInfo(Version Version, string DownloadUrl, string PageUrl);

public sealed class UpdateException : Exception
{
    public UpdateException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Обновление через GitHub Releases: последний релиз с тегом вида v0.2.15 и файлом Shotly.exe.
/// Ничего не работает в фоне постоянно: одна проверка при запуске (если включена) и по кнопке.
/// </summary>
public static class Updater
{
    private const string AssetName = "Shotly.exe";

    // HttpClient создаётся только при первой проверке, чтобы не замедлять запуск.
    private static readonly Lazy<HttpClient> Http = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Shotly-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    });

    public static Version Current
    {
        get
        {
            var v = typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return Normalize(v);
        }
    }

    /// <summary>Новая версия или null, если установлена последняя.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string repository, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repository) || repository.Split('/').Length != 2)
            throw new UpdateException("Источник обновлений не настроен.");

        HttpResponseMessage response;
        try
        {
            response = await Http.Value.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Нет связи с GitHub.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException("GitHub не ответил вовремя.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new UpdateException("Релизы не найдены: репозиторий закрыт или в нём ещё нет релизов.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new UpdateException("GitHub временно ограничил запросы, попробуйте позже.");
            if (!response.IsSuccessStatusCode)
                throw new UpdateException($"GitHub ответил ошибкой {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var remote))
                return null; // релиз без номера версии — не обновляемся на него

            string? url = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var n)
                        && string.Equals(n.GetString(), AssetName, StringComparison.OrdinalIgnoreCase)
                        && asset.TryGetProperty("browser_download_url", out var u))
                        url = u.GetString();
                }
            }

            if (url is null || Normalize(remote) <= Current)
                return null;

            var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty;
            return new UpdateInfo(Normalize(remote), url, page);
        }
    }

    /// <summary>Скачивает новую версию во временную папку и возвращает путь к файлу.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Shotly-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Shotly-{info.Version}.exe");

        // Timeout HttpClient не ограничивает чтение тела ответа, поэтому общий предел на всю загрузку.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            using var response = await Http.Value.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            response.EnsureSuccessStatusCode();
            await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                await response.Content.CopyToAsync(file, limit.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            try { File.Delete(path); } catch (IOException) { }
            throw new UpdateException(ex is OperationCanceledException
                ? "Загрузка обновления прервана по таймауту."
                : "Не удалось скачать обновление.", ex);
        }

        // Защита от обрезанной загрузки: настоящий exe весит десятки мегабайт.
        if (new FileInfo(path).Length < 1024 * 1024)
        {
            File.Delete(path);
            throw new UpdateException("Скачанный файл повреждён.");
        }

        return path;
    }

    /// <summary>
    /// Заменяет текущий exe новым после выхода процесса и запускает его.
    /// Скрипт ждёт выхода процесса и повторяет замену раз в секунду (до 30 раз), если файл ещё занят.
    /// После вызова приложение должно завершиться.
    /// </summary>
    public static void ApplyAndRestart(string newExe)
    {
        var exe = Installation.Quote(Installation.ExePath);
        var source = Installation.Quote(newExe);
        Installation.RunAfterExit($@"
for ($i = 0; $i -lt 30; $i++) {{
  try {{ Move-Item -LiteralPath {source} -Destination {exe} -Force -ErrorAction Stop; break }}
  catch {{ Start-Sleep -Seconds 1 }}
}}
Start-Process -FilePath {exe}");
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
