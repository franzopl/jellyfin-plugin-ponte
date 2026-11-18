using Jellyfin.Plugin.Ponte.Stremio;
using Xunit;

namespace Jellyfin.Plugin.Ponte.Tests;

public class ReleaseNameTests
{
    [Theory]
    [InlineData("The.Windsors.S02E04.1080p.NF.WEB-DL.AAC2.0.H.264-UPD.mkv", "The Windsors", "WEB-DL", "UPD", "Netflix")]
    [InlineData("Hachiko.1987.1080p.AMZN.WEB-DL.DDP2.0.H.264.DUAL-BiOMA.mkv", "Hachiko", "WEB-DL", "BiOMA", "Prime Video")]
    [InlineData("Earth.to.Luna.S07E22.A.Jornada.do.Som.1080p.MAX.WEB-DL.DDP2.0.H.264-NeX.mkv", "Earth to Luna", "WEB-DL", "NeX", "HBO Max")]
    [InlineData("Movie.2023.2160p.UHD.BluRay.REMUX.HEVC.TrueHD.7.1-GRP.mkv", "Movie", "BluRay REMUX", "GRP", null)]
    [InlineData("TED.2012.1080p.BluRay.x264-BestHD.DUAL-Rick.SD.mkv", "TED", "BluRay", "Rick", null)]
    [InlineData("1917.2019.1080p.AMZN.WEB-DL.DDP5.1.H.264.DUAL-cza.mkv", "1917", "WEB-DL", "cza", "Prime Video")]
    [InlineData("Filme.2011.1080p.iT.WEBRip.AAC2.0.x264-100REAL(1).mkv", "Filme", "WEBRip", "100REAL", "iTunes")]
    [InlineData("Filme.1980.HDTVRip.x264-gooz.mkv", "Filme", "HDTV", "gooz", null)]
    [InlineData("Big Buck Bunny (2008) - 1080p.mkv", "Big Buck Bunny", null, null, null)]
    [InlineData("Andy Milonakis Show - S01EP01.mkv", "Andy Milonakis Show", null, null, null)]
    [InlineData("[SubsPlease] Some Anime - 05 [1080p].mkv", "Some Anime", null, "SubsPlease", null)]
    public void Reads_release_names(string file, string title, string? quality, string? group, string? network)
    {
        var release = ReleaseName.Parse(file);

        Assert.Equal(title, release.Title);
        Assert.Equal(quality, release.Quality);
        Assert.Equal(group, release.Group);
        Assert.Equal(network, release.Network);
    }

    [Fact]
    public void A_numbered_disc_file_has_no_title()
    {
        Assert.Null(ReleaseName.Parse("00000.m2ts").Title);
    }

    [Fact]
    public void Words_of_the_title_are_not_release_details()
    {
        var release = ReleaseName.Parse("The.Extended.Family.Remastered.Dreams.2020.1080p.WEB-DL.x264-GRP.mkv");

        Assert.Equal("The Extended Family Remastered Dreams", release.Title);
        Assert.Empty(release.Editions);
    }

    [Fact]
    public void Reads_editions_and_flags()
    {
        var release = ReleaseName.Parse("Amadeus.1984.DC.IMAX.REPACK.PROPER.1080p.BluRay.x264-CLASSiC.mkv");

        Assert.Contains("Director's Cut", release.Editions);
        Assert.Contains("IMAX", release.VisualTags);
        Assert.True(release.Repack);
        Assert.True(release.Proper);
        Assert.Equal("mkv", release.Extension);
    }
}
