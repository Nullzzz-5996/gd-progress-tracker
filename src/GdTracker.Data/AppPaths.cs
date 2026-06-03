namespace GdTracker.Data;

/// <summary>
/// Пути приложения в каталоге пользователя (%LOCALAPPDATA%\GdTracker).
/// </summary>
public static class AppPaths
{
    /// <summary>Корневой каталог данных приложения.</summary>
    public static string AppDataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GdTracker");

    /// <summary>Путь к файлу БД SQLite.</summary>
    public static string DatabasePath { get; } = Path.Combine(AppDataDir, "gdtracker.db");

    /// <summary>Каталог для хранения видеофайлов (Фаза 4).</summary>
    public static string MediaDir { get; } = Path.Combine(AppDataDir, "media");

    /// <summary>Строка подключения SQLite к БД приложения.</summary>
    public static string ConnectionString { get; } = $"Data Source={DatabasePath}";

    /// <summary>Создаёт каталоги данных, если их ещё нет.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDir);
        Directory.CreateDirectory(MediaDir);
    }
}
