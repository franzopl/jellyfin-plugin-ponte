using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Ponte.Configuration;

/// <summary>
/// Ponte's settings, edited on the plugin page of the dashboard.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// The address clients reach this server at, such as <c>https://jellyfin.example.com</c>.
    /// Empty: the address of each request, which is right unless a proxy rewrites it.
    /// </summary>
    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>The addon name clients show. Empty: <c>Ponte · &lt;server name&gt;</c>.</summary>
    public string AddonName { get; set; } = string.Empty;

    /// <summary>Whether each movie and show library gets a catalog.</summary>
    public bool EnableLibraryCatalogs { get; set; } = true;

    /// <summary>Whether the "Recently added" catalogs are offered.</summary>
    public bool EnableRecentCatalogs { get; set; } = true;

    /// <summary>Whether the "Continue watching" catalogs are offered.</summary>
    public bool EnableResumeCatalogs { get; set; } = true;

    /// <summary>Whether external subtitle files are offered through the subtitles resource.</summary>
    public bool EnableSubtitles { get; set; } = true;

    /// <summary>Items per catalog page.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>The addon links handed out, one per user.</summary>
    public List<UserLink> Links { get; set; } = [];
}

/// <summary>
/// A user's addon link: the secret token in its URL stands for that user.
/// </summary>
public class UserLink
{
    /// <summary>The Jellyfin user the link plays as.</summary>
    public Guid UserId { get; set; }

    /// <summary>The secret path segment of the addon URL.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When the link was created, UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
