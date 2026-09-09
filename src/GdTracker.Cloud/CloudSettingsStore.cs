using System.Text.Json;

namespace GdTracker.Cloud;

/// <summary>
/// Настройки синхронизации, переживающие перезапуск. Хранятся отдельно от
/// <c>settings.json</c>: облако — необязательная часть приложения, и его настройки
/// не должны мешаться под ногами у тех, кто аккаунтом не пользуется.
/// </summary>
public interface ICloudSettingsStore
{
    /// <summary>Адрес сервера синхронизации.</summary>
    string ServerUrl { get; set; }

    /// <summary>Последняя использованная почта — чтобы не набирать её при каждом входе.</summary>
    string? LastEmail { get; set; }

    /// <summary>Синхронизировать ли автоматически при запуске приложения.</summary>
    bool AutoSyncOnStartup { get; set; }

    /// <summary>Момент последней успешной синхронизации.</summary>
    DateTime? LastSyncAtUtc { get; set; }

    /// <summary>Ревизия снимка в облаке на момент последней синхронизации.</summary>
    long LastRevision { get; set; }
}

/// <summary>Настройки синхронизации в JSON-файле рядом с базой приложения.</summary>
public sealed class CloudSettingsStore : ICloudSettingsStore
{
    /// <summary>
    /// Адрес сервера синхронизации по умолчанию. Это единственное место, где он задан:
    /// после развёртывания боевого сервера (см. README, раздел «Сервер синхронизации»)
    /// достаточно поменять эту строку — приложение подставит новый адрес всем, кто не
    /// вводил свой вручную. Пока сервер не развёрнут, здесь локальный запуск из этого же
    /// репозитория: <c>dotnet run --project src/GdTracker.Api</c>.
    /// </summary>
    public const string DefaultServerUrl = "http://localhost:5080";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private CloudSettings _settings;

    public CloudSettingsStore() : this(CloudPaths.SettingsPath)
    {
    }

    public CloudSettingsStore(string filePath)
    {
        _filePath = filePath;
        _settings = Load(filePath);
    }

    public string ServerUrl
    {
        get => string.IsNullOrWhiteSpace(_settings.ServerUrl) ? DefaultServerUrl : _settings.ServerUrl!;
        set => Update(_settings with { ServerUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public string? LastEmail
    {
        get => _settings.LastEmail;
        set => Update(_settings with { LastEmail = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
    }

    public bool AutoSyncOnStartup
    {
        get => _settings.AutoSyncOnStartup;
        set => Update(_settings with { AutoSyncOnStartup = value });
    }

    public DateTime? LastSyncAtUtc
    {
        get => _settings.LastSyncAtUtc;
        set => Update(_settings with { LastSyncAtUtc = value });
    }

    public long LastRevision
    {
        get => _settings.LastRevision;
        set => Update(_settings with { LastRevision = value });
    }

    private void Update(CloudSettings updated)
    {
        _settings = updated;
        Save();
    }

    private static CloudSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new CloudSettings();

            return JsonSerializer.Deserialize<CloudSettings>(File.ReadAllText(filePath)) ?? new CloudSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Битые настройки облака не должны мешать запуску приложения.
            return new CloudSettings();
        }
    }

    /// <summary>Запись через временный файл — как в <c>SettingsService</c>.</summary>
    private void Save()
    {
        var tmpPath = _filePath + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(tmpPath, JsonSerializer.Serialize(_settings, JsonOptions));
            File.Move(tmpPath, _filePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Настройки останутся в памяти до конца сеанса — это лучше, чем падение.
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);
            }
            catch
            {
                // Уборка временного файла не важна настолько, чтобы ронять исключение.
            }
        }
    }

    /// <summary>Содержимое cloud.json.</summary>
    private sealed record CloudSettings
    {
        public string? ServerUrl { get; init; }
        public string? LastEmail { get; init; }
        public bool AutoSyncOnStartup { get; init; }
        public DateTime? LastSyncAtUtc { get; init; }
        public long LastRevision { get; init; }
    }
}
