using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GdTracker.Data;

/// <summary>
/// Design-time фабрика контекста — используется инструментами EF Core
/// (<c>dotnet ef migrations</c>), чтобы создавать миграции без запуска WPF-приложения.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(AppPaths.ConnectionString)
            .Options;

        return new AppDbContext(options);
    }
}
