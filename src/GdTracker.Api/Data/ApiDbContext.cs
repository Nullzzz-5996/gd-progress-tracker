using GdTracker.Sharing.Cloud;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Data;

/// <summary>Аккаунт пользователя облачной синхронизации.</summary>
public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Адрес почты в нормализованном виде (нижний регистр) — он же логин.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Хеш пароля вместе с параметрами и солью (см. <c>PasswordHasher</c>).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// Ник — публичное имя аккаунта: под ним он виден в демонлисте, в мнениях
    /// о размещении и в топе рекордов. Хранится так, как его ввёл владелец.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Тот же ник в нижнем регистре — по нему идёт проверка на занятость.
    /// Отдельный столбец, а не <c>lower(Username)</c> в запросе: в SQLite
    /// встроенный <c>lower</c> знает только латиницу, и «Игрок» с «игрок»
    /// оказались бы разными никами.
    /// </summary>
    public string UsernameNormalized { get; set; } = string.Empty;

    /// <summary>Права в сообществе. По умолчанию — обычный участник.</summary>
    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>
    /// Полный бан: аккаунт не входит и не синхронизируется. Строку не удаляем —
    /// иначе тот же человек завёл бы её заново той же почтой через минуту.
    /// </summary>
    public bool IsBanned { get; set; }

    /// <summary>Пояснение к полному бану: его видит забаненный при попытке входа.</summary>
    public string? BanReason { get; set; }

    public DateTime? BannedAtUtc { get; set; }

    /// <summary>
    /// Бан в демонлисте: вход и облако остаются, участие в списке — нет.
    /// Ни мнений, ни заявок, а прежние записи из публичных списков пропадают.
    /// </summary>
    public bool IsListBanned { get; set; }

    /// <summary>Пояснение к бану в демонлисте.</summary>
    public string? ListBanReason { get; set; }

    public DateTime? ListBannedAtUtc { get; set; }

    public ProgressSnapshot? Snapshot { get; set; }
}

/// <summary>
/// Снимок прогресса пользователя. На аккаунт хранится ровно один снимок:
/// клиент сливает облачные и локальные данные у себя и загружает результат целиком,
/// поэтому серверу не нужна история версий — только текущая ревизия для защиты
/// от перезаписи чужих изменений.
/// </summary>
public sealed class ProgressSnapshot
{
    public Guid UserId { get; set; }

    /// <summary>Номер ревизии, увеличивается на единицу при каждой успешной записи.</summary>
    public long Revision { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Пакет прогресса как JSON — сервер его не разбирает, только хранит.</summary>
    public string PayloadJson { get; set; } = string.Empty;
}

/// <summary>Контекст БД сервера (EF Core + SQLite).</summary>
public sealed class ApiDbContext : DbContext
{
    public ApiDbContext(DbContextOptions<ApiDbContext> options) : base(options)
    {
    }

    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<ProgressSnapshot> Snapshots => Set<ProgressSnapshot>();
    public DbSet<DemonListEntry> Demons => Set<DemonListEntry>();
    public DbSet<PlacementOpinion> Opinions => Set<PlacementOpinion>();
    public DbSet<RecordSubmission> Records => Set<RecordSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).IsRequired().HasMaxLength(254);
            e.Property(x => x.PasswordHash).IsRequired().HasMaxLength(400);
            e.Property(x => x.Username).IsRequired().HasMaxLength(Usernames.MaxLength);
            e.Property(x => x.UsernameNormalized).IsRequired().HasMaxLength(Usernames.MaxLength);
            e.Property(x => x.BanReason).HasMaxLength(500);
            e.Property(x => x.ListBanReason).HasMaxLength(500);
            // Уникальные индексы — единственная надёжная защита от гонки при
            // одновременной регистрации одной и той же почты (или ника) двумя запросами.
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.UsernameNormalized).IsUnique();

            e.HasOne(x => x.Snapshot)
                .WithOne()
                .HasForeignKey<ProgressSnapshot>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProgressSnapshot>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.PayloadJson).IsRequired();
        });

        modelBuilder.Entity<DemonListEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Publisher).HasMaxLength(200);
            e.Property(x => x.Verifier).HasMaxLength(200);
            e.Property(x => x.Video).HasMaxLength(500);
            e.Property(x => x.Thumbnail).HasMaxLength(500);
            // Индекс, но не уникальный: перестановка сдвигает соседние места
            // по одной строке за раз, и в середине этой цепочки два демона
            // на короткое время делят одно место.
            e.HasIndex(x => x.Position);
        });

        modelBuilder.Entity<PlacementOpinion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).IsRequired().HasMaxLength(1000);
            e.HasIndex(x => x.DemonId);

            e.HasOne<DemonListEntry>()
                .WithMany()
                .HasForeignKey(x => x.DemonId)
                .OnDelete(DeleteBehavior.Cascade);

            // Удалили аккаунт — уходят и его мнения: безымянных записей в списке нет.
            e.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RecordSubmission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.LevelName).IsRequired().HasMaxLength(200);
            e.Property(x => x.VideoUrl).IsRequired().HasMaxLength(500);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.HasIndex(x => x.Status);

            e.HasOne<UserAccount>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Демона могут снять со списка, а рекорд по нему остаётся:
            // ссылка обнуляется, название уровня хранится в самой заявке.
            e.HasOne<DemonListEntry>()
                .WithMany()
                .HasForeignKey(x => x.DemonId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
