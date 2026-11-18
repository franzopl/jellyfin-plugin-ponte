using System.Text.Json;
using Jellyfin.Plugin.Ponte.Stremio;
using Xunit;

namespace Jellyfin.Plugin.Ponte.Tests;

public class AioStreamsDataTests
{
    private static readonly MediaFacts Episode = new()
    {
        FileName = "The.Windsors.S02E04.1080p.NF.WEB-DL.AAC2.0.H.264-UPD.mkv",
        Title = "Os Windsors",
        Season = 2,
        Episodes = [4],
        Size = 1000,
        RunTimeTicks = TimeSpan.FromMinutes(30).Ticks,
        Width = 1920,
        Height = 1080,
        VideoCodec = "h264",
        VideoRangeType = "SDR",
        BitDepth = 8,
        VideoIndex = 1, // Jellyfin numbers the external subtitle first
        Audio =
        [
            new AudioFacts("eac3", 6, "eng", "Dolby Digital Plus + Dolby Atmos", "Main", 2, true),
            new AudioFacts("aac", 2, "por", "LC", "Brazilian", 3),
            new AudioFacts("aac", 2, "eng", "LC", "Director's Commentary", 4),
        ],
        Subtitles =
        [
            new SubtitleFacts("pt-br", null, "subrip", 0, IsExternal: true),
            new SubtitleFacts("por", "Brazilian (Forced)", "subrip", 6, IsForced: true),
            new SubtitleFacts("por", "European", "subrip", 5),
            new SubtitleFacts("eng", "English (SDH)", "PGSSUB", 7),
            new SubtitleFacts("tlh", "Klingon", "subrip", 8),
        ],
    };

    private static ParsedFile Parsed(MediaFacts facts) => AioStreamsData.Build(facts, "Ponte · Casa").ParsedFile;

    [Fact]
    public void Carries_the_stream_identity()
    {
        var data = AioStreamsData.Build(Episode, "Ponte · Casa");

        Assert.Equal("Ponte · Casa", data.Addon);
        Assert.Equal("http", data.Type);
        Assert.True(data.Library);
        Assert.Equal(Episode.FileName, data.Filename);
        Assert.Equal(1000, data.Size);
        Assert.Equal(1_800_000, data.Duration);
    }

    [Fact]
    public void Describes_the_release()
    {
        var file = Parsed(Episode);

        Assert.Equal("The Windsors", file.Title);
        Assert.Null(file.Year);
        Assert.Equal([2], file.Seasons);
        Assert.Equal([4], file.Episodes);
        Assert.Equal("1080p", file.Resolution);
        Assert.Equal("WEB-DL", file.Quality);
        Assert.Equal("AVC", file.Encode);
        Assert.Equal("UPD", file.ReleaseGroup);
        Assert.Equal("Netflix", file.Network);
        Assert.Equal("probe", file.MediaInfoQuality);
    }

    [Fact]
    public void Describes_audio_from_the_tracks()
    {
        var file = Parsed(Episode);

        Assert.Equal(["DD+", "Atmos", "AAC"], file.AudioTags);
        Assert.Equal(["5.1", "2.0"], file.AudioChannels);
        Assert.Equal(["English", "Portuguese (Brazil)", "Dual Audio"], file.Languages);
        Assert.Equal("Portuguese (Brazil)", file.AudioTracks[1].Lang);
        Assert.True(file.AudioTracks[0].Default);
        Assert.True(file.AudioTracks[2].Commentary);
    }

    [Fact]
    public void A_commentary_in_another_language_is_not_a_dub()
    {
        var facts = new MediaFacts
        {
            FileName = "Filme.2020.1080p.WEB-DL.mkv",
            Audio =
            [
                new AudioFacts("aac", 2, "por", "LC"),
                new AudioFacts("aac", 2, "eng", "LC", "Director's Commentary"),
            ],
        };

        Assert.Equal(["Portuguese"], Parsed(facts).Languages);
        Assert.Contains("🌎 Portuguese\n", StreamFormatter.Description(facts) + "\n");
    }

    [Fact]
    public void Tracks_are_only_those_inside_the_file_numbered_as_in_the_file()
    {
        var file = Parsed(Episode);

        Assert.Equal(["Portuguese (Brazil)", "Portuguese", "English"], file.Subtitles);
        Assert.Equal(0, file.VideoIndex);
        Assert.Equal([1, 2, 3], file.AudioTracks.Select(t => t.Index));
        Assert.Equal([4, 5, 6, 7], file.SubtitleTracks.Select(t => t.Index));
        Assert.True(file.SubtitleTracks[1].Forced);
        Assert.True(file.SubtitleTracks[2].HearingImpaired);
        Assert.Equal("pgssub", file.SubtitleTracks[2].Codec);
        Assert.Null(file.SubtitleTracks[3].Lang); // not a language AIOStreams knows
        Assert.Equal("Klingon", file.SubtitleTracks[3].Title);
    }

    [Fact]
    public void Falls_back_to_the_library_title_and_gives_movies_a_year()
    {
        var file = Parsed(new MediaFacts { FileName = "00000.m2ts", Title = "Filme", Year = 1999 });

        Assert.Equal("Filme", file.Title);
        Assert.Equal("1999", file.Year);
        Assert.Null(file.Seasons);
        Assert.Null(file.Episodes);
    }

    [Fact]
    public void Serializes_with_the_names_AIOStreams_expects()
    {
        var json = JsonSerializer.Serialize(new Jellyfin.Plugin.Ponte.Stremio.Stream
        {
            Url = "http://x",
            Name = "n",
            Description = "d",
            StreamData = AioStreamsData.Build(Episode, "Ponte"),
        }, StremioJson.Options);

        using var doc = JsonDocument.Parse(json);
        var parsed = doc.RootElement.GetProperty("streamData").GetProperty("parsedFile");
        Assert.Equal("probe", parsed.GetProperty("mediaInfoQuality").GetString());
        Assert.Equal("UPD", parsed.GetProperty("releaseGroup").GetString());
        Assert.Equal(4, parsed.GetProperty("subtitleTracks").GetArrayLength());
    }
}
