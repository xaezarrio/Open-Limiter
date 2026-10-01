using System.Security.Cryptography;
using System.Text;

namespace OpenLimiter.Core.Naming;

public static class OwnedPolicyName
{
    public const string Prefix = "OpenLimiter";

    public static string Firewall(Guid ruleId, string direction) => $"{Prefix}:fw:{ruleId:N}:{direction}";

    public static string Qos(Guid ruleId) => $"{Prefix}-qos-{ShortHash(ruleId.ToString("N"))}";

    private static string ShortHash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}

