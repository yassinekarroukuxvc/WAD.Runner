// PartAutomation/SolidWorks/Interop/FeatureSuppression.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace WAD.Runner.PartAutomation.SolidWorks.Interop
{
    /// <summary>
    /// Applies suppression/unsuppression using SetSuppression2 in swThisConfiguration,
    /// skipping calls when the current state already matches.
    ///
    /// Improvements:
    /// - Avoids dynamic COM late-binding (uses cached reflection call for IsSuppressed2 / IsSuppressed)
    /// - Better diagnostics & fewer wasted COM calls
    /// </summary>
    public static class FeatureSuppression
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo?> IsSuppressed2Cache = new();
        private static readonly ConcurrentDictionary<Type, MethodInfo?> IsSuppressedCache = new();

        public static void Apply(
            IReadOnlyDictionary<string, Feature> featureMap,
            IEnumerable<string> names,
            bool suppress,
            Action<string>? log = null)
        {
            if (featureMap is null) throw new ArgumentNullException(nameof(featureMap));
            if (names is null) return;

            var action = suppress
                ? (int)swFeatureSuppressionAction_e.swSuppressFeature
                : (int)swFeatureSuppressionAction_e.swUnSuppressFeature;

            var cfgOpt = (int)swInConfigurationOpts_e.swThisConfiguration;

            foreach (var raw in names)
            {
                var nm = (raw ?? string.Empty).Trim();
                if (nm.Length == 0) continue;

                if (!featureMap.TryGetValue(nm, out var feat) || feat is null)
                {
                    log?.Invoke($"[FeatureSuppression] Feature not found: {nm}");
                    continue;
                }

                // Skip COM call if already in desired state
                if (TryIsSuppressedFast(feat, cfgOpt, out var isSuppressed) && isSuppressed == suppress)
                    continue;

                try
                {
                    feat.SetSuppression2(action, cfgOpt, null);
                }
                catch (Exception ex)
                {
                    log?.Invoke($"[FeatureSuppression] SetSuppression2 failed for '{nm}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Fast suppression check: prefer IsSuppressed2(cfgOpt, null), fallback IsSuppressed().
        /// Uses cached reflection to avoid dynamic COM overhead.
        /// </summary>
        public static bool TryIsSuppressedFast(Feature feat, int cfgOpt, out bool isSuppressed)
        {
            isSuppressed = false;
            if (feat is null) return false;

            var t = feat.GetType();

            // Try IsSuppressed2(int, object)
            var m2 = IsSuppressed2Cache.GetOrAdd(t, ResolveIsSuppressed2);
            if (m2 != null)
            {
                try
                {
                    var v = m2.Invoke(feat, new object?[] { cfgOpt, null });
                    if (TryCoerceBool(v, out isSuppressed)) return true;
                }
                catch
                {
                    // fall through
                }
            }

            // Fallback IsSuppressed()
            var m1 = IsSuppressedCache.GetOrAdd(t, ResolveIsSuppressed);
            if (m1 != null)
            {
                try
                {
                    var v = m1.Invoke(feat, Array.Empty<object>());
                    if (TryCoerceBool(v, out isSuppressed)) return true;
                }
                catch
                {
                    // ignored
                }
            }

            return false;
        }

        private static MethodInfo? ResolveIsSuppressed2(Type t)
        {
            return t.GetMethod(
                "IsSuppressed2",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(int), typeof(object) },
                modifiers: null);
        }

        private static MethodInfo? ResolveIsSuppressed(Type t)
        {
            return t.GetMethod(
                "IsSuppressed",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
        }

        private static bool TryCoerceBool(object? v, out bool b)
        {
            b = false;
            if (v is null) return false;

            if (v is bool bb) { b = bb; return true; }
            if (v is int i) { b = i != 0; return true; }
            if (v is short s) { b = s != 0; return true; }
            if (v is long l) { b = l != 0; return true; }

            return false;
        }
    }
}
