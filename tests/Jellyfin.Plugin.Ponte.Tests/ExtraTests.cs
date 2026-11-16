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
}
