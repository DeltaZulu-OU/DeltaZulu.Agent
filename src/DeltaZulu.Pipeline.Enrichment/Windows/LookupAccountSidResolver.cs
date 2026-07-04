using System.Security.Principal;

namespace DeltaZulu.Pipeline.Enrichment.Windows;

public sealed class LookupAccountSidResolver : IWindowsSidResolver
{
    public SidResolutionResult Resolve(string sid, DateTimeOffset observedAtUtc, TimeSpan cacheTtl)
    {
        try
        {
            var securityIdentifier = new SecurityIdentifier(sid);
            var account = (NTAccount)securityIdentifier.Translate(typeof(NTAccount));
            var canonical = account.Value;
            var slash = canonical.IndexOf('\\', StringComparison.Ordinal);
            var domain = slash > 0 ? canonical[..slash] : null;
            var name = slash > 0 ? canonical[(slash + 1)..] : canonical;
            return new SidResolutionResult(sid, name, domain, canonical, "User", DetermineScope(sid), "LookupAccountSidW", "Resolved", "High", observedAtUtc, observedAtUtc.Add(cacheTtl));
        }
        catch (IdentityNotMappedException)
        {
            return Unresolved(sid, observedAtUtc, cacheTtl, "NotMapped");
        }
        catch (Exception)
        {
            return Unresolved(sid, observedAtUtc, cacheTtl, "Error");
        }
    }

    private static SidResolutionResult Unresolved(string sid, DateTimeOffset observedAtUtc, TimeSpan cacheTtl, string status) =>
        new(sid, null, null, null, "Unknown", DetermineScope(sid), "LookupAccountSidW", status, "Unknown", observedAtUtc, observedAtUtc.Add(cacheTtl));

    private static string DetermineScope(string sid) => sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ? "LocalMachine" : "Unknown";
}