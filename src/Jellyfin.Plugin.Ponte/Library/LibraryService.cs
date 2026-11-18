using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Ponte.Stremio;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using StremioStream = Jellyfin.Plugin.Ponte.Stremio.Stream;
using StremioVideo = Jellyfin.Plugin.Ponte.Stremio.Video;

namespace Jellyfin.Plugin.Ponte.Library;

/// <summary>
/// Reads the library on behalf of one user and answers in Stremio terms.
/// </summary>
/// <remarks>
/// Every query runs as the link's user, so library access and parental controls apply as they
/// do in Jellyfin's own apps. Only items backed by a local file are served: virtual items and
/// the placeholders and stream rows of plugins such as Gelato are left out, so an AIOStreams
/// that feeds Gelato never gets its own streams back.
/// </remarks>
public sealed class LibraryService
{
    /// <summary>Catalog id prefix of per-library catalogs.</summary>
    public const string LibraryCatalogPrefix = "ponte.lib.";

    /// <summary>Catalog id of "Recently added".</summary>
    public const string RecentCatalogId = "ponte.recent";

    /// <summary>Catalog id of "Continue watching".</summary>
    public const string ResumeCatalogId = "ponte.resume";

    private const string GelatoStreamTag = "gelato-stream";

    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryService"/> class.
    /// </summary>
    public LibraryService(ILibraryManager libraryManager, IMediaSourceManager mediaSourceManager)
    {
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
    }

    /// <summary>The movie and show libraries the user can see.</summary>
    public IReadOnlyList<(Folder Folder, string Type)> Libraries(Jellyfin.Database.Implementations.Entities.User user)
    {
        var result = new List<(Folder, string)>();
        foreach (var child in _libraryManager.GetUserRootFolder().GetChildren(user, true))
        {
            if (child is not ICollectionFolder collection || child is not Folder folder)
            {
                continue;
            }

            switch (collection.CollectionType)
            {
                case CollectionType.movies:
                    result.Add((folder, "movie"));
                    break;
                case CollectionType.tvshows:
                    result.Add((folder, "series"));
                    break;
            }
        }

        return result;
    }

    /// <summary>One page of a catalog.</summary>
    public IReadOnlyList<Meta> Catalog(
        Jellyfin.Database.Implementations.Entities.User user,
        string type,
        string catalogId,
        Extra extra,
        int pageSize,
        string baseUrl)
    {
        var kind = type == "movie" ? BaseItemKind.Movie : BaseItemKind.Series;

        if (catalogId == ResumeCatalogId)
        {
            return Resume(user, type, extra, pageSize, baseUrl);
        }

        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [kind],
            Recursive = true,
            IsVirtualItem = false,
            StartIndex = extra.Skip,
            Limit = pageSize,
            DtoOptions = new DtoOptions(true),
        };

        if (catalogId == RecentCatalogId)
        {
            query.OrderBy = [(ItemSortBy.DateCreated, SortOrder.Descending)];
        }
        else if (catalogId.StartsWith(LibraryCatalogPrefix, StringComparison.Ordinal)
                 && Guid.TryParse(catalogId[LibraryCatalogPrefix.Length..], out var libraryId))
        {
            if (!Libraries(user).Any(l => l.Folder.Id == libraryId))
            {
                return [];
            }

            query.ParentId = libraryId;
            query.OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)];
        }
        else
        {
            return [];
        }

        if (extra.Search is not null)
        {
            query.SearchTerm = extra.Search;
            query.OrderBy = [];
        }

        if (extra.Genre is not null)
        {
            query.Genres = [extra.Genre];
        }

        // Two items with the same IMDb id (copies Jellyfin did not group as versions) are one
        // title in Stremio; their files all come back as streams of it.
        return _libraryManager.GetItemList(query)
            .Where(IsLocal)
            .DistinctBy(StremioIdOf)
            .Select(item => ToMeta(item, type, baseUrl, withVideos: false))
            .ToList();
    }

    /// <summary>The full meta of a title, with episodes for a series.</summary>
    public Meta? GetMeta(Jellyfin.Database.Implementations.Entities.User user, string type, StremioId id, string baseUrl)
    {
        var item = FindTitle(user, type, id);
        return item is null ? null : ToMeta(item, type, baseUrl, withVideos: type == "series");
    }

    /// <summary>The files of a movie or episode, as streams.</summary>
    public IReadOnlyList<StremioStream> Streams(
        Jellyfin.Database.Implementations.Entities.User user,
        string type,
        StremioId id,
        string addonName,
        Func<BaseItem, MediaSourceInfo, string> playUrl)
    {
        var streams = new List<StremioStream>();
        foreach (var item in FindPlayable(user, type, id))
        {
            foreach (var source in LocalSources(item, user))
            {
                var facts = Facts(item, source);
                var resolution = StreamFormatter.Resolution(facts.Width, facts.Height) ?? "unknown";
                streams.Add(new StremioStream
                {
                    Url = playUrl(item, source),
                    Name = StreamFormatter.Name(addonName, facts),
                    Description = StreamFormatter.Description(facts),
                    BehaviorHints = new StreamHints
                    {
                        Filename = facts.FileName,
                        VideoSize = facts.Size,
                        BingeGroup = "ponte-" + resolution,
                        NotWebReady = !string.Equals(source.Container, "mp4", StringComparison.OrdinalIgnoreCase) ? true : null,
                    },
                    StreamData = AioStreamsData.Build(facts, addonName),
                });
            }
        }

        // Highest resolution first, then largest file.
        return streams
            .OrderByDescending(s => ResolutionRank(s.Name))
            .ThenByDescending(s => s.BehaviorHints?.VideoSize ?? 0)
            .ToList();
    }

    /// <summary>
    /// The external subtitle files of a movie or episode. When the client says which file it is
    /// playing, only that file's: a subtitle timed for another copy would be out of sync.
    /// </summary>
    public IReadOnlyList<(BaseItem Item, MediaSourceInfo Source, MediaStream Stream)> ExternalSubtitles(
        Jellyfin.Database.Implementations.Entities.User user,
        string type,
        StremioId id,
        PlayingFile playing)
    {
        var files = FindPlayable(user, type, id)
            .SelectMany(item => LocalSources(item, user).Select(source => (Item: item, Source: source)))
            .ToList();

        var result = new List<(BaseItem, MediaSourceInfo, MediaStream)>();
        foreach (var (item, source) in playing.Select(files, f => Path.GetFileName(f.Source.Path), f => f.Source.Size ?? TryFileSize(f.Source.Path)))
        {
            foreach (var stream in source.MediaStreams)
            {
                if (stream.Type == MediaStreamType.Subtitle
                    && stream.IsExternal
                    && !string.IsNullOrEmpty(stream.Path)
                    && File.Exists(stream.Path))
                {
                    result.Add((item, source, stream));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The local file of one media source of an item the user can see, or null.
    /// </summary>
    public MediaSourceInfo? FindSource(Jellyfin.Database.Implementations.Entities.User user, Guid itemId, string sourceId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null || !item.IsVisibleStandalone(user) || !IsLocal(item))
        {
            return null;
        }

        return LocalSources(item, user).FirstOrDefault(s => string.Equals(s.Id, sourceId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The Stremio id of an item: its IMDb id, or a Ponte id when it has none.</summary>
    public static string StremioIdOf(BaseItem item)
    {
        var imdb = item.GetProviderId(MetadataProvider.Imdb);
        return !string.IsNullOrEmpty(imdb) && imdb.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
            ? imdb.ToLowerInvariant()
            : StremioId.ForItem(item.Id);
    }

    /// <summary>Whether an item is backed by a local file or folder, not a placeholder.</summary>
    public static bool IsLocal(BaseItem item)
    {
        if (item.IsVirtualItem || string.IsNullOrEmpty(item.Path))
        {
            return false;
        }

        // Alternate versions are served as extra streams of their primary item, not as titles.
        if (item is MediaBrowser.Controller.Entities.Video { PrimaryVersionId: not null })
        {
            return false;
        }

        if (item.Tags is not null && item.Tags.Contains(GelatoStreamTag, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = item.Path;
        if (path.StartsWith("gelato", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("stremio", StringComparison.OrdinalIgnoreCase)
            || path.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return Path.IsPathRooted(path);
    }

    private IReadOnlyList<Meta> Resume(
        Jellyfin.Database.Implementations.Entities.User user,
        string type,
        Extra extra,
        int pageSize,
        string baseUrl)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [type == "movie" ? BaseItemKind.Movie : BaseItemKind.Episode],
            Recursive = true,
            IsVirtualItem = false,
            IsResumable = true,
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            DtoOptions = new DtoOptions(true),
        };

        IEnumerable<BaseItem> items = _libraryManager.GetItemList(query).Where(IsLocal);
        if (type == "series")
        {
            items = items
                .OfType<Episode>()
                .Select(e => e.Series)
                .Where(s => s is not null)
                .DistinctBy(s => s!.Id)!;
        }

        return items
            .DistinctBy(StremioIdOf)
            .Skip(extra.Skip)
            .Take(pageSize)
            .Select(item => ToMeta(item, type, baseUrl, withVideos: false))
            .ToList();
    }

    private BaseItem? FindTitle(Jellyfin.Database.Implementations.Entities.User user, string type, StremioId id)
    {
        var kind = type == "movie" ? BaseItemKind.Movie : BaseItemKind.Series;

        if (id.Source == IdSource.Jellyfin)
        {
            var item = _libraryManager.GetItemById(Guid.Parse(id.Value));
            return item is not null
                   && item.GetBaseItemKind() == kind
                   && item.IsVisibleStandalone(user)
                   && IsLocal(item)
                ? item
                : null;
        }

        var provider = id.Source == IdSource.Imdb ? MetadataProvider.Imdb : MetadataProvider.Tmdb;
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [kind],
            Recursive = true,
            IsVirtualItem = false,
            HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.ToString()] = id.Value,
            },
            DtoOptions = new DtoOptions(true),
        };

        return _libraryManager.GetItemList(query).FirstOrDefault(IsLocal);
    }

    private IEnumerable<BaseItem> FindPlayable(Jellyfin.Database.Implementations.Entities.User user, string type, StremioId id)
    {
        if (type == "movie")
        {
            if (id.Source == IdSource.Jellyfin)
            {
                var single = FindTitle(user, "movie", id);
                return single is null ? [] : [single];
            }

            var provider = id.Source == IdSource.Imdb ? MetadataProvider.Imdb : MetadataProvider.Tmdb;
            return _libraryManager.GetItemList(new InternalItemsQuery(user)
                {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    Recursive = true,
                    IsVirtualItem = false,
                    HasAnyProviderId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [provider.ToString()] = id.Value,
                    },
                    DtoOptions = new DtoOptions(true),
                })
                .Where(IsLocal);
        }

        if (!id.IsEpisode || FindTitle(user, "series", id) is not Series series)
        {
            return [];
        }

        return Episodes(user, series)
            .Where(e => e.ParentIndexNumber == id.Season
                        && e.IndexNumber is { } first
                        && id.Episode >= first
                        && id.Episode <= (e.IndexNumberEnd ?? first));
    }

    private IEnumerable<Episode> Episodes(Jellyfin.Database.Implementations.Entities.User user, Series series) =>
        _libraryManager.GetItemList(new InternalItemsQuery(user)
            {
                AncestorIds = [series.Id],
                IncludeItemTypes = [BaseItemKind.Episode],
                Recursive = true,
                IsVirtualItem = false,
                DtoOptions = new DtoOptions(true),
            })
            .OfType<Episode>()
            .Where(IsLocal);

    private IEnumerable<MediaSourceInfo> LocalSources(BaseItem item, Jellyfin.Database.Implementations.Entities.User user) =>
        _mediaSourceManager.GetStaticMediaSources(item, false, user)
            .Where(s => s.Protocol == MediaProtocol.File
                        && !string.IsNullOrEmpty(s.Path)
                        && File.Exists(s.Path));

    private Meta ToMeta(BaseItem item, string type, string baseUrl, bool withVideos)
    {
        var id = StremioIdOf(item);
        IReadOnlyList<StremioVideo>? videos = null;

        if (withVideos && item is Series series)
        {
            videos = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    AncestorIds = [series.Id],
                    IncludeItemTypes = [BaseItemKind.Episode],
                    Recursive = true,
                    IsVirtualItem = false,
                    DtoOptions = new DtoOptions(true),
                })
                .OfType<Episode>()
                .Where(e => IsLocal(e) && e.ParentIndexNumber is not null && e.IndexNumber is not null)
                .OrderBy(e => e.ParentIndexNumber)
                .ThenBy(e => e.IndexNumber)
                .SelectMany(e => EpisodeNumbers(e).Select(n => new StremioVideo
                {
                    Id = StremioId.ForEpisode(id, e.ParentIndexNumber!.Value, n),
                    Title = e.Name ?? string.Create(CultureInfo.InvariantCulture, $"Episode {n}"),
                    Season = e.ParentIndexNumber!.Value,
                    Episode = n,
                    Released = e.PremiereDate?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                    Overview = e.Overview,
                    Thumbnail = ImageUrl(baseUrl, e, ImageType.Primary, 640),
                }))
                .ToList();
        }

        return new Meta
        {
            Id = id,
            Type = type,
            Name = item.Name ?? string.Empty,
            Poster = ImageUrl(baseUrl, item, ImageType.Primary, 500),
            Background = ImageUrl(baseUrl, item, ImageType.Backdrop, 1920),
            Logo = ImageUrl(baseUrl, item, ImageType.Logo, 800),
            Description = item.Overview,
            ReleaseInfo = ReleaseInfo(item),
            Genres = item.Genres is { Length: > 0 } genres ? genres : null,
            ImdbRating = item.CommunityRating is { } rating
                ? rating.ToString("0.0", CultureInfo.InvariantCulture)
                : null,
            Runtime = item.RunTimeTicks is { } ticks && ticks > 0
                ? ((int)TimeSpan.FromTicks(ticks).TotalMinutes).ToString(CultureInfo.InvariantCulture) + " min"
                : null,
            Videos = videos,
        };
    }

    private static IEnumerable<int> EpisodeNumbers(Episode e)
    {
        var first = e.IndexNumber!.Value;
        var last = Math.Max(first, e.IndexNumberEnd ?? first);
        return Enumerable.Range(first, last - first + 1);
    }

    private static string? ReleaseInfo(BaseItem item)
    {
        if (item.ProductionYear is not { } year)
        {
            return null;
        }

        if (item is Series series)
        {
            var end = series.EndDate?.Year;
            return series.Status == SeriesStatus.Ended && end is not null && end != year
                ? string.Create(CultureInfo.InvariantCulture, $"{year}–{end}")
                : string.Create(CultureInfo.InvariantCulture, $"{year}–");
        }

        return year.ToString(CultureInfo.InvariantCulture);
    }

    private static string? ImageUrl(string baseUrl, BaseItem item, ImageType type, int maxWidth)
    {
        if (!item.HasImage(type, 0))
        {
            return null;
        }

        // Jellyfin serves item images without authentication; the tag makes the URL change
        // (and caches refresh) when the image does.
        var info = item.GetImageInfo(type, 0);
        var tag = info?.DateModified.Ticks.ToString(CultureInfo.InvariantCulture);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{baseUrl}/Items/{item.Id:N}/Images/{type}/0?maxWidth={maxWidth}&quality=90{(tag is null ? string.Empty : "&tag=" + tag)}");
    }

    private static MediaFacts Facts(BaseItem item, MediaSourceInfo source)
    {
        var streams = source.MediaStreams ?? [];
        var video = streams.FirstOrDefault(s => s.Type == MediaStreamType.Video);
        var episode = item as Episode;
        return new MediaFacts
        {
            FileName = Path.GetFileName(source.Path),
            Container = source.Container,
            Size = source.Size ?? TryFileSize(source.Path),
            Width = video?.Width,
            Height = video?.Height,
            VideoCodec = video?.Codec,
            VideoRangeType = video?.VideoRangeType.ToString(),
            BitDepth = video?.BitDepth,
            VideoIndex = video?.Index,
            Audio = streams
                .Where(s => s.Type == MediaStreamType.Audio)
                .Select(s => new AudioFacts(s.Codec, s.Channels, s.Language, s.Profile, s.Title, s.Index, s.IsDefault))
                .ToList(),
            Subtitles = streams
                .Where(s => s.Type == MediaStreamType.Subtitle)
                .Select(s => new SubtitleFacts(
                    s.Language,
                    s.Title,
                    s.Codec,
                    s.Index,
                    s.IsForced,
                    s.IsHearingImpaired,
                    s.IsDefault,
                    s.IsExternal))
                .ToList(),
            VersionName = source.Name,
            Title = episode is null ? item.Name : episode.SeriesName,
            Year = episode is null ? item.ProductionYear : null,
            Season = episode?.ParentIndexNumber,
            Episodes = episode?.IndexNumber is null ? [] : EpisodeNumbers(episode).ToList(),
            RunTimeTicks = source.RunTimeTicks ?? item.RunTimeTicks,
        };
    }

    private static long? TryFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int ResolutionRank(string name) =>
        name.Contains("2160p", StringComparison.Ordinal) ? 6
        : name.Contains("1440p", StringComparison.Ordinal) ? 5
        : name.Contains("1080p", StringComparison.Ordinal) ? 4
        : name.Contains("720p", StringComparison.Ordinal) ? 3
        : name.Contains("576p", StringComparison.Ordinal) ? 2
        : name.Contains("480p", StringComparison.Ordinal) ? 1
        : 0;
}
