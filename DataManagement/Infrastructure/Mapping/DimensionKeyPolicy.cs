using System.Collections.Concurrent;
using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Infrastructure.Mapping;

public static class DimensionKeyPolicy
{

    private static readonly ConcurrentDictionary<string, byte> AngleKeys = new(
        new[]
        {
            "BA",    // Back Angle
            "CA",    // Cavity Angle
            "CBRA",  // CBR Angle
            "FA",    // Front Angle
            "FA2",   // 2nd Front Angle
            "FNA",   // Funnel Angle
            "FTA",   // Foot Angle
            "GA",    // Groove Angle
            "HA",    // Hole Angle
            "IDFA",  // Inner Hole Funnel Angle
            "ISA",   // Inclusive Side Angle
            "ISA2",
            "MTA",   // MTA
            "NA",    // Notch Angle
            "RA",    // Relief Angle
            "RA2",   // 2nd Relief Angle
            "SA",    // Slot Angle
            "VRA",   // Vertical Relief Angle
            "W2A",   // Chamfered Tip Width Angle
            "WA2",
        }.ToDictionary(
            key => key,
            _ => (byte)1,
            StringComparer.OrdinalIgnoreCase)
    );

    public static string NormalizeKey(string transportKey)
    {
        if (string.IsNullOrWhiteSpace(transportKey)) return string.Empty;

        var s = transportKey.Trim();

        if (s.StartsWith("Wed-", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(4);
        else if (s.StartsWith("PGB-", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(4);

        return s.Trim();
    }

    public static DimensionKey ToDomainKey(string transportKey)
        => DimensionKey.From(NormalizeKey(transportKey));

    public static bool IsAngle(string keyMaybeWithPrefix)
    {
        var k = NormalizeKey(keyMaybeWithPrefix);
        return !string.IsNullOrEmpty(k) && AngleKeys.ContainsKey(k);
    }

    public static bool IsLength(string keyMaybeWithPrefix) => !IsAngle(keyMaybeWithPrefix);

    public static void RegisterAngleKey(string domainKey)
    {
        if (string.IsNullOrWhiteSpace(domainKey)) return;
        AngleKeys.TryAdd(domainKey.Trim(), 1);
    }

    public static void UnregisterAngleKey(string domainKey)
    {
        if (string.IsNullOrWhiteSpace(domainKey)) return;
        AngleKeys.TryRemove(domainKey.Trim(), out _);
    }
}
