using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Ponte.Stremio;

// Wire types of the Stremio addon protocol:
// https://github.com/Stremio/stremio-addon-sdk/tree/master/docs/api

/// <summary>JSON settings for addon responses: camelCase, nulls left out.</summary>
public static class StremioJson
{
    /// <summary>The serializer options every addon response uses.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>The addon manifest.</summary>
public sealed class Manifest
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public string? Logo { get; init; }

    public required IReadOnlyList<object> Resources { get; init; }

    public required IReadOnlyList<string> Types { get; init; }

    public required IReadOnlyList<string> IdPrefixes { get; init; }

    public required IReadOnlyList<CatalogDefinition> Catalogs { get; init; }

    public ManifestHints? BehaviorHints { get; init; }
}

/// <summary>A resource restricted to some types and id prefixes.</summary>
public sealed record ResourceDefinition(string Name, IReadOnlyList<string> Types, IReadOnlyList<string> IdPrefixes);

/// <summary>Manifest behaviour hints.</summary>
public sealed record ManifestHints(bool Configurable, bool ConfigurationRequired);

/// <summary>A catalog the addon offers.</summary>
public sealed record CatalogDefinition(string Type, string Id, string Name, IReadOnlyList<ExtraDefinition> Extra);

/// <summary>An argument a catalog accepts.</summary>
public sealed record ExtraDefinition(string Name, bool? IsRequired = null, IReadOnlyList<string>? Options = null);

/// <summary>A title, as listed in a catalog or returned by the meta resource.</summary>
public sealed class Meta
{
    public required string Id { get; init; }

    public required string Type { get; init; }

    public required string Name { get; init; }

    public string? Poster { get; init; }

    public string? Background { get; init; }

    public string? Logo { get; init; }

    public string? Description { get; init; }

    public string? ReleaseInfo { get; init; }

    public IReadOnlyList<string>? Genres { get; init; }

    public string? ImdbRating { get; init; }

    public string? Runtime { get; init; }

    public IReadOnlyList<Video>? Videos { get; init; }
}

/// <summary>One episode of a series.</summary>
public sealed class Video
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public int Season { get; init; }

    public int Episode { get; init; }

    public string? Released { get; init; }

    public string? Overview { get; init; }

    public string? Thumbnail { get; init; }
}

/// <summary>A playable stream.</summary>
public sealed class Stream
{
    public required string Url { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public StreamHints? BehaviorHints { get; init; }

    /// <summary>Everything known about the file, for AIOStreams; other clients ignore it.</summary>
    public StreamData? StreamData { get; init; }
}

/// <summary>Stream behaviour hints.</summary>
public sealed class StreamHints
{
    public string? Filename { get; init; }

    public long? VideoSize { get; init; }

    public string? BingeGroup { get; init; }

    public bool? NotWebReady { get; init; }
}

/// <summary>AIOStreams' description of a stream.</summary>
public sealed class StreamData
{
    public required string Addon { get; init; }

    public string Type { get; init; } = "http";

    public bool Library { get; init; } = true;

    public required string Filename { get; init; }

    public long? Size { get; init; }

    /// <summary>The running time in milliseconds.</summary>
    public long? Duration { get; init; }

    public required ParsedFile ParsedFile { get; init; }
}

/// <summary>AIOStreams' parsed view of a file, in its own vocabulary.</summary>
public sealed class ParsedFile
{
    public string? Title { get; init; }

    public string? Year { get; init; }

    public IReadOnlyList<int>? Seasons { get; init; }

    public IReadOnlyList<int>? Episodes { get; init; }

    public string? Resolution { get; init; }

    public string? Quality { get; init; }

    public string? Encode { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? Network { get; init; }

    public IReadOnlyList<string>? Editions { get; init; }

    public bool? Repack { get; init; }

    public bool? Proper { get; init; }

    public required IReadOnlyList<string> VisualTags { get; init; }

    public required IReadOnlyList<string> AudioTags { get; init; }

    public required IReadOnlyList<string> AudioChannels { get; init; }

    public required IReadOnlyList<string> Languages { get; init; }

    public required IReadOnlyList<string> Subtitles { get; init; }

    public required IReadOnlyList<MediaTrack> AudioTracks { get; init; }

    public required IReadOnlyList<MediaTrack> SubtitleTracks { get; init; }

    public int? VideoIndex { get; init; }

    public string? Container { get; init; }

    public string? Extension { get; init; }

    /// <summary>Where the track data comes from: Jellyfin's ffprobe scan of the file.</summary>
    public string MediaInfoQuality { get; init; } = "probe";
}

/// <summary>One audio or subtitle track of a file.</summary>
public sealed class MediaTrack
{
    public int? Index { get; init; }

    public string? Lang { get; init; }

    public string? Codec { get; init; }

    public string? Title { get; init; }

    public string? Tag { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public string? Channels { get; init; }

    public bool? Default { get; init; }

    public bool? Forced { get; init; }

    public bool? Commentary { get; init; }

    public bool? HearingImpaired { get; init; }
}

/// <summary>A subtitle file offered through the subtitles resource.</summary>
/// <param name="Id">The subtitle's id.</param>
/// <param name="Url">Where the file is served.</param>
/// <param name="Lang">The language code.</param>
/// <param name="Label">The name players that support labels show next to the language.</param>
/// <param name="Forced">True for a forced subtitle.</param>
/// <param name="HearingImpaired">True for an SDH subtitle.</param>
public sealed record Subtitle(
    string Id,
    string Url,
    string Lang,
    string? Label = null,
    bool? Forced = null,
    bool? HearingImpaired = null);
