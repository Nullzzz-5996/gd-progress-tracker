using System.Security.Cryptography;

namespace GdTracker.Api.Security;

/// <summary>
/// Источник ключа подписи токенов.
/// В боевой среде ключ задаётся конфигурацией (<c>Auth:SigningKey</c> или переменная
/// окружения <c>GDTRACKER_SIGNING_KEY</c>). Если он не задан, сервер генерирует
/// случайный ключ и сохраняет его в файл рядом с БД: без этого при каждом перезапуске
/// все выданные токены становились бы недействительными.
/// </summary>
public static class SigningKeyProvider
{
    private const int KeySizeBytes = 64;

    public static byte[] Resolve(string? configuredKey, string dataDirectory, out bool generated)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
        {
            generated = false;
            var bytes = System.Text.Encoding.UTF8.GetBytes(configuredKey);
            // HMAC-SHA256 требует ключ не короче 256 бит; короткие ключи из
            // конфигурации растягиваем хешем, а не отвергаем.
            return bytes.Length >= 32 ? bytes : SHA512.HashData(bytes);
        }

        generated = true;
        Directory.CreateDirectory(dataDirectory);
        var keyPath = Path.Combine(dataDirectory, "signing.key");

        if (File.Exists(keyPath))
        {
            try
            {
                var stored = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
                if (stored.Length >= 32)
                    return stored;
            }
            catch (FormatException)
            {
                // Испорченный файл ключа перезапишем новым: все текущие токены
                // станут недействительными, но сервер поднимется.
            }
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        File.WriteAllText(keyPath, Convert.ToBase64String(key));
        return key;
    }
}
