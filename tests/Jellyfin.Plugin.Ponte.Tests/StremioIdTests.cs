using Jellyfin.Plugin.Ponte.Stremio;
using Xunit;

namespace Jellyfin.Plugin.Ponte.Tests;

public class StremioIdTests
{
    [Theory]
    [InlineData("tt0133093", IdSource.Imdb, "tt0133093", null, null)]
    [InlineData("TT0133093", IdSource.Imdb, "tt0133093", null, null)]
    [InlineData("tt0944947:1:2", IdSource.Imdb, "tt0944947", 1, 2)]
    [InlineData("tmdb:603", IdSource.Tmdb, "603", null, null)]
    [InlineData("tmdb:1399:3:10", IdSource.Tmdb, "1399", 3, 10)]
    [InlineData("ponte:31538703354fda8970fcd1367377b9fc", IdSource.Jellyfin, "31538703354fda8970fcd1367377b9fc", null, null)]
    [InlineData("ponte:31538703-354f-da89-70fc-d1367377b9fc:2:5", IdSource.Jellyfin, "31538703354fda8970fcd1367377b9fc", 2, 5)]
    [InlineData("tt0944947%3A1%3A2", IdSource.Imdb, "tt0944947", 1, 2)]
    public void Parses_supported_ids(string raw, IdSource source, string value, int? season, int? episode)
    {
        var id = StremioId.Parse(raw);

        Assert.NotNull(id);
        Assert.Equal(source, id.Source);
        Assert.Equal(value, id.Value);
        Assert.Equal(season, id.Season);
        Assert.Equal(episode, id.Episode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kitsu:1:1")]
    [InlineData("mal:20")]
    [InlineData("tt")]
    [InlineData("tmdb:")]
    [InlineData("ponte:not-a-guid")]
    [InlineData("tt0944947:1")]
    [InlineData("tt0944947:a:b")]
    [InlineData("tt0944947:1:2:3")]
    public void Rejects_ids_it_cannot_look_up(string? raw)
    {
        Assert.Null(StremioId.Parse(raw));
    }

    [Fact]
    public void Title_and_episode_ids_round_trip()
    {
        var item = Guid.Parse("31538703-354f-da89-70fc-d1367377b9fc");
        var title = StremioId.ForItem(item);
        var episode = StremioId.ForEpisode(title, 1, 4);

        Assert.Equal("ponte:31538703354fda8970fcd1367377b9fc", title);
        Assert.Equal(title + ":1:4", episode);
        Assert.Equal(title, StremioId.Parse(episode)!.TitleId);
        Assert.True(StremioId.Parse(episode)!.IsEpisode);
        Assert.Equal("tmdb:1399", StremioId.Parse("tmdb:1399:1:1")!.TitleId);
    }
}
