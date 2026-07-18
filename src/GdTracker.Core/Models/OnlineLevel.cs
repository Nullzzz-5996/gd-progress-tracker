namespace GdTracker.Core.Models;

/// <summary>Уровень из онлайн-поиска по серверам Geometry Dash.</summary>
public sealed record OnlineLevel
{
    public required long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Creator { get; init; }
    public string Difficulty { get; init; } = "Unrated";
    public int Stars { get; init; }
    public int Downloads { get; init; }
    public int Likes { get; init; }
}
