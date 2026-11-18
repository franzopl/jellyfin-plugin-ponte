using Jellyfin.Plugin.Ponte.Stremio;
using Xunit;

namespace Jellyfin.Plugin.Ponte.Tests;

public class StreamFormatterTests
{
    [Theory]
    [InlineData(3840, 2160, "2160p")]
    [InlineData(3840, 1600, "2160p")] // scope 4K keeps the width
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1920, 800, "1080p")] // scope 1080p
    [InlineData(1280, 720, "720p")]
    [InlineData(720, 576, "576p")]
    [InlineData(640, 480, "480p")]
    public void Resolution_from_dimensions(int width, int height, string expected)
    {
        Assert.Equal(expected, StreamFormatter.Resolution(width, height));
    }

    [Fact]
    public void Unknown_dimensions_have_no_resolution()
    {
        Assert.Null(StreamFormatter.Resolution(null, null));
    }

    [Theory]
    [InlineData("DOVIWithHDR10", 10, "DV,HDR10,10bit")]
    [InlineData("DOVI", 10, "DV,10bit")]
    [InlineData("HDR10Plus", 10, "HDR10+,10bit")]
    [InlineData("HDR10", 10, "HDR10,10bit")]
    [InlineData("HLG", 10, "HLG,10bit")]
    [InlineData("DOVIWithEL", 10, "DV,10bit")]
    [InlineData("DOVIWithELHDR10Plus", 10, "DV,HDR10+,10bit")]
    [InlineData("SDR", 8, "")]
    [InlineData(null, null, "")]
    public void Visual_tags_use_release_names(string? range, int? bitDepth, string expected)
    {
        Assert.Equal(expected, string.Join(',', StreamFormatter.VisualTags(range, bitDepth)));
    }

    [Theory]
    [InlineData("truehd", "TrueHD + Atmos", "TrueHD Atmos")]
    [InlineData("eac3", "Dolby Digital Plus + Dolby Atmos", "DD+ Atmos")]
    [InlineData("eac3", null, "DD+")]
    [InlineData("ac3", null, "DD")]
    [InlineData("dts", "DTS-HD MA", "DTS-HD MA")]
    [InlineData("dts", "DTS:X", "DTS:X")]
    [InlineData("dts", "DTS-HD MA + DTS:X", "DTS:X")]
    [InlineData("dts", "DTS-HD HRA", "DTS-HD")]
    [InlineData("dts", null, "DTS")]
    [InlineData("aac", "LC", "AAC")]
    public void Audio_codecs_use_release_names(string codec, string? profile, string expected)
    {
        Assert.Equal(expected, StreamFormatter.AudioCodecName(codec, profile));
    }

    [Theory]
    [InlineData(2, "2.0")]
    [InlineData(6, "5.1")]
    [InlineData(7, "6.1")]
    [InlineData(8, "7.1")]
    [InlineData(4, "4ch")]
    public void Channel_layouts(int channels, string expected)
    {
        Assert.Equal(expected, StreamFormatter.ChannelLayout(channels));
    }

    [Theory]
    [InlineData(1536L, "2 KB")]
    [InlineData(10_803_048L, "10 MB")]
    [InlineData(4_521_000_000L, "4.21 GB")]
    public void Human_sizes(long bytes, string expected)
    {
        Assert.Equal(expected, StreamFormatter.HumanSize(bytes));
    }

    [Theory]
    [InlineData("por", "Portuguese")]
    [InlineData("pt", "Portuguese")]
    [InlineData("pt-BR", "Portuguese (Brazil)")]
    [InlineData("eng", "English")]
    [InlineData("ger", "German")]
    [InlineData("dan", "Danish")]
    [InlineData("rum", "Romanian")]
    [InlineData("nob", "Norwegian")]
    [InlineData("xyz", "xyz")]
    public void Language_names(string code, string expected)
    {
        Assert.Equal(expected, StreamFormatter.LanguageName(code));
    }

    [Theory]
    [InlineData("por", "Brazilian", "Portuguese (Brazil)")]
    [InlineData("por", "Portuguese (Brazil) [Forced]", "Portuguese (Brazil)")]
    [InlineData("por", "European", "Portuguese")]
    [InlineData("pob", null, "Portuguese (Brazil)")]
    [InlineData("pt-br", null, "Portuguese (Brazil)")]
    [InlineData("spa", "Latin American", "Latino")]
    [InlineData("es-419", null, "Latino")]
    [InlineData("spa", "European", "Spanish")]
    [InlineData("und", "Brazilian", null)]
    [InlineData(null, null, null)]
    public void Track_languages_use_the_title_for_variants(string? code, string? title, string? expected)
    {
        Assert.Equal(expected, StreamFormatter.TrackLanguage(code, title));
    }

    [Theory]
    [InlineData("pt-BR", "pob")]
    [InlineData("por", "por")]
    [InlineData("pt", "por")]
    [InlineData("en", "eng")]
    [InlineData(null, "und")]
    public void Subtitle_language_codes(string? code, string expected)
    {
        Assert.Equal(expected, StreamFormatter.SubtitleLanguageCode(code));
    }

    [Fact]
    public void Description_lists_video_audio_languages_and_size()
    {
        var facts = new MediaFacts
        {
            FileName = "Movie.2023.2160p.UHD.BluRay.REMUX.mkv",
            Size = 60L * 1024 * 1024 * 1024,
            Width = 3840,
            Height = 2160,
            VideoCodec = "hevc",
            VideoRangeType = "DOVIWithHDR10",
            BitDepth = 10,
            Audio =
            [
                new AudioFacts("truehd", 8, "eng", "TrueHD + Atmos"),
                new AudioFacts("ac3", 6, "por", null),
            ],
            Subtitles =
            [
                new SubtitleFacts("por", "Brazilian"),
                new SubtitleFacts("por", "Brazilian (Forced)", IsForced: true),
                new SubtitleFacts("por", "European"),
                new SubtitleFacts("eng", "English (SDH)", IsHearingImpaired: true),
                new SubtitleFacts("eng"),
                new SubtitleFacts("por", "Brazilian"),
                new SubtitleFacts(null),
            ],
        };

        Assert.Equal("Ponte\n2160p", StreamFormatter.Name("Ponte", facts));
        Assert.Equal(
            "Movie.2023.2160p.UHD.BluRay.REMUX.mkv\n"
            + "🎞️ HEVC · DV · HDR10 · 10bit\n"
            + "🔊 TrueHD Atmos 7.1 | DD 5.1\n"
            + "🌎 English | Portuguese\n"
            + "💬 Portuguese (Brazil) | Portuguese (Brazil) Forced | Portuguese | English SDH | English | Unknown\n"
            + "📦 60.00 GB",
            StreamFormatter.Description(facts));
    }

    [Fact]
    public void Version_name_is_added_when_the_filename_lacks_it()
    {
        var facts = new MediaFacts { FileName = "movie.mkv", VersionName = "Director's Cut" };

        Assert.Equal("movie.mkv\nDirector's Cut", StreamFormatter.Description(facts));
    }
}
