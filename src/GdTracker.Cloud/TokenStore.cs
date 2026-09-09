using GdTracker.Sharing.Cloud;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GdTracker.Cloud;

/// <summary>
/// Каталог данных облачной части. Совпадает с <c>GdTracker.Data.AppPaths.AppDataDir</c>,
/// но задан здесь отдельно: слой Data ссылается на Cloud, а не наоборот, поэтому
/// брать путь из Data было бы циклической зависимостью.
/// </summary>
public static class CloudPaths
{
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GdTracker");

    /// <summary>Файл с зашифрованным токеном доступа.</summary>
    public static string TokenPath { get; } = Path.Combine(AppDataDir, "cloud.token");

    /// <summary>Файл настроек синхронизации (без секретов, читаемый глазами).</summary>
    public static string SettingsPath { get; } = Path.Combine(AppDataDir, "cloud.json");
}

/// <summary>Сохранённая сессия: токен доступа и к кому он относится.</summary>
public sealed record StoredToken(
    string UserId,
    string Email,
    string Username,
    UserRole Role,
    string AccessToken,
    DateTime ExpiresAtUtc)
{
    /// <summary>
    /// Считаем токен просроченным за минуту до фактического истечения: запрос,
    /// отправленный в последнюю секунду жизни токена, дошёл бы до сервера уже мёртвым.
    /// </summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc - TimeSpan.FromMinutes(1);
}

/// <summary>Хранилище токена между запусками приложения.</summary>
public interface ITokenStore
{
    /// <summary>Читает сохранённый токен, либо null, если его нет или файл нечитаем.</summary>
    StoredToken? Load();

    void Save(StoredToken token);

    void Clear();
}

/// <summary>
/// Хранит токен в файле, зашифрованном DPAPI на текущего пользователя Windows:
/// другой пользователь того же компьютера файл расшифровать не сможет.
/// </summary>
public sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _filePath;

    public DpapiTokenStore() : this(CloudPaths.TokenPath)
    {
    }

    public DpapiTokenStore(string filePath) => _filePath = filePath;

    public StoredToken? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var protectedBytes = Convert.FromBase64String(File.ReadAllText(_filePath));
            var json = Encoding.UTF8.GetString(Unprotect(protectedBytes));

            return JsonSerializer.Deserialize<StoredToken>(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException
                                      or JsonException or CryptographicException)
        {
            // Испорченный, чужой или недоступный файл токена означает ровно одно:
            // сессии нет. Приложение при этом обязано запуститься как обычно.
            return null;
        }
    }

    public void Save(StoredToken token)
    {
        var json = JsonSerializer.Serialize(token);
        var protectedBytes = Protect(Encoding.UTF8.GetBytes(json));

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_filePath, Convert.ToBase64String(protectedBytes));
    }

    /// <summary>
    /// DPAPI существует только в Windows, а сборка нацелена на net10.0 без суффикса
    /// платформы (её тянет за собой слой данных, общий с тестами), поэтому вызовы
    /// закрыты явной проверкой ОС — без неё анализатор совместимости справедливо ругается.
    /// </summary>
    private static byte[] Protect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Хранение токена поддерживается только в Windows.");

        return ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Хранение токена поддерживается только в Windows.");

        return ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Не удалось стереть файл — сессия всё равно уже сброшена в памяти.
        }
    }
}
