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

    public IReadOnlyList<SubtitleFacts> Subtitles { get; init; } = [];

    /// <summary>The media source's own name, when the item has several versions.</summary>
    public string? VersionName { get; init; }

    /// <summary>The movie's or series' name in the library.</summary>
    public string? Title { get; init; }

    /// <summary>A movie's year; episodes have none, as in release names.</summary>
    public int? Year { get; init; }

    public int? Season { get; init; }

    public IReadOnlyList<int> Episodes { get; init; } = [];

    public long? RunTimeTicks { get; init; }

    public int? VideoIndex { get; init; }
}

/// <summary>One audio track.</summary>
public sealed record AudioFacts(
    string? Codec,
    int? Channels,
    string? Language,
    string? Profile,
    string? Title = null,
    int? Index = null,
    bool IsDefault = false);

/// <summary>One subtitle track, inside the file or next to it.</summary>
public sealed record SubtitleFacts(
    string? Language,
    string? Title = null,
    string? Codec = null,
    int? Index = null,
    bool IsForced = false,
    bool IsHearingImpaired = false,
    bool IsDefault = false,
    bool IsExternal = false);

/// <summary>Turns <see cref="MediaFacts"/> into the text of a Stremio stream.</summary>
/// <remarks>
/// AIOStreams and most clients parse the filename and description for resolution, HDR, codec,
/// audio and languages, so the description uses the usual release-name vocabulary.
/// </remarks>
public static class StreamFormatter
{
    // Servers running with invariant globalization know no cultures, so the usual languages are
    // listed here, with their ISO 639-1, 639-2/T and 639-2/B codes.
    private static readonly Dictionary<string, string> CommonLanguages = BuildLanguages(
        ("Albanian", "sq sqi alb"), ("Arabic", "ar ara"), ("Azerbaijani", "az aze"), ("Basque", "eu eus baq"),
        ("Bengali", "bn ben"), ("Bosnian", "bs bos"), ("Bulgarian", "bg bul"), ("Catalan", "ca cat"),
        ("Chinese", "zh zho chi"), ("Croatian", "hr hrv"), ("Czech", "cs ces cze"), ("Danish", "da dan"),
        ("Dutch", "nl nld dut"), ("English", "en eng"), ("Estonian", "et est"), ("Filipino", "fil tl tgl"),
        ("Finnish", "fi fin"), ("French", "fr fra fre"), ("Galician", "gl glg"), ("Georgian", "ka kat geo"),
        ("German", "de deu ger"), ("Greek", "el ell gre"), ("Gujarati", "gu guj"), ("Hebrew", "he heb"),
        ("Hindi", "hi hin"), ("Hungarian", "hu hun"), ("Icelandic", "is isl ice"), ("Indonesian", "id ind"),
        ("Irish", "ga gle"), ("Italian", "it ita"), ("Japanese", "ja jpn"), ("Kannada", "kn kan"),
        ("Kazakh", "kk kaz"), ("Khmer", "km khm"), ("Korean", "ko kor"), ("Latvian", "lv lav"),
        ("Lithuanian", "lt lit"), ("Macedonian", "mk mkd mac"), ("Malay", "ms msa may"), ("Malayalam", "ml mal"),
        ("Marathi", "mr mar"), ("Norwegian", "no nb nn nor nob nno"), ("Persian", "fa fas per"), ("Polish", "pl pol"),
        ("Portuguese", "pt por"), ("Punjabi", "pa pan"), ("Romanian", "ro ron rum"), ("Russian", "ru rus"),
        ("Serbian", "sr srp"), ("Sinhala", "si sin"), ("Slovak", "sk slk slo"), ("Slovenian", "sl slv"),
        ("Spanish", "es spa"), ("Swahili", "sw swa"), ("Swedish", "sv swe"), ("Tamil", "ta tam"),
        ("Telugu", "te tel"), ("Thai", "th tha"), ("Turkish", "tr tur"), ("Ukrainian", "uk ukr"),
        ("Vietnamese", "vi vie"), ("Welsh", "cy cym wel"), ("Zulu", "zu zul"));

    /// <summary>How a subtitle file next to the media is named in players.</summary>
    public const string ExternalSubtitleLabel = "External · Ponte";

    /// <summary>The language names AIOStreams filters and sorts on.</summary>
    public static readonly IReadOnlySet<string> AioStreamsLanguages = new HashSet<string>(StringComparer.Ordinal)
    {
        "English", "Japanese", "Chinese", "Russian", "Arabic", "Portuguese", "Portuguese (Brazil)", "Spanish",
        "French", "German", "Italian", "Korean", "Hindi", "Bengali", "Punjabi", "Marathi", "Gujarati", "Tamil",
        "Telugu", "Kannada", "Malayalam", "Thai", "Vietnamese", "Indonesian", "Turkish", "Hebrew", "Persian",
        "Ukrainian", "Greek", "Lithuanian", "Latvian", "Estonian", "Polish", "Czech", "Slovak", "Hungarian",
        "Romanian", "Bulgarian", "Serbian", "Croatian", "Slovenian", "Dutch", "Danish", "Finnish", "Swedish",
        "Norwegian", "Malay", "Latino",
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
            case "DOVIWITHEL":
                tags.Add("DV");
                break;
            case "DOVIWITHELHDR10PLUS":
                tags.Add("DV");
                tags.Add("HDR10+");
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
            "dts" => p.Contains("DTS:X", StringComparison.OrdinalIgnoreCase) ? "DTS:X"
                : p.Contains("MA", StringComparison.Ordinal) ? "DTS-HD MA"
                : p.Contains("DTS-HD", StringComparison.OrdinalIgnoreCase) ? "DTS-HD"
                : "DTS",
            "aac" => "AAC",
            "flac" => "FLAC",
            "opus" => "Opus",
            "mp3" => "MP3",
            var other => other.ToUpperInvariant(),
        };
    }

    /// <summary>A video codec in AIOStreams' vocabulary, or null when it has no word for it.</summary>
    public static string? EncodeName(string? codec, string fileName) => codec?.ToLowerInvariant() switch
    {
        "hevc" or "h265" => "HEVC",
        "h264" or "avc" => "AVC",
        "av1" => "AV1",
        "vc1" => "VC-1",
        "mpeg4" when fileName.Contains("xvid", StringComparison.OrdinalIgnoreCase) => "XviD",
        "mpeg4" when fileName.Contains("divx", StringComparison.OrdinalIgnoreCase) => "DivX",
        "mpeg4" => "MPEG-4",
        _ => null,
    };

    /// <summary>An audio track's tags in AIOStreams' vocabulary: the format, then Atmos or DTS:X.</summary>
    public static IReadOnlyList<string> AudioTags(string? codec, string? profile)
    {
        var p = profile ?? string.Empty;
        var c = codec?.ToLowerInvariant() ?? string.Empty;
        string? format = c switch
        {
            "eac3" => "DD+",
            "ac3" => "DD",
            "truehd" => "TrueHD",
            "dts" => p.Contains("DTS-HD MA", StringComparison.OrdinalIgnoreCase) ? "DTS-HD MA"
                : p.Contains("DTS-HD", StringComparison.OrdinalIgnoreCase) ? "DTS-HD"
                : p.Contains("DTS-ES", StringComparison.OrdinalIgnoreCase) ? "DTS-ES"
                : "DTS",
            "opus" => "OPUS",
            "flac" => "FLAC",
            "aac" => "AAC",
            _ when c.StartsWith("pcm_", StringComparison.Ordinal) => "PCM",
            _ => null,
        };

        var tags = new List<string>();
        if (format is not null)
        {
            tags.Add(format);
        }

        if (p.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("Atmos");
        }

        if (p.Contains("DTS:X", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("DTS:X");
        }

        return tags;
    }

    /// <summary>Channels as 2.0, 5.1, 7.1.</summary>
    public static string? ChannelLayout(int? channels) => channels switch
    {
        null or <= 0 => null,
        1 => "1.0",
        2 => "2.0",
        6 => "5.1",
        7 => "6.1",
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

        var audioLanguages = facts.Audio
            .Where(a => !IsCommentary(a))
            .Select(a => TrackLanguage(a.Language, a.Title))
            .Where(l => l is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (audioLanguages.Count > 0)
        {
            sb.Append("\n🌎 ").AppendJoin(" | ", audioLanguages);
        }

        var subtitles = facts.Subtitles.Select(SubtitleLabel).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (subtitles.Count > 0)
        {
            sb.Append("\n💬 ").AppendJoin(" | ", subtitles);
        }

        if (HumanSize(facts.Size) is { } size)
        {
            sb.Append("\n📦 ").Append(size);
        }

        return sb.ToString();
    }

    /// <summary>
    /// A track's language, using its title to tell regional variants apart, because files usually
    /// tag Brazilian and European Portuguese alike as <c>por</c>. Null for an untagged track.
    /// </summary>
    public static string? TrackLanguage(string? code, string? title)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var c = code.Trim().Replace('_', '-').ToLowerInvariant();
        if (c is "und" or "unk" or "mis" or "mul" or "zxx" or "qaa")
        {
            return null;
        }

        var name = LanguageName(c);
        var t = title ?? string.Empty;
        if (name == "Portuguese"
            && (t.Contains("brazil", StringComparison.OrdinalIgnoreCase) || t.Contains("brasil", StringComparison.OrdinalIgnoreCase)))
        {
            return "Portuguese (Brazil)";
        }

        if (c is "es-419" or "es-mx" or "es-la"
            || (name == "Spanish"
                && (t.Contains("latin", StringComparison.OrdinalIgnoreCase) || t.Contains("latam", StringComparison.OrdinalIgnoreCase))))
        {
            return "Latino";
        }

        return name;
    }

    /// <summary>Whether an audio track is a commentary, which is not a dub of the film.</summary>
    public static bool IsCommentary(AudioFacts audio) =>
        audio.Title?.Contains("commentary", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Whether a subtitle only covers foreign dialogue.</summary>
    public static bool IsForced(SubtitleFacts subtitle) =>
        subtitle.IsForced || subtitle.Title?.Contains("forced", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Whether a subtitle is meant for deaf and hard-of-hearing viewers.</summary>
    public static bool IsHearingImpaired(SubtitleFacts subtitle)
    {
        var title = subtitle.Title ?? string.Empty;
        return subtitle.IsHearingImpaired
            || title.Contains("SDH", StringComparison.Ordinal)
            || title.Contains("hearing", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A subtitle as listed in a description: language, then Forced or SDH.</summary>
    public static string SubtitleLabel(SubtitleFacts subtitle)
    {
        var label = TrackLanguage(subtitle.Language, subtitle.Title) ?? "Unknown";
        return IsForced(subtitle) ? label + " Forced"
            : IsHearingImpaired(subtitle) ? label + " SDH"
            : label;
    }

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

    private static Dictionary<string, string> BuildLanguages(params (string Name, string Codes)[] languages)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, codes) in languages)
        {
            foreach (var code in codes.Split(' '))
            {
                result[code] = name;
            }
        }

        return result;
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
