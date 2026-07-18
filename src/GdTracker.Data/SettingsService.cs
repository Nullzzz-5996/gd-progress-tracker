using System.Text.Json;
using GdTracker.Core.Abstractions;

namespace GdTracker.Data;

/// <inheritdoc />
public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private AppSettings _settings;

    /// <summary>Боевой конструктор: settings.json рядом с БД приложения.</summary>
    public SettingsService() : this(Path.Combine(AppPaths.AppDataDir, "settings.json"))
    {
    }

    /// <summary>Конструктор с явным путём (используется тестами).</summary>
    public SettingsService(string filePath)
    {
        _filePath = filePath;
        _settings = Load(filePath);
    }

    public string? SaveFilePath => _settings.SaveFilePath;

    public void SetSaveFilePath(string? path)
    {
        _settings = _settings with { SaveFilePath = string.IsNullOrWhiteSpace(path) ? null : path };
        Save();
    }

    private static AppSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Битые или недоступные настройки не должны мешать запуску приложения.
            return new AppSettings();
        }
    }

    /// <summary>
    /// Сохраняет настройки в файл атомарно: пишет во временный файл, затем заменяет целевой.
    /// Ошибки сохранения не прерывают работу приложения (оно продолжит со значениями в памяти).
    /// </summary>
    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmpPath = _filePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Ошибки сохранения настроек не должны мешать работе приложения.
            // Приложение продолжит работать с текущими значениями в памяти.
        }
        finally
        {
            // Постарайся убрать временный файл, но неудача уборки не должна ронять исключение.
            try
            {
                var tmpPath = _filePath + ".tmp";
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
                // Игнорируем ошибки уборки временного файла
            }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Содержимое settings.json.</summary>
    private sealed record AppSettings
    {
        public string? SaveFilePath { get; init; }
    }
}
