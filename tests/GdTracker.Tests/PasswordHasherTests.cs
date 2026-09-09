using FluentAssertions;
using GdTracker.Api.Security;

namespace GdTracker.Tests;

/// <summary>Тесты хеширования паролей на сервере синхронизации.</summary>
public class PasswordHasherTests
{
    // Пониженное число итераций: тесты проверяют логику, а не стойкость к перебору,
    // и не должны тратить секунды на каждый вызов.
    private const int FastIterations = 1_000;

    [Fact]
    public void Hash_verifies_correct_password()
    {
        var hash = PasswordHasher.Hash("правильный-пароль", FastIterations);

        PasswordHasher.Verify("правильный-пароль", hash).Should().BeTrue();
    }

    [Fact]
    public void Hash_rejects_wrong_password()
    {
        var hash = PasswordHasher.Hash("правильный-пароль", FastIterations);

        PasswordHasher.Verify("другой-пароль", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_of_same_password_differs_because_of_salt()
    {
        var first = PasswordHasher.Hash("одинаковый", FastIterations);
        var second = PasswordHasher.Hash("одинаковый", FastIterations);

        first.Should().NotBe(second);
        PasswordHasher.Verify("одинаковый", first).Should().BeTrue();
        PasswordHasher.Verify("одинаковый", second).Should().BeTrue();
    }

    [Fact]
    public void Hash_stores_iteration_count_so_old_hashes_keep_verifying()
    {
        var hash = PasswordHasher.Hash("пароль", FastIterations);

        hash.Should().StartWith($"pbkdf2-sha256${FastIterations}$");
        PasswordHasher.Verify("пароль", hash).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("мусор")]
    [InlineData("pbkdf2-sha256$нечисло$c29sdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$1000$не-base64$aGFzaA==")]
    [InlineData("bcrypt$1000$c29sdA==$aGFzaA==")]
    public void Verify_returns_false_for_broken_hash_instead_of_throwing(string encodedHash)
    {
        PasswordHasher.Verify("пароль", encodedHash).Should().BeFalse();
    }

    [Fact]
    public void Verify_returns_false_for_empty_password()
    {
        var hash = PasswordHasher.Hash("пароль", FastIterations);

        PasswordHasher.Verify(string.Empty, hash).Should().BeFalse();
    }
}
