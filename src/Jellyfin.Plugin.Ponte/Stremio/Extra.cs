using System.Globalization;

namespace Jellyfin.Plugin.Ponte.Stremio;

/// <summary>
/// The "extra" path segment of a Stremio catalog request: <c>skip=100&amp;search=matrix&amp;genre=Drama</c>.
/// </summary>
public sealed record Extra(int Skip, string? Search, string? Genre)
{
    /// <summary>No extra arguments.</summary>
    public static Extra None { get; } = new(0, null, null);

    /// <summary>Parses an extra segment; unknown keys are ignored.</summary>
    public static Extra Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return None;
        }

        var skip = 0;
        string? search = null;
        string? genre = null;

        foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var key = pair[..eq];
            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')).Trim();
            switch (key)
            {
                case "skip":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                    {
                        skip = n;
                    }

                    break;
                case "search":
                    search = value.Length > 0 ? value : null;
                    break;
                case "genre":
                    genre = value.Length > 0 ? value : null;
                    break;
            }
        }

        return new Extra(skip, search, genre);
    }
}

/// <summary>
/// The file a client is playing, from the "extra" segment of a subtitles request:
/// <c>filename=Movie.mkv&amp;videoSize=1234</c>.
/// </summary>
public sealed record PlayingFile(string? FileName, long? Size)
{
    /// <summary>Parses a segment still in its URL-encoded form; unknown keys are ignored.</summary>
    public static PlayingFile Parse(string? raw)
    {
        string? fileName = null;
        long? size = null;

        foreach (var pair in (raw ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var value = Uri.UnescapeDataString(pair[(eq + 1)..]).Trim();
            switch (pair[..eq])
            {
                case "filename":
                    fileName = value.Length > 0 ? value : null;
                    break;
                case "videoSize":
                    size = long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
                    break;
            }
        }

        return new PlayingFile(fileName, size);
    }

    /// <summary>
    /// The candidates that are this file: by name, then by size among those. When nothing
    /// matches, all of them, since the client may have renamed the file or be playing another
    /// addon's copy.
    /// </summary>
    public IReadOnlyList<T> Select<T>(IReadOnlyList<T> candidates, Func<T, string?> nameOf, Func<T, long?> sizeOf)
    {
        var selected = candidates;
        if (FileName is not null)
        {
            selected = Narrow(selected, c => string.Equals(nameOf(c), FileName, StringComparison.OrdinalIgnoreCase));
        }

        if (Size is not null)
        {
            selected = Narrow(selected, c => sizeOf(c) == Size);
        }

        return selected;
    }

    private static IReadOnlyList<T> Narrow<T>(IReadOnlyList<T> candidates, Func<T, bool> matches)
    {
        var matching = candidates.Where(matches).ToList();
        return matching.Count > 0 ? matching : candidates;
    }
}
