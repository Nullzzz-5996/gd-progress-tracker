namespace GdTracker.GameSync;

/// <summary>Поиск сейв-файлов Geometry Dash на Windows.</summary>
public static class SaveFileLocator
{
    /// <summary>Ожидаемый путь к CCGameManager.dat (может не существовать).</summary>
    public static string GameManagerPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GeometryDash", "CCGameManager.dat");

    /// <summary>Путь к CCGameManager.dat, если файл существует; иначе null.</summary>
    public static string? DefaultGameManagerPath()
    {
        var path = GameManagerPath();
        return File.Exists(path) ? path : null;
    }
}
