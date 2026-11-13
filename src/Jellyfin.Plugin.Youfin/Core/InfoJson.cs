using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Jellyfin.Plugin.Youfin.Core;

/// <summary>Subset of yt-dlp info.json that Jellyfin does not store.</summary>
public sealed record InfoJson(
    string? ChannelId,
    string? Channel,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Categories);

public static class InfoJsonReader
{
    /// <summary>Reads "&lt;video&gt;.info.json" next to the video. Never throws; returns null if missing/invalid.</summary>
    public static InfoJson? TryReadForVideo(string? videoPath)
    {
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return null;
        }

        try
        {
            var file = Path.ChangeExtension(videoPath, ".info.json");
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static InfoJson? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new InfoJson(
                Str(r, "channel_id"),
                Str(r, "channel") ?? Str(r, "uploader"),
                List(r, "tags"),
                List(r, "categories"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString() : null;

    private static IReadOnlyList<string> List(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : Array.Empty<string>();
}
