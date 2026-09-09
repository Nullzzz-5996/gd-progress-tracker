using System.Text;
using System.Text.RegularExpressions;
using GdTracker.Sharing.Cloud;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Data;

/// <summary>
/// Правила ников. Ник — публичное имя аккаунта: он виден в демонлисте, в мнениях
/// о размещении и в топе пройденных уровней, поэтому он уникален и не меняется
/// втихую под другим человеком.
/// </summary>
public static partial class Usernames
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    /// <summary>
    /// Разрешены буквы (в том числе кириллица), цифры, дефис и подчёркивание.
    /// Пробелов нет намеренно: ник должен читаться в одну строку в таблицах топа
    /// и не притворяться двумя разными именами из-за двойного пробела.
    /// </summary>
    [GeneratedRegex(@"^[\p{L}\p{Nd}_-]+$")]
    private static partial Regex Allowed();

    /// <summary>Ник в каноническом виде для сравнения: регистр и края не считаются.</summary>
    public static string Normalize(string username) => username.Trim().ToLowerInvariant();

    /// <summary>
    /// Проверяет ник. Возвращает описание ошибки либо null, если ник годится.
    /// </summary>
    public static ApiError? Validate(string? username)
    {
        var value = (username ?? string.Empty).Trim();

        if (value.Length is < MinLength or > MaxLength)
            return new ApiError("invalid_username", $"Ник должен быть от {MinLength} до {MaxLength} символов.");

        if (!Allowed().IsMatch(value))
            return new ApiError(
                "invalid_username",
                "В нике допустимы буквы, цифры, дефис и подчёркивание — без пробелов и знаков препинания.");

        // Ник из одних дефисов читается как пустое место и путает таблицы топа.
        if (!value.Any(char.IsLetterOrDigit))
            return new ApiError("invalid_username", "В нике должна быть хотя бы одна буква или цифра.");

        return null;
    }

    /// <summary>Занят ли ник кем-то, кроме указанного аккаунта.</summary>
    public static Task<bool> IsTakenAsync(
        ApiDbContext db, string normalized, Guid? exceptUserId = null, CancellationToken ct = default) =>
        db.Users.AnyAsync(u => u.UsernameNormalized == normalized && (exceptUserId == null || u.Id != exceptUserId), ct);

    /// <summary>
    /// Придумывает ник по адресу почты — для аккаунтов, заведённых без него
    /// (старым клиентом или до появления ников вообще). К занятому имени
    /// добавляется номер, пока не найдётся свободное.
    /// </summary>
    public static async Task<string> DeriveAsync(
        ApiDbContext db, string email, CancellationToken ct = default)
    {
        var seed = Sanitize(email);
        var candidate = seed;

        for (var suffix = 2; await IsTakenAsync(db, Normalize(candidate), null, ct); suffix++)
        {
            var tail = suffix.ToString();
            // Номер приклеивается к укороченному имени, иначе ник вылезет за предел длины.
            candidate = seed[..Math.Min(seed.Length, MaxLength - tail.Length)] + tail;
        }

        return candidate;
    }

    /// <summary>
    /// Часть почты до собачки, очищенная до допустимых символов и подтянутая
    /// до минимальной длины. Светить адрес целиком в публичном списке незачем.
    /// </summary>
    private static string Sanitize(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;

        var builder = new StringBuilder(local.Length);
        foreach (var ch in local)
        {
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-')
                builder.Append(ch);
        }

        var value = builder.ToString();
        if (value.Length > MaxLength)
            value = value[..MaxLength];

        // Почта вида «!!!@example.com» не даёт ни одного пригодного символа.
        return value.Length >= MinLength ? value : $"gd{value}".PadRight(MinLength, '0');
    }
}
