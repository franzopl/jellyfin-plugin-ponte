using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Ponte.Stremio;

/// <summary>What Ponte knows about one file, independent of Jellyfin's types.</summary>
public sealed record MediaFacts
{
    public required string FileName { get; init; }

    public string? Container { get; init; }

    public long? Size { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? VideoCodec { get; init; }

    /// <summary>Jellyfin's video range type: SDR, HDR10, HDR10Plus, DOVI, DOVIWithHDR10, HLG...</summary>
    public string? VideoRangeType { get; init; }

    public int? BitDepth { get; init; }

    public IReadOnlyList<AudioFacts> Audio { get; init; } = [];

    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];

    /// <summary>The media source's own name, when the item has several versions.</summary>
    public string? VersionName { get; init; }
}

/// <summary>One audio track.</summary>
public sealed record AudioFacts(string? Codec, int? Channels, string? Language, string? Profile);

/// <summary>Turns <see cref="MediaFacts"/> into the text of a Stremio stream.</summary>
/// <remarks>
/// AIOStreams and most clients parse the filename and description for resolution, HDR, codec,
/// audio and languages, so the description uses the usual release-name vocabulary.
/// </remarks>
public static class StreamFormatter
{
    private static readonly Dictionary<string, string> CommonLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pt"] = "Portuguese", ["por"] = "Portuguese",
        ["en"] = "English", ["eng"] = "English",
        ["es"] = "Spanish", ["spa"] = "Spanish",
        ["fr"] = "French", ["fra"] = "French", ["fre"] = "French",
        ["de"] = "German", ["deu"] = "German", ["ger"] = "German",
        ["it"] = "Italian", ["ita"] = "Italian",
        ["ja"] = "Japanese", ["jpn"] = "Japanese",
        ["ko"] = "Korean", ["kor"] = "Korean",
        ["zh"] = "Chinese", ["zho"] = "Chinese", ["chi"] = "Chinese",
        ["ru"] = "Russian", ["rus"] = "Russian",
        ["nl"] = "Dutch", ["nld"] = "Dutch", ["dut"] = "Dutch",
        ["pl"] = "Polish", ["pol"] = "Polish",
        ["tr"] = "Turkish", ["tur"] = "Turkish",
        ["ar"] = "Arabic", ["ara"] = "Arabic",
        ["hi"] = "Hindi", ["hin"] = "Hindi",
    };

    private static readonly Dictionary<string, string> TwoToThree = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pt"] = "por", ["en"] = "eng", ["es"] = "spa", ["fr"] = "fre", ["de"] = "ger", ["it"] = "ita",
        ["ja"] = "jpn", ["ko"] = "kor", ["zh"] = "chi", ["ru"] = "rus", ["nl"] = "dut", ["pl"] = "pol",
        ["tr"] = "tur", ["ar"] = "ara", ["hi"] = "hin",
    };

    /// <summary>The resolution label: 2160p, 1080p, 720p...</summary>
    public static string? Resolution(int? width, int? height)
    {
        if (width is null && height is null)
        {
            return null;
        }

        // Scope and widescreen encodes keep the width and crop the height, so check both.
        var w = width ?? 0;
        var h = height ?? 0;
        if (w >= 3200 || h >= 2000)
        {
            return "2160p";
        }

        if (w >= 2200 || h >= 1300)
        {
            return "1440p";
        }

        if (w >= 1700 || h >= 900)
        {
            return "1080p";
        }

        if (w >= 1100 || h >= 650)
        {
            return "720p";
        }

        if (h >= 520)
        {
            return "576p";
        }

        return "480p";
    }

    /// <summary>The visual tags of a video range type, in release-name form.</summary>
    public static IReadOnlyList<string> VisualTags(string? rangeType, int? bitDepth)
    {
        var tags = new List<string>();
        switch (rangeType?.ToUpperInvariant())
        {
            case "HDR10":
                tags.Add("HDR10");
                break;
            case "HDR10PLUS":
                tags.Add("HDR10+");
                break;
            case "HLG":
                tags.Add("HLG");
                break;
            case "DOVI":
                tags.Add("DV");
                break;
            case "DOVIWITHHDR10":
                tags.Add("DV");
                tags.Add("HDR10");
                break;
            case "DOVIWITHHDR10PLUS":
                tags.Add("DV");
                tags.Add("HDR10+");
                break;
            case "DOVIWITHHLG":
                tags.Add("DV");
                tags.Add("HLG");
                break;
            case "DOVIWITHSDR":
                tags.Add("DV");
                break;
        }

        if (bitDepth >= 10)
        {
            tags.Add("10bit");
        }

        return tags;
    }

    /// <summary>A video codec in release-name form.</summary>
    public static string? VideoCodecName(string? codec) => codec?.ToLowerInvariant() switch
    {
        null or "" => null,
        "hevc" or "h265" => "HEVC",
        "h264" or "avc" => "AVC",
        "av1" => "AV1",
        "vp9" => "VP9",
        "mpeg2video" => "MPEG2",
        var other => other.ToUpperInvariant(),
    };

    /// <summary>An audio codec in release-name form.</summary>
    public static string? AudioCodecName(string? codec, string? profile)
    {
        var p = profile ?? string.Empty;
        return codec?.ToLowerInvariant() switch
        {
            null or "" => null,
            "truehd" => p.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ? "TrueHD Atmos" : "TrueHD",
            "eac3" => p.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ? "DD+ Atmos" : "DD+",
            "ac3" => "DD",
            "dts" => p.Contains("MA", StringComparison.Ordinal) ? "DTS-HD MA"
                : p.Contains("X", StringComparison.Ordinal) ? "DTS:X"
                : "DTS",
            "aac" => "AAC",
            "flac" => "FLAC",
            "opus" => "Opus",
            "mp3" => "MP3",
            var other => other.ToUpperInvariant(),
        };
    }

    /// <summary>Channels as 2.0, 5.1, 7.1.</summary>
    public static string? ChannelLayout(int? channels) => channels switch
    {
        null or <= 0 => null,
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        8 => "7.1",
        var n => n.Value.ToString(CultureInfo.InvariantCulture) + "ch",
    };

    /// <summary>A size as 4.21 GB.</summary>
    public static string? HumanSize(long? bytes)
    {
        if (bytes is null or <= 0)
        {
            return null;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit >= 3 ? "0.00" : "0", CultureInfo.InvariantCulture) + " " + units[unit];
    }

    /// <summary>The stream's name line: the addon name and the resolution.</summary>
    public static string Name(string addonName, MediaFacts facts)
    {
        var resolution = Resolution(facts.Width, facts.Height);
        return resolution is null ? addonName : addonName + "\n" + resolution;
    }

    /// <summary>The stream's description: filename, then video, audio, subtitles and size lines.</summary>
    public static string Description(MediaFacts facts)
    {
        var sb = new StringBuilder();
        sb.Append(facts.FileName);

        if (!string.IsNullOrWhiteSpace(facts.VersionName)
            && !facts.FileName.Contains(facts.VersionName, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append('\n').Append(facts.VersionName);
        }

        var video = new List<string>();
        if (VideoCodecName(facts.VideoCodec) is { } vc)
        {
            video.Add(vc);
        }

        video.AddRange(VisualTags(facts.VideoRangeType, facts.BitDepth));
        if (video.Count > 0)
        {
            sb.Append("\n🎞️ ").AppendJoin(" · ", video);
        }

        var audio = facts.Audio
            .Select(a => string.Join(' ', new[] { AudioCodecName(a.Codec, a.Profile), ChannelLayout(a.Channels) }.Where(s => s is not null)))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (audio.Count > 0)
        {
            sb.Append("\n🔊 ").AppendJoin(" | ", audio);
        }

        var audioLanguages = LanguageNames(facts.Audio.Select(a => a.Language));
        if (audioLanguages.Count > 0)
        {
            sb.Append("\n🌎 ").AppendJoin(" | ", audioLanguages);
        }

        var subtitleLanguages = LanguageNames(facts.SubtitleLanguages);
        if (subtitleLanguages.Count > 0)
        {
            sb.Append("\n💬 ").AppendJoin(" | ", subtitleLanguages);
        }

        if (HumanSize(facts.Size) is { } size)
        {
            sb.Append("\n📦 ").Append(size);
        }

        return sb.ToString();
    }

    /// <summary>English language names for ISO 639 codes, deduplicated, unknown codes kept as-is.</summary>
    public static IReadOnlyList<string> LanguageNames(IEnumerable<string?> codes) =>
        codes
            .Where(c => !string.IsNullOrWhiteSpace(c) && !c.Equals("und", StringComparison.OrdinalIgnoreCase))
            .Select(c => LanguageName(c!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The English name of an ISO 639-1/639-2 code, Portuguese (Brazil) for pt-BR.</summary>
    public static string LanguageName(string code)
    {
        var c = code.Trim();
        if (c.Equals("pt-br", StringComparison.OrdinalIgnoreCase)
            || c.Equals("pob", StringComparison.OrdinalIgnoreCase)
            || c.Equals("pt_BR", StringComparison.OrdinalIgnoreCase))
        {
            return "Portuguese (Brazil)";
        }

        // Servers running with invariant globalization know no cultures; cover the common ones.
        if (CommonLanguages.TryGetValue(c, out var common))
        {
            return common;
        }

        try
        {
            var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .FirstOrDefault(ci =>
                    ci.TwoLetterISOLanguageName.Equals(c, StringComparison.OrdinalIgnoreCase)
                    || ci.ThreeLetterISOLanguageName.Equals(c, StringComparison.OrdinalIgnoreCase)
                    || ci.ThreeLetterWindowsLanguageName.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (culture is not null && !string.IsNullOrEmpty(culture.EnglishName))
            {
                return culture.EnglishName;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        // ISO 639-2/B codes that differ from the 639-2/T codes .NET knows.
        return c.ToLowerInvariant() switch
        {
            "fre" => "French",
            "ger" => "German",
            "chi" => "Chinese",
            "dut" => "Dutch",
            "gre" => "Greek",
            "cze" => "Czech",
            "rum" => "Romanian",
            "per" => "Persian",
            _ => c,
        };
    }

    /// <summary>A two-letter-or-three-letter code to the ISO 639-2 code Stremio subtitles use.</summary>
    public static string SubtitleLanguageCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return "und";
        }

        var c = code.Trim();
        if (c.Equals("pt-br", StringComparison.OrdinalIgnoreCase) || c.Equals("pt_BR", StringComparison.OrdinalIgnoreCase))
        {
            return "pob";
        }

        if (c.Length == 3)
        {
            return c.ToLowerInvariant();
        }

        if (TwoToThree.TryGetValue(c, out var three))
        {
            return three;
        }

        try
        {
            var culture = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
                .FirstOrDefault(ci => ci.TwoLetterISOLanguageName.Equals(c, StringComparison.OrdinalIgnoreCase));
            if (culture is not null)
            {
                return culture.ThreeLetterISOLanguageName;
            }
        }
        catch (CultureNotFoundException)
        {
        }

        return c.ToLowerInvariant();
    }
}
