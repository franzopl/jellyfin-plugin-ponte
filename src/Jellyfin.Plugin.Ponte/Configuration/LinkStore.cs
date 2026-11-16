using System.Security.Cryptography;

namespace Jellyfin.Plugin.Ponte.Configuration;

/// <summary>
/// Issues, finds and revokes the per-user tokens of addon URLs.
/// </summary>
public sealed class LinkStore
{
    private readonly Plugin _plugin;
    private readonly Lock _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkStore"/> class.
    /// </summary>
    public LinkStore(Plugin plugin)
    {
        _plugin = plugin;
    }

    /// <summary>The user a token belongs to, or null for an unknown token.</summary>
    public Guid? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 64)
        {
            return null;
        }

        var tokenBytes = System.Text.Encoding.ASCII.GetBytes(token);
        foreach (var link in _plugin.Configuration.Links)
        {
            if (link.Token.Length == token.Length
                && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(link.Token), tokenBytes))
            {
                return link.UserId;
            }
        }

        return null;
    }

    /// <summary>The user's token, created on first use.</summary>
    public string GetOrCreate(Guid userId)
    {
        lock (_lock)
        {
            var existing = _plugin.Configuration.Links.FirstOrDefault(l => l.UserId == userId);
            if (existing is not null)
            {
                return existing.Token;
            }

            return Issue(userId);
        }
    }

    /// <summary>Replaces the user's token; the old addon URL stops working.</summary>
    public string Reset(Guid userId)
    {
        lock (_lock)
        {
            _plugin.Configuration.Links.RemoveAll(l => l.UserId == userId);
            return Issue(userId);
        }
    }

    private string Issue(Guid userId)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _plugin.Configuration.Links.Add(new UserLink { UserId = userId, Token = token, CreatedAt = DateTime.UtcNow });
        _plugin.SaveConfiguration();
        return token;
    }
}
