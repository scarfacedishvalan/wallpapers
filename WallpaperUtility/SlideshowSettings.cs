using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperUtility;

internal sealed class SlideshowSettings
{
    public required string FolderPath { get; init; }

    /// <summary>Optional; when true the "Compose collages" option starts ticked.</summary>
    public bool ComposeCollages { get; init; }

    public static SlideshowSettings Load()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = JsonSerializer.Deserialize<SlideshowSettings>(File.ReadAllText(file),
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new JsonException("Settings must contain a JSON object.");
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FolderPath);
        if (!Path.IsPathFullyQualified(settings.FolderPath))
        {
            throw new ArgumentException("FolderPath in appsettings.json must be an absolute folder path.");
        }

        return settings;
    }
}
