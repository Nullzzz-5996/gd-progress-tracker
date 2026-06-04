using System.Text.Json;
using System.Text.Json.Serialization;

namespace GdTracker.Sharing;

/// <summary>Сериализация пакета прогресса в JSON и обратно.</summary>
public static class ProgressPackageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ProgressPackage package) => JsonSerializer.Serialize(package, Options);

    public static ProgressPackage Deserialize(string json)
        => JsonSerializer.Deserialize<ProgressPackage>(json, Options) ?? new ProgressPackage();
}
