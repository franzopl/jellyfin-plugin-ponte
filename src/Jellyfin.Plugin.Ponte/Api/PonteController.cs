using System.Globalization;
using System.Net.Mime;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Data;
using Jellyfin.Plugin.Ponte.Library;
using Jellyfin.Plugin.Ponte.Stremio;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Ponte.Api;

/// <summary>
/// The addon endpoints under <c>/ponte/{token}/</c>, plus the page where users get their link.
/// </summary>
/// <remarks>
/// Stremio clients cannot send Jellyfin credentials, so the addon URL itself carries a secret
/// token that stands for one Jellyfin user. Anyone holding the URL can browse and play what
/// that user can; resetting the link revokes it.
/// </remarks>
[ApiController]
[Route("ponte")]
public class PonteController : ControllerBase
{
    private static readonly string[] Types = ["movie", "series"];
    private static readonly string[] IdPrefixes = ["tt", "tmdb:", StremioId.PontePrefix];

    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _host;
    private readonly LibraryService _library;

    /// <summary>
    /// Initializes a new instance of the <see cref="PonteController"/> class.
    /// </summary>
    public PonteController(
        IUserManager userManager,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IServerApplicationHost host)
    {
        _userManager = userManager;
        _host = host;
        _library = new LibraryService(libraryManager, mediaSourceManager);
    }

    private static Plugin PluginInstance =>
        Plugin.Instance ?? throw new InvalidOperationException("The Ponte plugin is not loaded.");

    private static Configuration.PluginConfiguration Config => PluginInstance.Configuration;

    // ---- Link page and the signed-in user's link -------------------------------------------

    /// <summary>The page where a user signs in and gets their addon link.</summary>
    [HttpGet("link")]
    [AllowAnonymous]
    public ActionResult LinkPage()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.link.html");
        return stream is null ? NotFound() : File(stream, "text/html; charset=utf-8");
    }

    /// <summary>The signed-in user's addon link, created on first use.</summary>
    [HttpGet("me")]
    [Authorize]
    public ActionResult<LinkResponse> GetMyLink()
    {
        var userId = CurrentUserId();
        return userId is null ? Unauthorized() : LinkFor(userId.Value, PluginInstance.Links.GetOrCreate(userId.Value));
    }

    /// <summary>Replaces the signed-in user's addon link; the old one stops working.</summary>
    [HttpPost("me/reset")]
    [Authorize]
    public ActionResult<LinkResponse> ResetMyLink()
    {
        var userId = CurrentUserId();
        return userId is null ? Unauthorized() : LinkFor(userId.Value, PluginInstance.Links.Reset(userId.Value));
    }

    // ---- Addon resources -------------------------------------------------------------------

    /// <summary>The addon manifest.</summary>
    [HttpGet("{token}/manifest.json")]
    public ActionResult Manifest([FromRoute] string token)
    {
        var user = UserFor(token);
        if (user is null)
        {
            return NotFound();
        }

        var config = Config;
        var catalogs = new List<CatalogDefinition>();
        var browse = new ExtraDefinition[] { new("skip"), new("search") };
        var pageOnly = new ExtraDefinition[] { new("skip") };

        if (config.EnableRecentCatalogs)
        {
            catalogs.Add(new("movie", LibraryService.RecentCatalogId, "Recently added", pageOnly));
            catalogs.Add(new("series", LibraryService.RecentCatalogId, "Recently added", pageOnly));
        }

        if (config.EnableResumeCatalogs)
        {
            catalogs.Add(new("movie", LibraryService.ResumeCatalogId, "Continue watching", pageOnly));
            catalogs.Add(new("series", LibraryService.ResumeCatalogId, "Continue watching", pageOnly));
        }

        if (config.EnableLibraryCatalogs)
        {
            foreach (var (folder, type) in _library.Libraries(user))
            {
                catalogs.Add(new(
                    type,
                    LibraryService.LibraryCatalogPrefix + folder.Id.ToString("N", CultureInfo.InvariantCulture),
                    folder.Name,
                    browse));
            }
        }

        var resources = new List<object>
        {
            "catalog",
            new ResourceDefinition("meta", Types, [.. IdPrefixes]),
            new ResourceDefinition("stream", Types, [.. IdPrefixes]),
        };
        if (config.EnableSubtitles)
        {
            resources.Add(new ResourceDefinition("subtitles", Types, [.. IdPrefixes]));
        }

        return Json(new Manifest
        {
            Id = "cc.ponte." + _host.SystemId,
            Version = PluginInstance.AddonVersion,
            Name = AddonName(),
            Description = $"Movies and shows from the Jellyfin server {_host.FriendlyName}, played straight from the files.",
            Resources = resources,
            Types = Types,
            IdPrefixes = IdPrefixes,
            Catalogs = catalogs,
            BehaviorHints = new ManifestHints(Configurable: false, ConfigurationRequired: false),
        });
    }

    /// <summary>A catalog page.</summary>
    [HttpGet("{token}/catalog/{type}/{id}.json")]
    [HttpGet("{token}/catalog/{type}/{id}/{extra}.json")]
    public ActionResult Catalog([FromRoute] string token, [FromRoute] string type, [FromRoute] string id, [FromRoute] string? extra)
    {
        var user = UserFor(token);
        if (user is null)
        {
            return NotFound();
        }

        if (!Types.Contains(type))
        {
            return Json(new { metas = Array.Empty<Meta>() });
        }

        var pageSize = Math.Clamp(Config.PageSize, 1, 500);
        return Json(new { metas = _library.Catalog(user, type, id, Stremio.Extra.Parse(extra), pageSize, BaseUrl()) });
    }

    /// <summary>A title's meta.</summary>
    [HttpGet("{token}/meta/{type}/{id}.json")]
    public ActionResult Meta([FromRoute] string token, [FromRoute] string type, [FromRoute] string id)
    {
        var user = UserFor(token);
        if (user is null)
        {
            return NotFound();
        }

        var parsed = StremioId.Parse(id);
        var meta = parsed is null || !Types.Contains(type) ? null : _library.GetMeta(user, type, parsed, BaseUrl());
        return Json(new { meta });
    }

    /// <summary>The streams of a movie or episode.</summary>
    [HttpGet("{token}/stream/{type}/{id}.json")]
    public ActionResult Streams([FromRoute] string token, [FromRoute] string type, [FromRoute] string id)
    {
        var user = UserFor(token);
        if (user is null)
        {
            return NotFound();
        }

        var parsed = StremioId.Parse(id);
        if (parsed is null || !Types.Contains(type))
        {
            return Json(new { streams = Array.Empty<Stremio.Stream>() });
        }

        var baseUrl = BaseUrl();
        var streams = _library.Streams(
            user,
            type,
            parsed,
            AddonName(),
            (item, source) => string.Create(
                CultureInfo.InvariantCulture,
                $"{baseUrl}/ponte/{token}/play/{item.Id:N}/{source.Id}/{Uri.EscapeDataString(Path.GetFileName(source.Path))}"));
        return Json(new { streams });
    }

    /// <summary>The external subtitle files of a movie or episode.</summary>
    [HttpGet("{token}/subtitles/{type}/{id}.json")]
    [HttpGet("{token}/subtitles/{type}/{id}/{extra}.json")]
    public ActionResult Subtitles([FromRoute] string token, [FromRoute] string type, [FromRoute] string id, [FromRoute] string? extra)
    {
        var user = UserFor(token);
        if (user is null)
        {
            return NotFound();
        }

        var parsed = StremioId.Parse(id);
        if (!Config.EnableSubtitles || parsed is null || !Types.Contains(type))
        {
            return Json(new { subtitles = Array.Empty<Subtitle>() });
        }

        var baseUrl = BaseUrl();
        var playing = PlayingFile.Parse(extra is null ? null : RawExtra() ?? extra);
        var subtitles = _library.ExternalSubtitles(user, type, parsed, playing)
            .Select(s =>
            {
                var facts = new SubtitleFacts(
                    s.Stream.Language,
                    s.Stream.Title,
                    IsForced: s.Stream.IsForced,
                    IsHearingImpaired: s.Stream.IsHearingImpaired);
                return new Subtitle(
                    string.Create(CultureInfo.InvariantCulture, $"ponte-{s.Item.Id:N}-{s.Stream.Index}"),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{baseUrl}/ponte/{token}/subtitle/{s.Item.Id:N}/{s.Source.Id}/{s.Stream.Index}/{Uri.EscapeDataString(Path.GetFileName(s.Stream.Path))}"),
                    StreamFormatter.SubtitleLanguageCode(s.Stream.Language),
                    StreamFormatter.ExternalSubtitleLabel,
                    StreamFormatter.IsForced(facts) ? true : null,
                    StreamFormatter.IsHearingImpaired(facts) ? true : null);
            })
            .ToList();
        return Json(new { subtitles });
    }

    // ---- Files -----------------------------------------------------------------------------

    /// <summary>Serves a media file, with range requests so players can seek.</summary>
    [HttpGet("{token}/play/{itemId}/{sourceId}/{fileName}")]
    [HttpHead("{token}/play/{itemId}/{sourceId}/{fileName}")]
    public ActionResult Play([FromRoute] string token, [FromRoute] Guid itemId, [FromRoute] string sourceId, [FromRoute] string fileName)
    {
        var user = UserFor(token);
        var source = user is null ? null : _library.FindSource(user, itemId, sourceId);
        if (source?.Path is null)
        {
            return NotFound();
        }

        return PhysicalFile(source.Path, MimeTypes.GetMimeType(source.Path) ?? MediaTypeNames.Application.Octet, enableRangeProcessing: true);
    }

    /// <summary>Serves an external subtitle file.</summary>
    [HttpGet("{token}/subtitle/{itemId}/{sourceId}/{index}/{fileName}")]
    public ActionResult Subtitle([FromRoute] string token, [FromRoute] Guid itemId, [FromRoute] string sourceId, [FromRoute] int index, [FromRoute] string fileName)
    {
        var user = UserFor(token);
        var source = user is null ? null : _library.FindSource(user, itemId, sourceId);
        var stream = source?.MediaStreams?.FirstOrDefault(s =>
            s.Index == index && s.IsExternal && s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle);
        if (stream?.Path is null || !System.IO.File.Exists(stream.Path))
        {
            return NotFound();
        }

        return PhysicalFile(stream.Path, MimeTypes.GetMimeType(stream.Path) ?? MediaTypeNames.Text.Plain);
    }

    // ---- Helpers ---------------------------------------------------------------------------

    private Jellyfin.Database.Implementations.Entities.User? UserFor(string token)
    {
        // Stremio Web and other browser clients fetch addons cross-origin.
        Response.Headers.AccessControlAllowOrigin = "*";

        var userId = PluginInstance.Links.Resolve(token);
        if (userId is null)
        {
            return null;
        }

        var user = _userManager.GetUserById(userId.Value);
        return user is null || user.HasPermission(Jellyfin.Database.Implementations.Enums.PermissionKind.IsDisabled) ? null : user;
    }

    // Routing has already decoded the segment, which turns an encoded "&" in a file name into a
    // separator, so the extra is read from the request line as the client sent it.
    private string? RawExtra()
    {
        var target = HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (string.IsNullOrEmpty(target))
        {
            return null;
        }

        var query = target.IndexOf('?', StringComparison.Ordinal);
        var path = query >= 0 ? target[..query] : target;
        var segment = path[(path.LastIndexOf('/') + 1)..];
        return segment.EndsWith(".json", StringComparison.Ordinal) ? segment[..^5] : null;
    }

    private Guid? CurrentUserId()
    {
        // Jellyfin's authentication handler puts the user id in this claim.
        var value = User.FindFirst("Jellyfin-UserId")?.Value;
        return Guid.TryParse(value, out var id) && id != Guid.Empty ? id : null;
    }

    private LinkResponse LinkFor(Guid userId, string token)
    {
        var manifest = $"{BaseUrl()}/ponte/{token}/manifest.json";
        var user = _userManager.GetUserById(userId);
        return new LinkResponse(user?.Username ?? string.Empty, manifest, "stremio://" + manifest[(manifest.IndexOf("://", StringComparison.Ordinal) + 3)..]);
    }

    private string AddonName() =>
        string.IsNullOrWhiteSpace(Config.AddonName) ? "Ponte · " + _host.FriendlyName : Config.AddonName.Trim();

    private string BaseUrl()
    {
        var configured = Config.PublicUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        var request = Request;
        return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
    }

    private ContentResult Json(object value) =>
        Content(JsonSerializer.Serialize(value, StremioJson.Options), "application/json; charset=utf-8");
}

/// <summary>A user's addon link.</summary>
/// <param name="UserName">The Jellyfin user the link plays as.</param>
/// <param name="ManifestUrl">The addon URL to paste into a client.</param>
/// <param name="StremioUrl">The <c>stremio://</c> URL that opens Stremio's install dialog.</param>
public sealed record LinkResponse(string UserName, string ManifestUrl, string StremioUrl);
