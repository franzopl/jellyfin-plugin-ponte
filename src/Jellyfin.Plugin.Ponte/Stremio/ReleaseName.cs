using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Ponte.Stremio;

/// <summary>What only a file's name can say: source, release group, network and edition.</summary>
/// <remarks>
/// Jellyfin knows the tracks of a file but not where the release came from. The vocabulary here
/// is AIOStreams': a stream that brings its own parsed data skips AIOStreams' filename parser, so
/// these values have to arrive ready to filter and sort on.
/// </remarks>
public sealed partial record ReleaseName
{
    private const string Start = @"(?<![^\s\[(_\-.,])";
    private const string End = @"(?=[\s\[()\]_.\-,]|$)";
    private const RegexOptions Ignore = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly (string Name, Regex Pattern)[] Qualities =
    [
        ("DVD REMUX", Token(@"(hd[ .\-_]?)?dvd.*remux|remux.*(hd[ .\-_]?)?dvd")),
        ("BluRay REMUX", Token(@"(bd|br|b|uhd)?remux")),
        ("WEBRip", Token(@"web[ .\-_]?rip")),
        ("HDRip", Token(@"hd[ .\-_]?rip|web[ .\-_]?dl[ .\-_]?rip")),
        ("WEB-DL", Token(@"web[ .\-_]?(dl)?")),
        ("BluRay", Token(@"(bd|blu[ .\-_]?ray)([ .\-_]?rip)?|br[ .\-_]?rip")),
        ("DVDRip", Token(@"dvd[ .\-_]?(rip|mux|r|full|5|9)?")),
        ("HDTV", Token(@"(hd|pd)tv([ .\-_]?rip)?|tv[ .\-_]?rip|dsr(ip)?|sat[ .\-_]?rip")),
        ("CAM", Token(@"(hd)?cam([ .\-_]?rip)?")),
        ("TS", Token(@"telesync|hd[ .\-_]?ts|pdvd|predvd(rip)?")),
        ("TC", Token(@"telecine|hd[ .\-_]?tc")),
        ("SCR", Token(@"((dvd|bd|web|hd)[ .\-_]?)?scr(eener)?")),
    ];

    private static readonly (string Name, Regex Pattern)[] EditionPatterns =
    [
        ("Director's Cut", Token(@"director'?s?[ .\-_]cut|dc")),
        ("Extended Edition", Token(@"extended([ .\-_](cut|edition))?")),
        ("Theatrical", Token(@"theatrical([ .\-_](cut|edition))?")),
        ("Remastered", Token(@"remaster(ed)?")),
        ("IMAX", Token("imax")),
    ];

    private static readonly (string Name, Regex Pattern)[] VisualPatterns =
    [
        ("IMAX", Token("imax")),
        ("3D", Token(@"(bd)?3[ .\-_]?d")),
        ("Upscaled", Token(@"(ai)?(uprez|upscal(ed?|ing))(ai)?")),
    ];

    /// <summary>Streaming services by the tag releases use for them.</summary>
    private static readonly Dictionary<string, string> Networks = new(StringComparer.Ordinal)
    {
        ["AMZN"] = "Prime Video",
        ["DSNP"] = "Disney+",
        ["NF"] = "Netflix",
        ["HMAX"] = "HBO Max",
        ["MAX"] = "HBO Max",
        ["CR"] = "Crunchyroll",
        ["ATVP"] = "Apple TV",
        ["iT"] = "iTunes",
        ["MA"] = "Movies Anywhere",
        ["PLAY"] = "Google TV",
        ["PMTP"] = "Paramount+",
        ["PCOK"] = "Peacock",
        ["HULU"] = "Hulu",
        ["STAN"] = "Stan",
        ["SKST"] = "SkyShowtime",
    };

    private static readonly HashSet<string> NotGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "DL", "WEB", "Rip", "DoVi", "DV", "HDR", "HDR10", "SDR", "x264", "x265", "h264", "h265", "AVC", "HEVC",
        "Audio", "Dual", "Multi", "Subs", "Sub", "Ray",
    };

    /// <summary>The title, or null when the name starts with release details.</summary>
    public string? Title { get; init; }

    public string? Quality { get; init; }

    public string? Group { get; init; }

    public string? Network { get; init; }

    public IReadOnlyList<string> Editions { get; init; } = [];

    public IReadOnlyList<string> VisualTags { get; init; } = [];

    public bool Repack { get; init; }

    public bool Proper { get; init; }

    /// <summary>The extension without its dot, lower case.</summary>
    public string? Extension { get; init; }

    /// <summary>Reads a file name.</summary>
    public static ReleaseName Parse(string fileName)
    {
        var extension = ExtensionPattern().Match(fileName);
        var stem = extension.Success ? fileName[..extension.Index] : fileName;

        var leadingGroup = LeadingGroupPattern().Match(stem);
        var body = leadingGroup.Success ? stem[leadingGroup.Length..] : stem;
        var marker = TitleEndPattern().Match(body);
        var details = marker.Success ? body[marker.Index..] : string.Empty;

        return new ReleaseName
        {
            Title = CleanTitle(marker.Success ? body[..marker.Index] : body, marker.Success),
            Quality = Qualities.FirstOrDefault(q => q.Pattern.IsMatch(details)).Name,
            Group = leadingGroup.Success ? leadingGroup.Groups[1].Value.Trim() : TrailingGroup(details),
            Network = FindNetwork(details),
            Editions = Matching(EditionPatterns, details),
            VisualTags = Matching(VisualPatterns, details),
            Repack = RepackPattern().IsMatch(details),
            Proper = ProperPattern().IsMatch(details),
            Extension = extension.Success ? extension.Groups[1].Value.ToLowerInvariant() : null,
        };
    }

    private static Regex Token(string pattern) => new(Start + "(" + pattern + ")" + End, Ignore);

    private static string[] Matching((string Name, Regex Pattern)[] patterns, string text) =>
        patterns.Where(p => p.Pattern.IsMatch(text)).Select(p => p.Name).ToArray();

    // A bare number is a title only ahead of release details ("1917.2019.1080p"); alone it is a
    // disc file such as 00000.m2ts.
    private static string? CleanTitle(string text, bool hasDetails)
    {
        var title = SeparatorsPattern().Replace(text, " ").Trim(' ', '-', '(', ')', '[', ']');
        return title.Length == 0 || (!hasDetails && title.All(char.IsDigit)) ? null : title;
    }

    private static string? FindNetwork(string details)
    {
        var match = DistinctNetworkPattern().Match(details);
        if (!match.Success)
        {
            match = ShortNetworkPattern().Match(details);
        }

        return match.Success ? Networks[match.Groups[1].Value] : null;
    }

    private static string? TrailingGroup(string details)
    {
        var group = TrailingGroupPattern().Match(details);
        if (!group.Success)
        {
            return null;
        }

        var name = group.Groups[1].Value;
        return name.Any(char.IsLetter) && !NotGroups.Contains(name) ? name : null;
    }

    [GeneratedRegex(@"\.([A-Za-z0-9]{2,4})$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"^\[([^\]]+)\]\s*")]
    private static partial Regex LeadingGroupPattern();

    // Where the title stops: a year, an episode number, a resolution or a source.
    [GeneratedRegex(
        @"(?<=.)[ ._\-(\[]+(?:(?:19|20)\d{2}(?=[ ._)\]\-]|$)|S\d{1,3}(?:[ ._\-]?EP?\d{1,4})?(?=[ ._\-]|$)|\d{1,2}x\d{2,3}(?=[ ._\-]|$)|\d{3,4}[pi](?=[ ._\-\])]|$)|4K(?=[ ._\-\])]|$)|(?:UHD|BD|BR|WEB|HD|DVD|PD)[ ._\-]?(?:Rip|DL|TV|Remux)?(?=[ ._\-\])]|$)|Blu[ ._\-]?Ray|REMUX|-[ ]\d{1,4}(?=[ \[(v]|$))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleEndPattern();

    [GeneratedRegex(@"[ ._\-](AMZN|DSNP|HMAX|ATVP|PMTP|PCOK|HULU|STAN|SKST)(?=[ ._\-]|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DistinctNetworkPattern();

    // These tags are also ordinary words, so they only count next to a WEB source.
    [GeneratedRegex(@"[ ._\-](NF|MAX|CR|iT|MA|PLAY)[ ._\-]+(?:\d{3,4}p[ ._\-]+)?WEB", RegexOptions.CultureInvariant)]
    private static partial Regex ShortNetworkPattern();

    // The last "-NAME", which some releases follow with a short uploader tag or a copy number.
    [GeneratedRegex(@"-([A-Za-z0-9]{2,})(?:\(\d+\)|[. ][A-Za-z0-9()]{1,12})?$")]
    private static partial Regex TrailingGroupPattern();

    [GeneratedRegex(@"[._\s]+")]
    private static partial Regex SeparatorsPattern();

    [GeneratedRegex(@"(?<![A-Za-z])repack\d?(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RepackPattern();

    [GeneratedRegex(@"(?<![A-Za-z])proper(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProperPattern();
}
