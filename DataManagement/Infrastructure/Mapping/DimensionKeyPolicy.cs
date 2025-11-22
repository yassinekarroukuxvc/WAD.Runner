using System.Collections.Concurrent;
using WAD.Runner.DataManagement.Domain.Dimensions;

namespace WAD.Runner.DataManagement.Infrastructure.Mapping;

/// <summary>
/// Central policy for normalizing transport keys (e.g., "Wed-TL", "PGB-BA")
/// into domain keys (e.g., "TL", "BA") and classifying whether a key is an
/// angle (deg) or a length (mm).
/// </summary>
public static class DimensionKeyPolicy
{
    // Known angular keys (domain form, WITHOUT "Wed-" / "PGB-" prefixes)
    private static readonly ConcurrentDictionary<string, byte> AngleKeys = new(
        new[]
        {
            "BA",   // Back angle
            "FA",   // Front angle
            "ISA",  // Included side angle
            "GA",   // Grind angle (example)
            "VRA"
        }.ToDictionary(k => k, _ => (byte)1, StringComparer.OrdinalIgnoreCase)
    );

    /// <summary>
    /// Strips known prefixes ("Wed-", "PGB-") and trims whitespace.
    /// </summary>
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

    /// <summary>
    /// Converts a transport key into the domain DimensionKey (normalized).
    /// </summary>
    public static DimensionKey ToDomainKey(string transportKey)
        => DimensionKey.From(NormalizeKey(transportKey));

    /// <summary>
    /// True if the (transport or domain) key represents an ANGLE (deg).
    /// </summary>
    public static bool IsAngle(string keyMaybeWithPrefix)
    {
        var k = NormalizeKey(keyMaybeWithPrefix);
        return !string.IsNullOrEmpty(k) && AngleKeys.ContainsKey(k);
    }

    /// <summary>
    /// True if the (transport or domain) key represents a LENGTH (mm).
    /// </summary>
    public static bool IsLength(string keyMaybeWithPrefix) => !IsAngle(keyMaybeWithPrefix);

    /// <summary>
    /// Allows extending the known set of angle keys at runtime (case-insensitive).
    /// Safe to call multiple times; no-op if already present.
    /// </summary>
    public static void RegisterAngleKey(string domainKey)
    {
        if (string.IsNullOrWhiteSpace(domainKey)) return;
        AngleKeys.TryAdd(domainKey.Trim(), 1);
    }

    /// <summary>
    /// Removes an angle key from the known set (case-insensitive).
    /// </summary>
    public static void UnregisterAngleKey(string domainKey)
    {
        if (string.IsNullOrWhiteSpace(domainKey)) return;
        AngleKeys.TryRemove(domainKey.Trim(), out _);
    }
}
