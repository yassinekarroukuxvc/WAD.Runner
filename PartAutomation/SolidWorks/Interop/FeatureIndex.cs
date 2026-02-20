// PartAutomation/SolidWorks/Interop/FeatureIndex.cs
using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

namespace WAD.Runner.PartAutomation.SolidWorks.Interop
{
    /// <summary>
    /// Builds a case-insensitive index of all features and sub-features by name.
    /// Macro-aligned: FirstFeature -> GetNextFeature, plus recursive sub-feature walk.
    ///
    /// Notes:
    /// - Feature names are NOT guaranteed unique in SW; first occurrence wins by default.
    /// - Optionally collects duplicates for diagnostics.
    /// </summary>
    public static class FeatureIndex
    {
        public sealed class Result
        {
            public Result(
                IReadOnlyDictionary<string, Feature> map,
                IReadOnlyDictionary<string, List<Feature>> duplicates)
            {
                Map = map;
                Duplicates = duplicates;
            }

            public IReadOnlyDictionary<string, Feature> Map { get; }
            public IReadOnlyDictionary<string, List<Feature>> Duplicates { get; }
        }

        /// <summary>
        /// Build the feature index. By default, "first feature wins" on duplicate names.
        /// If trackDuplicates=true, all duplicates are collected in the Result.
        /// </summary>
        public static Result Build(ModelDoc2 model, bool trackDuplicates = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));

            var map = new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<Feature>>? dups = trackDuplicates
                ? new Dictionary<string, List<Feature>>(StringComparer.OrdinalIgnoreCase)
                : null;

            Feature? feat = null;

            try
            {
                feat = model.FirstFeature() as Feature;
            }
            catch
            {
                // COM can throw if model is in a bad state; return empty maps.
                return new Result(map, dups ?? new Dictionary<string, List<Feature>>(StringComparer.OrdinalIgnoreCase));
            }

            while (feat != null)
            {
                AddRecursive(feat, map, dups);
                try
                {
                    feat = feat.GetNextFeature() as Feature;
                }
                catch
                {
                    // Stop safely if traversal breaks.
                    break;
                }
            }

            return new Result(map, dups ?? new Dictionary<string, List<Feature>>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Convenience: if you only want the map (most callers do).
        /// </summary>
        public static IReadOnlyDictionary<string, Feature> BuildMap(ModelDoc2 model)
            => Build(model, trackDuplicates: false).Map;

        private static void AddRecursive(
            Feature feat,
            Dictionary<string, Feature> map,
            Dictionary<string, List<Feature>>? duplicates)
        {
            if (feat is null) return;

            var name = (feat.Name ?? string.Empty).Trim();
            if (name.Length > 0)
            {
                if (!map.TryAdd(name, feat))
                {
                    if (duplicates != null)
                    {
                        if (!duplicates.TryGetValue(name, out var list))
                        {
                            list = new List<Feature>(capacity: 2);
                            duplicates[name] = list;

                            // include the "winner" already stored
                            if (map.TryGetValue(name, out var winner) && winner != null)
                                list.Add(winner);
                        }

                        list.Add(feat);
                    }
                }
            }

            Feature? sub = null;
            try
            {
                sub = feat.GetFirstSubFeature() as Feature;
            }
            catch
            {
                sub = null;
            }

            while (sub != null)
            {
                AddRecursive(sub, map, duplicates);

                try
                {
                    sub = sub.GetNextSubFeature() as Feature;
                }
                catch
                {
                    break;
                }
            }
        }
    }
}
