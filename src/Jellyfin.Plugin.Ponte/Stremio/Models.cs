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
}

/// <summary>Stream behaviour hints.</summary>
public sealed class StreamHints
{
    public string? Filename { get; init; }

    public long? VideoSize { get; init; }

    public string? BingeGroup { get; init; }

    public bool? NotWebReady { get; init; }
}

/// <summary>A subtitle track.</summary>
public sealed record Subtitle(string Id, string Url, string Lang);
