namespace GdTracker.GameSync;

/// <summary>
/// Вычисление метки сложности уровня из полей сейва (по логике gdcolon Save Explorer):
/// k33 auto, k25 demon + k76 demonType, k26 stars.
/// </summary>
public static class SaveDifficulty
{
    /// <param name="auto">k33 — авто-уровень.</param>
    /// <param name="demon">k25 — демон.</param>
    /// <param name="demonType">k76 — подтип демона (1=Hard,3=Easy,4=Medium,5=Insane,6=Extreme).</param>
    /// <param name="stars">k26 — звёзды (0–10).</param>
    private static readonly string[] StarLabels =
        ["Unrated", "Auto", "Easy", "Normal", "Hard", "Hard", "Harder", "Harder", "Insane", "Insane", "Demon"];

    private static readonly Dictionary<int, string> DemonSubtypes = new()
    {
        [1] = "Hard", [3] = "Easy", [4] = "Medium", [5] = "Insane", [6] = "Extreme",
    };

    public static string Resolve(bool auto, bool demon, int demonType, int stars)
    {
        if (auto)
            return "Auto";

        if (demon)
            return DemonSubtypes.TryGetValue(demonType, out var sub) ? $"{sub} Demon" : "Demon";

        return stars >= 0 && stars < StarLabels.Length ? StarLabels[stars] : "Unrated";
    }
}
