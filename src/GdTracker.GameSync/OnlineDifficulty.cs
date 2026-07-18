namespace GdTracker.GameSync;

/// <summary>
/// Метка сложности из ответа серверов GD (схема getGJLevels): k25 auto, k17 demon +
/// k43 demonDiff (3=Easy,4=Medium,5=Insane,6=Extreme,иначе Hard), k9 difficulty (10..50).
/// Отличается от схемы сейва (k76), поэтому отдельный резолвер.
/// </summary>
public static class OnlineDifficulty
{
    /// <param name="auto">k25 — авто.</param>
    /// <param name="demon">k17 — демон.</param>
    /// <param name="demonDiff">k43 — подтип демона (3=Easy,4=Medium,5=Insane,6=Extreme).</param>
    /// <param name="difficulty">k9 — сложность (10=Easy,20=Normal,30=Hard,40=Harder,50=Insane).</param>
    public static string Resolve(bool auto, bool demon, int demonDiff, int difficulty)
    {
        if (auto)
            return "Auto";

        if (demon)
        {
            var sub = demonDiff switch
            {
                3 => "Easy",
                4 => "Medium",
                5 => "Insane",
                6 => "Extreme",
                _ => "Hard",
            };
            return $"{sub} Demon";
        }

        return difficulty switch
        {
            10 => "Easy",
            20 => "Normal",
            30 => "Hard",
            40 => "Harder",
            50 => "Insane",
            _ => "Unrated",
        };
    }
}
