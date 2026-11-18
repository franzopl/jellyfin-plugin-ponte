using Jellyfin.Plugin.Ponte.Stremio;
using Xunit;

namespace Jellyfin.Plugin.Ponte.Tests;

public class ExtraTests
{
    [Fact]
    public void Empty_extra_is_none()
    {
        Assert.Equal(Extra.None, Extra.Parse(null));
        Assert.Equal(Extra.None, Extra.Parse(""));
    }

    [Fact]
    public void Parses_skip_search_and_genre()
    {
        var extra = Extra.Parse("genre=Science%20Fiction&skip=200&search=o+poderoso%20chef%C3%A3o");

        Assert.Equal(200, extra.Skip);
        Assert.Equal("o poderoso chefão", extra.Search);
        Assert.Equal("Science Fiction", extra.Genre);
    }

    [Theory]
    [InlineData("skip=-5")]
    [InlineData("skip=abc")]
    [InlineData("skip=")]
    [InlineData("foo=bar")]
    [InlineData("=x")]
    public void Ignores_bad_or_unknown_values(string raw)
    {
        var extra = Extra.Parse(raw);

        Assert.Equal(0, extra.Skip);
        Assert.Null(extra.Search);
        Assert.Null(extra.Genre);
    }

    [Fact]
    public void Blank_search_is_no_search()
    {
        Assert.Null(Extra.Parse("search=%20%20").Search);
    }

    private static readonly (string Name, long Size)[] Copies =
    [
        ("Movie.1080p.mkv", 100),
        ("Movie.2160p.mkv", 200),
        ("Copy/Movie.same.mkv", 300),
        ("Movie.same.mkv", 300),
    ];

    private static IEnumerable<string> Picked(string? extra) =>
        PlayingFile.Parse(extra).Select(Copies, c => c.Name, c => c.Size).Select(c => c.Name);

    [Theory]
    [InlineData("filename=Movie.2160p.mkv", "Movie.2160p.mkv")]
    [InlineData("filename=movie.2160P.MKV&videoSize=999", "Movie.2160p.mkv")]
    [InlineData("videoHash=abc&videoSize=100", "Movie.1080p.mkv")]
    [InlineData("filename=renamed.mkv&videoSize=200", "Movie.2160p.mkv")]
    public void Subtitles_follow_the_playing_file(string extra, string expected)
    {
        Assert.Equal([expected], Picked(extra));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("filename=renamed.mkv")]
    [InlineData("videoSize=999")]
    [InlineData("videoSize=abc")]
    public void An_unknown_file_keeps_every_copy(string? extra)
    {
        Assert.Equal(Copies.Length, Picked(extra).Count());
    }

    [Fact]
    public void File_names_keep_encoded_separators_and_plus_signs()
    {
        var file = PlayingFile.Parse("filename=A%26B%2B%20(1).mkv&videoSize=7");

        Assert.Equal("A&B+ (1).mkv", file.FileName);
        Assert.Equal(7, file.Size);
    }
}
