using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Поиск уровней на серверах Geometry Dash (по названию или ID).</summary>
public interface IGdLevelSearch
{
    Task<IReadOnlyList<OnlineLevel>> SearchAsync(string query, CancellationToken ct = default);
}
