using System.Globalization;
using Jellyfin.Plugin.Ponte.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Ponte;

/// <summary>
/// Ponte: serves a Jellyfin library as a Stremio addon.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>The plugin id; also the id Jellyfin's repository manifest uses.</summary>
    public const string PluginGuid = "1bbb9efc-a177-428b-acdc-8d9370cc3a62";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Links = new LinkStore(this);
    }

    /// <summary>The running instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Ponte";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <inheritdoc />
    public override string Description => "Expose your Jellyfin library as a Stremio addon.";

    /// <summary>The per-user addon links.</summary>
    public LinkStore Links { get; }

    /// <summary>The version shown in the addon manifest: major.minor.patch.</summary>
    public string AddonVersion => string.Format(
        CultureInfo.InvariantCulture,
        "{0}.{1}.{2}",
        Version.Major,
        Version.Minor,
        Math.Max(Version.Build, 0));

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html",
        };
    }
}
