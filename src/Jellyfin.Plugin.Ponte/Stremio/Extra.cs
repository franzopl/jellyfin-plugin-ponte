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
