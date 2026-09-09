using System.Security.Cryptography;
using System.Text;

namespace GdTracker.Api.Security;

/// <summary>
/// Хеширование паролей: PBKDF2-HMAC-SHA256 со случайной солью.
/// Формат строки — <c>pbkdf2-sha256$итерации$сольBase64$хешBase64</c>: параметры
/// хранятся рядом с хешем, поэтому старые пароли продолжат проверяться,
/// даже если число итераций в коде вырастет.
/// </summary>
public static class PasswordHasher
{
    /// <summary>Число итераций для новых хешей (рекомендация OWASP для PBKDF2-SHA256).</summary>
    public const int DefaultIterations = 210_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, HashSize);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Проверяет пароль. Возвращает false и на неверном пароле, и на испорченной
    /// строке хеша — вызывающему коду в обоих случаях нужно одно и то же: отказ во входе.
    /// </summary>
    public static bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encodedHash))
            return false;

        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            return false;

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (salt.Length == 0 || expected.Length == 0)
            return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Сравнение за постоянное время: обычное сравнение массивов раскрывает
        // по времени, сколько первых байт хеша совпало.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
