# Сборка и автозапуск

## Готовый .exe из GitHub Actions

Каждый push и pull request собирается на `windows-latest` (`.github/workflows/build.yml`).
После каждого мержа в `main` готовый `Shotly.exe` публикуется в [Releases → latest](https://github.com/loudscreamx/Shotly/releases/tag/latest).
Для pull request файл лежит в артефакте **Shotly-win-x64** внизу страницы запуска во вкладке Actions (скачивается zip).

## Сборка на своём компьютере

Нужен [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
# Быстрый запуск для разработки
dotnet run --project src/Shotly

# Один самодостаточный .exe (не требует установленного .NET)
dotnet publish src/Shotly/Shotly.csproj -c Release -o publish
# → publish\Shotly.exe
```

Все параметры публикации уже прописаны в `Shotly.csproj` для конфигурации Release:

| Параметр | Значение | Зачем |
| --- | --- | --- |
| `SelfContained` | true | .NET встроен в exe, ставить ничего не нужно |
| `PublishSingleFile` | true | один файл вместо папки с DLL |
| `PublishReadyToRun` | true | код заранее скомпилирован, быстрее холодный старт |
| `IncludeNativeLibrariesForSelfExtract` | true | нативные DLL WPF тоже внутри exe |
| `EnableCompressionInSingleFile` | true | ~60–75 МБ вместо ~150 МБ ценой небольшой задержки на распаковку при старте |
| `PublishTrimmed` | false | WPF не поддерживает trimming |

Если важнее скорость старта, чем размер, поставьте `EnableCompressionInSingleFile` в `false`.

## Автозапуск

Включается в меню трея: **«Запускать при входе в Windows»**. Shotly записывает в
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` значение `Shotly` = `"<путь к Shotly.exe>" --autostart`.
Это работает без прав администратора и только для текущего пользователя.

Если exe перенести в другую папку, автозапуск нужно выключить и включить снова, чтобы обновить путь.
Хранить exe лучше в постоянном месте, например `%LocalAppData%\Programs\Shotly\Shotly.exe`.

Альтернатива без программы: ярлык на `Shotly.exe` в папке `shell:startup` (Win+R → `shell:startup`).

## Где что лежит

| Что | Путь |
| --- | --- |
| Настройки | `%AppData%\Shotly\settings.json` |
| Лог | `%LocalAppData%\Shotly\shotly.log` |
| Снимки по умолчанию | `Изображения\Shotly` |
