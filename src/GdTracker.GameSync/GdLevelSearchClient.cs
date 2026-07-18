using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <summary>
/// Клиент поиска уровней на серверах Geometry Dash (Boomlings getGJLevels21).
/// Только чтение. User-Agent не задаётся (браузерный UA блокируется Cloudflare).
/// </summary>
public sealed class GdLevelSearchClient : IGdLevelSearch
{
    private const string Endpoint = "http://www.boomlings.com/database/getGJLevels21.php";
    private const string Secret = "Wmfd2893gb7";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<IReadOnlyList<OnlineLevel>> SearchAsync(string query, CancellationToken ct = default)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return [];

        var form = new Dictionary<string, string>
        {
            ["secret"] = Secret,
            ["type"] = "0",          // 0 = поиск по строке/ID
            ["str"] = trimmed,
            ["gameVersion"] = "22",
            ["binaryVersion"] = "47",
            ["page"] = "0",
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await Http.PostAsync(Endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        return GdSearchResponseParser.Parse(body);
    }
}
