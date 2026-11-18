using System.Globalization;

namespace Jellyfin.Plugin.Ponte.Stremio;

/// <summary>Builds the <c>streamData</c> block AIOStreams reads instead of parsing a stream's text.</summary>
/// <remarks>
/// AIOStreams only takes these fields from an addon added through its AIOStreams preset, which
/// then does not look at the filename at all, so the block carries everything: the tracks
/// Jellyfin probed and what <see cref="ReleaseName"/> reads from the name. Stremio and other
/// clients ignore it.
/// </remarks>
public static class AioStreamsData
{
    /// <summary>The stream's data block.</summary>
    public static StreamData Build(MediaFacts facts, string addonName) => new()
    {
        Addon = addonName,
        Filename = facts.FileName,
        Size = facts.Size,
        Duration = facts.RunTimeTicks is > 0 ? facts.RunTimeTicks / TimeSpan.TicksPerMillisecond : null,
        ParsedFile = ParsedFile(facts),
    };

    private static ParsedFile ParsedFile(MediaFacts facts)
    {
        var release = ReleaseName.Parse(facts.FileName);
        var audioLanguages = KnownLanguages(
            facts.Audio.Where(a => !StreamFormatter.IsCommentary(a)).Select(a => StreamFormatter.TrackLanguage(a.Language, a.Title)));
        var languages = audioLanguages.ToList();
        if (languages.Count == 2)
        {
            languages.Add("Dual Audio");
        }
        else if (languages.Count > 2)
        {
            languages.Add("Multi");
        }

        // Only the tracks inside the file: players match these rows to the file's tracks by
        // position. External files reach AIOStreams through the subtitles resource.
        var embedded = facts.Subtitles.Where(s => !s.IsExternal).OrderBy(s => s.Index).ToList();

        return new ParsedFile
        {
            Title = release.Title ?? facts.Title,
            Year = facts.Year?.ToString(CultureInfo.InvariantCulture),
            Seasons = facts.Season is { } season ? [season] : null,
            Episodes = facts.Episodes.Count > 0 ? facts.Episodes : null,
            Resolution = StreamFormatter.Resolution(facts.Width, facts.Height),
            Quality = release.Quality,
            Encode = StreamFormatter.EncodeName(facts.VideoCodec, facts.FileName),
            ReleaseGroup = release.Group,
            Network = release.Network,
            Editions = release.Editions.Count > 0 ? release.Editions : null,
            Repack = release.Repack ? true : null,
            Proper = release.Proper ? true : null,
            VisualTags = StreamFormatter.VisualTags(facts.VideoRangeType, facts.BitDepth)
                .Concat(release.VisualTags)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            AudioTags = facts.Audio
                .SelectMany(a => StreamFormatter.AudioTags(a.Codec, a.Profile))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            AudioChannels = facts.Audio
                .Select(a => StreamFormatter.ChannelLayout(a.Channels))
                .Where(c => c is "2.0" or "5.1" or "6.1" or "7.1")
                .Select(c => c!)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Languages = languages,
            Subtitles = KnownLanguages(facts.Subtitles.Select(s => StreamFormatter.TrackLanguage(s.Language, s.Title))),
            AudioTracks = facts.Audio.Select(a => AudioTrack(a, facts)).ToList(),
            SubtitleTracks = embedded.Select(s => SubtitleTrack(s, facts)).ToList(),
            VideoIndex = FileIndex(facts.VideoIndex, facts),
            Container = release.Extension,
            Extension = release.Extension,
        };
    }

    // Jellyfin numbers external subtitles first and shifts the file's own streams after them.
    private static int? FileIndex(int? index, MediaFacts facts) =>
        index - facts.Subtitles.Count(s => s.IsExternal && s.Index < index);

    private static MediaTrack AudioTrack(AudioFacts audio, MediaFacts facts)
    {
        var tags = StreamFormatter.AudioTags(audio.Codec, audio.Profile);
        return new MediaTrack
        {
            Index = FileIndex(audio.Index, facts),
            Lang = KnownLanguage(StreamFormatter.TrackLanguage(audio.Language, audio.Title)),
            Codec = audio.Codec?.ToLowerInvariant(),
            Title = NullIfEmpty(audio.Title),
            Tag = tags.Count > 0 ? tags[0] : null,
            Tags = tags.Count > 0 ? tags : null,
            Channels = StreamFormatter.ChannelLayout(audio.Channels) is "2.0" or "5.1" or "6.1" or "7.1"
                ? StreamFormatter.ChannelLayout(audio.Channels)
                : null,
            Default = audio.IsDefault ? true : null,
            Commentary = StreamFormatter.IsCommentary(audio) ? true : null,
        };
    }

    private static MediaTrack SubtitleTrack(SubtitleFacts subtitle, MediaFacts facts) => new()
    {
        Index = FileIndex(subtitle.Index, facts),
        Lang = KnownLanguage(StreamFormatter.TrackLanguage(subtitle.Language, subtitle.Title)),
        Codec = subtitle.Codec?.ToLowerInvariant(),
        Title = NullIfEmpty(subtitle.Title),
        Default = subtitle.IsDefault ? true : null,
        Forced = StreamFormatter.IsForced(subtitle) ? true : null,
        HearingImpaired = StreamFormatter.IsHearingImpaired(subtitle) ? true : null,
    };

    private static List<string> KnownLanguages(IEnumerable<string?> names) =>
        names.Select(KnownLanguage).Where(n => n is not null).Select(n => n!).Distinct(StringComparer.Ordinal).ToList();

    private static string? KnownLanguage(string? name) =>
        name is not null && StreamFormatter.AioStreamsLanguages.Contains(name) ? name : null;

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
