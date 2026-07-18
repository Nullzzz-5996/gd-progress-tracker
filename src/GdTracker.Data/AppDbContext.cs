using GdTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data;

/// <summary>Контекст БД приложения (EF Core + SQLite).</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Level> Levels => Set<Level>();
    public DbSet<ProgressRecord> ProgressRecords => Set<ProgressRecord>();
    public DbSet<VideoClip> VideoClips => Set<VideoClip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Level>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Creator).HasMaxLength(100);
            e.Property(x => x.Difficulty).HasMaxLength(40);
            e.HasIndex(x => x.GdLevelId);

            e.HasMany(x => x.ProgressRecords)
                .WithOne(x => x.Level!)
                .HasForeignKey(x => x.LevelId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProgressRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(1000);

            e.HasMany(x => x.Videos)
                .WithOne()
                .HasForeignKey(x => x.ProgressRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VideoClip>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OriginalName).HasMaxLength(260);

            // Необязательная привязка клипа к уровню (помимо привязки к записи прогресса).
            e.HasOne<Level>()
                .WithMany()
                .HasForeignKey(x => x.LevelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
