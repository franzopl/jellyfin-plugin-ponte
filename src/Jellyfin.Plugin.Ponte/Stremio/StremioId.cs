using System.Globalization;

namespace Jellyfin.Plugin.Ponte.Stremio;

/// <summary>
/// Which external database an id points into.
/// </summary>
public enum IdSource
{
    /// <summary>IMDb, <c>tt1234567</c>.</summary>
    Imdb,

    /// <summary>TMDB, <c>tmdb:603</c>.</summary>
    Tmdb,

    /// <summary>A Jellyfin item that has no external id, <c>ponte:&lt;guid&gt;</c>.</summary>
    Jellyfin,
}

/// <summary>
/// A Stremio video id: a title, optionally narrowed to one episode
/// (<c>tt0944947:1:2</c>, <c>tmdb:1399:1:2</c>, <c>ponte:&lt;guid&gt;:1:2</c>).
/// </summary>
public sealed record StremioId(IdSource Source, string Value, int? Season = null, int? Episode = null)
{
    /// <summary>The prefix of ids Ponte mints for items without an external id.</summary>
    public const string PontePrefix = "ponte:";

    /// <summary>Whether the id names a single episode.</summary>
    public bool IsEpisode => Season is not null && Episode is not null;

    /// <summary>The id of the title alone, without season and episode.</summary>
    public string TitleId => Source switch
    {
        IdSource.Imdb => Value,
        IdSource.Tmdb => "tmdb:" + Value,
        _ => PontePrefix + Value,
    };

    /// <summary>
    /// Parses a Stremio id. Returns null for ids from sources Ponte cannot look up (kitsu, mal, ...).
    /// </summary>
    public static StremioId? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = Uri.UnescapeDataString(raw.Trim()).Split(':');
        IdSource source;
        string value;
        int rest;

        if (parts[0].StartsWith("tt", StringComparison.OrdinalIgnoreCase) && parts[0].Length > 2)
        {
            source = IdSource.Imdb;
            value = parts[0].ToLowerInvariant();
            rest = 1;
        }
        else if (parts.Length >= 2 && parts[0].Equals("tmdb", StringComparison.OrdinalIgnoreCase))
        {
            source = IdSource.Tmdb;
            value = parts[1];
            rest = 2;
        }
        else if (parts.Length >= 2
                 && parts[0].Equals("ponte", StringComparison.OrdinalIgnoreCase)
                 && Guid.TryParse(parts[1], out var guid))
        {
            source = IdSource.Jellyfin;
            value = guid.ToString("N", CultureInfo.InvariantCulture);
            rest = 2;
        }
        else
        {
            return null;
        }

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (parts.Length == rest)
        {
            return new StremioId(source, value);
        }

        if (parts.Length == rest + 2
            && int.TryParse(parts[rest], NumberStyles.None, CultureInfo.InvariantCulture, out var season)
            && int.TryParse(parts[rest + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var episode))
        {
            return new StremioId(source, value, season, episode);
        }

        return null;
    }

    /// <summary>The Stremio id of one episode of the title <paramref name="titleId"/>.</summary>
    public static string ForEpisode(string titleId, int season, int episode) =>
        string.Create(CultureInfo.InvariantCulture, $"{titleId}:{season}:{episode}");

    /// <summary>The id Ponte mints for a Jellyfin item with no IMDb id.</summary>
    public static string ForItem(Guid itemId) =>
        PontePrefix + itemId.ToString("N", CultureInfo.InvariantCulture);
}
