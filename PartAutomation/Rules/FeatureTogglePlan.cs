// PartAutomation/Rules/FeatureTogglePlan.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace WAD.Runner.PartAutomation.Rules
{
    /// <summary>
    /// Macro-style feature toggle plan:
    /// - Suppress everything in Off first
    /// - Then unsuppress everything in On
    /// - Optional group flags allow EquationUpsert to skip adding equations for suppressed groups
    ///   when variables are not referenced elsewhere (mirrors VBA behavior).
    /// </summary>
    public sealed class FeatureTogglePlan
    {
        /// <summary>
        /// Features/sketches to suppress (OFF).
        /// Applied first.
        /// </summary>
        public IReadOnlyList<string> Off { get; }

        /// <summary>
        /// Features/sketches to unsuppress (ON).
        /// Applied second.
        /// </summary>
        public IReadOnlyList<string> On { get; }

        /// <summary>
        /// Optional: group suppression map used by EquationUpsert.
        /// Key = base group token (usually the part of the variable name before '_' )
        /// Value = true means "group is suppressed" => skip adding unreferenced vars in that group.
        /// </summary>
        public IReadOnlyDictionary<string, bool> SuppressedGroups { get; }

        /// <summary>
        /// Optional notes for debugging (e.g. "FG overlay uses VFL sketch").
        /// </summary>
        public IReadOnlyList<string> Notes { get; }

        public FeatureTogglePlan(
            IEnumerable<string>? off = null,
            IEnumerable<string>? on = null,
            IReadOnlyDictionary<string, bool>? suppressedGroups = null,
            IEnumerable<string>? notes = null)
        {
            Off = Normalize(off);
            On = Normalize(on);

            SuppressedGroups = suppressedGroups is null
                ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, bool>(suppressedGroups, StringComparer.OrdinalIgnoreCase);

            Notes = Normalize(notes);
        }

        /// <summary>
        /// Ensures deterministic, macro-like behavior:
        /// - Any item present in On is removed from Off (On wins)
        /// - Both lists are deduplicated case-insensitively
        /// </summary>
        public FeatureTogglePlan Canonicalize()
        {
            var onSet = new HashSet<string>(On, StringComparer.OrdinalIgnoreCase);

            var offClean = Off
                .Where(x => !onSet.Contains(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var onClean = On
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new FeatureTogglePlan(offClean, onClean, SuppressedGroups, Notes);
        }

        /// <summary>
        /// Merge multiple plans:
        /// - Off union
        /// - On union
        /// - SuppressedGroups merged (true wins if conflicting)
        /// - Notes concatenated
        /// Then canonicalized (On wins over Off).
        /// </summary>
        public static FeatureTogglePlan Merge(params FeatureTogglePlan[] plans)
        {
            if (plans is null || plans.Length == 0)
                return new FeatureTogglePlan();

            var off = new List<string>();
            var on = new List<string>();
            var notes = new List<string>();
            var groups = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in plans.Where(x => x != null))
            {
                off.AddRange(p.Off);
                on.AddRange(p.On);
                notes.AddRange(p.Notes);

                foreach (var kv in p.SuppressedGroups)
                {
                    if (!groups.TryGetValue(kv.Key, out var cur))
                        groups[kv.Key] = kv.Value;
                    else
                        groups[kv.Key] = cur || kv.Value; // true wins
                }
            }

            return new FeatureTogglePlan(off, on, groups, notes).Canonicalize();
        }

        /// <summary>
        /// Fluent builder helper.
        /// </summary>
        public static Builder Create() => new Builder();

        public sealed class Builder
        {
            private readonly HashSet<string> _off = new(StringComparer.OrdinalIgnoreCase);
            private readonly HashSet<string> _on = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, bool> _groups = new(StringComparer.OrdinalIgnoreCase);
            private readonly List<string> _notes = new();

            // Existing API (kept)
            public Builder Off(params string[] names) { AddMany(_off, names); return this; }
            public Builder On(params string[] names) { AddMany(_on, names); return this; }

            // NEW: collection overloads (fixes your COBFeaturePlanner compile errors)
            public Builder Off(IEnumerable<string> names) { AddMany(_off, names); return this; }
            public Builder On(IEnumerable<string> names) { AddMany(_on, names); return this; }

            public Builder SuppressedGroup(string groupToken, bool isSuppressed = true)
            {
                if (string.IsNullOrWhiteSpace(groupToken)) return this;
                _groups[groupToken.Trim()] = isSuppressed;
                return this;
            }

            public Builder Note(string? note)
            {
                if (string.IsNullOrWhiteSpace(note)) return this;
                _notes.Add(note.Trim());
                return this;
            }

            public FeatureTogglePlan Build()
            {
                // On wins over Off
                foreach (var x in _on) _off.Remove(x);

                return new FeatureTogglePlan(_off, _on, _groups, _notes).Canonicalize();
            }

            private static void AddMany(HashSet<string> set, IEnumerable<string>? names)
            {
                if (names is null) return;
                foreach (var n in names)
                {
                    var t = (n ?? string.Empty).Trim();
                    if (t.Length == 0) continue;
                    set.Add(t);
                }
            }
        }

        private static IReadOnlyList<string> Normalize(IEnumerable<string>? src)
        {
            if (src is null) return Array.Empty<string>();

            var list = src
                .Select(s => (s ?? string.Empty).Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return list;
        }

        public override string ToString()
            => $"FeatureTogglePlan: OFF={Off.Count}, ON={On.Count}, Groups={SuppressedGroups.Count}, Notes={Notes.Count}";
    }
}
