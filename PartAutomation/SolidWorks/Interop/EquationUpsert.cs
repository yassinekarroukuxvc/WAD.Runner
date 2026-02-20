// PartAutomation/SolidWorks/Interop/EquationUpsert.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace WAD.Runner.PartAutomation.SolidWorks.Interop
{
    /// <summary>
    /// Macro-aligned batch upsert for global variables in EquationMgr:
    /// - Parses existing equations once (lhs -> index)
    /// - Collects referenced quoted names from RHS (VBA behavior)
    /// - Updates existing vars only when needed:
    ///     * numeric EPS compare when both sides are numeric
    ///     * otherwise normalized string compare
    ///
    /// IMPORTANT FOR MACRO PARITY:
    /// - In production jobs you typically want UPDATE-ONLY (no runtime Add3/Add2).
    ///   Runtime adds are slow and can trigger feature/solve side effects (including suppressed-feature errors).
    /// - Missing vars should be pre-seeded in the template (or by a separate "seeder" step).
    /// </summary>
    public static class EquationUpsert
    {
        /// <summary>
        /// Batch upsert globals into the model EquationMgr.
        /// </summary>
        /// <param name="eqMgr">SolidWorks EquationMgr</param>
        /// <param name="updates">varName -> value (numeric => unitless invariant; string => preserved expression/units)</param>
        /// <param name="eps">numeric delta threshold (when both sides numeric)</param>
        /// <param name="log">optional logger</param>
        /// <param name="allowAdds">
        /// If false (recommended for macro parity): missing vars are SKIPPED and logged (no Add3/Add2).
        /// If true: missing vars are added at the end (slower, more side effects).
        /// </param>
        public static void BatchUpsertGlobals(
            EquationMgr eqMgr,
            IDictionary<string, object> updates,
            double eps,
            Action<string>? log = null,
            bool allowAdds = false)
        {
            if (eqMgr is null) throw new ArgumentNullException(nameof(eqMgr));
            if (updates is null) throw new ArgumentNullException(nameof(updates));

            // Macro-like: keep SW quiet during batch writes.
            // In your service you already set these to false; we also protect here.
            bool prevAutoSolve = false;
            bool prevAutoRebuild = false;
            bool hasPrevSolve = false;
            bool hasPrevRebuild = false;

            try
            {
                try { prevAutoSolve = eqMgr.AutomaticSolveOrder; hasPrevSolve = true; } catch { }
                try { prevAutoRebuild = eqMgr.AutomaticRebuild; hasPrevRebuild = true; } catch { }

                // IMPORTANT: for batch upsert we do NOT want SW rebuilding/solving while writing.
                // We'll do one explicit rebuild after upsert in PartAutomationService.
                try { eqMgr.AutomaticSolveOrder = false; } catch { }
                try { eqMgr.AutomaticRebuild = false; } catch { }

                // Existing "lhs" -> index
                var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                // Names referenced on RHS (quoted) anywhere in the equations
                var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Queue for adds (used only when allowAdds=true)
                var newAdds = allowAdds
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : null;

                // ---------------------------
                // 1) Read all equations ONCE (reduce COM calls)
                // ---------------------------
                int cnt = eqMgr.GetCount();
                var equations = new string[cnt];
                for (int i = 0; i < cnt; i++)
                    equations[i] = eqMgr.Equation[i] ?? string.Empty;

                // ---------------------------
                // 2) Parse existing equations + RHS references (macro behavior)
                // ---------------------------
                for (int i = 0; i < cnt; i++)
                {
                    var eq = equations[i];
                    int p = eq.IndexOf('=');
                    if (p <= 0) continue;

                    var left = eq[..p].Trim().Replace("\"", "").Trim();
                    if (left.Length > 0 && !existing.ContainsKey(left))
                        existing[left] = i;

                    // Collect RHS quoted references: "SomeVar"
                    var rhs = eq[(p + 1)..];
                    int q = 0;
                    while (true)
                    {
                        int s = rhs.IndexOf('"', q);
                        if (s < 0) break;
                        int e = rhs.IndexOf('"', s + 1);
                        if (e < 0) break;

                        var rname = rhs.Substring(s + 1, e - s - 1).Trim();
                        if (rname.Length > 0) referenced.Add(rname);
                        q = e + 1;
                    }
                }

                // ---------------------------
                // 3) Optional suppressed groups map (only relevant when allowAdds=true)
                // ---------------------------
                IDictionary<string, bool>? suppressedGroups = null;
                if (updates.TryGetValue("__suppressedGroups", out var sgObj))
                    suppressedGroups = sgObj as IDictionary<string, bool>;

                LogSuppressedGroups(suppressedGroups, log);

                int updated = 0;
                int added = 0;
                int skippedAddByGroup = 0;
                int skippedMissing = 0;

                // ---------------------------
                // 4) Upsert loop (macro behavior)
                // ---------------------------
                foreach (var kv in updates)
                {
                    var varName = (kv.Key ?? string.Empty).Trim();
                    if (varName.Length == 0) continue;

                    if (string.Equals(varName, "__suppressedGroups", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Base group token is the prefix before '_' (macro behavior)
                    var baseName = varName.Split('_')[0];

                    // Build target RHS (macro behavior)
                    var targetRhs = BuildTargetRhsMacroAligned(kv.Value, out bool targetIsNumeric, out double numericTarget);

                    if (existing.TryGetValue(varName, out var idx))
                    {
                        var curEq = equations[idx];

                        bool curIsNumeric = TryParseValueFromEquation(curEq, out var curNum);
                        bool needWrite = true;

                        if (curIsNumeric && targetIsNumeric)
                        {
                            if (Math.Abs(curNum - numericTarget) <= eps)
                                needWrite = false;
                        }
                        else
                        {
                            var curNorm = NormalizeEquationString(varName, curEq);
                            var targetEq = $"\"{varName}\"={targetRhs}";
                            if (string.Equals(curNorm, targetEq, StringComparison.Ordinal))
                                needWrite = false;
                        }

                        if (needWrite)
                        {
                            var newEq = $"\"{varName}\"={targetRhs}";
                            eqMgr.Equation[idx] = newEq;   // COM write
                            equations[idx] = newEq;        // update local cache
                            updated++;
                        }
                    }
                    else
                    {
                        // Missing variable
                        if (!allowAdds)
                        {
                            // UPDATE-ONLY MODE (macro parity): do NOT add at runtime.
                            skippedMissing++;
                            log?.Invoke($"[EquationUpsert] MISSING (skip, allowAdds=false): {varName}");
                            continue;
                        }

                        // allowAdds=true path: consider group skip when NOT referenced
                        if (!referenced.Contains(varName))
                        {
                            if (suppressedGroups != null &&
                                suppressedGroups.TryGetValue(baseName, out var skip) &&
                                skip)
                            {
                                skippedAddByGroup++;
                                log?.Invoke($"[EquationUpsert] SKIP-ADD by group: var={varName}, base={baseName} (group suppressed), referenced=false");
                                continue;
                            }
                        }

                        var addEq = $"\"{varName}\"={targetRhs}";
                        if (!newAdds!.ContainsKey(varName))
                            newAdds[varName] = addEq;
                    }
                }

                // ---------------------------
                // 5) Add queued equations at the end (ONLY if allowAdds=true)
                // ---------------------------
                if (allowAdds && newAdds is not null && newAdds.Count > 0)
                {
                    foreach (var add in newAdds.Values)
                    {
                        try
                        {
                            _ = eqMgr.Add3(
                                -1,
                                add,
                                true,
                                (int)swInConfigurationOpts_e.swThisConfiguration,
                                null);
                            added++;
                        }
                        catch
                        {
                            try
                            {
                                eqMgr.Add2(-1, add, true);
                                added++;
                            }
                            catch
                            {
                                // Ignore add failure
                            }
                        }
                    }
                }

                log?.Invoke(
                    $"EquationUpsert.BatchUpsertGlobals: processed={updates.Count}, updated={updated}, added={added}, skippedMissing={skippedMissing}, skippedAddByGroup={skippedAddByGroup}, allowAdds={allowAdds}");
            }
            finally
            {
                // Restore flags
                try { if (hasPrevSolve) eqMgr.AutomaticSolveOrder = prevAutoSolve; } catch { }
                try { if (hasPrevRebuild) eqMgr.AutomaticRebuild = prevAutoRebuild; } catch { }
            }
        }

        // --------------------------- helpers ---------------------------

        private static void LogSuppressedGroups(IDictionary<string, bool>? suppressedGroups, Action<string>? log)
        {
            if (log is null) return;

            if (suppressedGroups is null)
            {
                log("[EquationUpsert] suppressedGroups: <null> (no __suppressedGroups provided)");
                return;
            }

            if (suppressedGroups.Count == 0)
            {
                log("[EquationUpsert] suppressedGroups: <empty>");
                return;
            }

            log($"[EquationUpsert] suppressedGroups: count={suppressedGroups.Count}");
            foreach (var kv in suppressedGroups.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                log($"[EquationUpsert] suppressedGroups[{kv.Key}] = {kv.Value}");
        }

        private static string BuildTargetRhsMacroAligned(object? value, out bool isNumeric, out double numeric)
        {
            isNumeric = false;
            numeric = 0;

            if (value is null)
                return "0";

            switch (value)
            {
                case double d:
                    isNumeric = true; numeric = d;
                    return ToSwNumber(d);

                case float f:
                    isNumeric = true; numeric = f;
                    return ToSwNumber(f);

                case int i:
                    isNumeric = true; numeric = i;
                    return ToSwNumber(i);

                case long l:
                    isNumeric = true; numeric = l;
                    return ToSwNumber(l);

                case decimal m:
                    isNumeric = true; numeric = (double)m;
                    return ToSwNumber((double)m);
            }

            var raw = value.ToString() ?? string.Empty;
            var t = NormalizeTargetRhs(raw);

            if (TryParseDoubleString(t, out var parsed))
            {
                isNumeric = true;
                numeric = parsed;
                return ToSwNumber(parsed); // unitless numeric
            }

            return t.Length == 0 ? "0" : t;
        }

        private static string NormalizeTargetRhs(string rhs)
        {
            if (rhs is null) return string.Empty;

            var t = rhs.Trim();

            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                t = t[1..^1].Trim();

            t = t.Replace(",", ".");
            return t;
        }

        private static string ToSwNumber(double d)
            => d.ToString("0.###############", CultureInfo.InvariantCulture);

        private static bool TryParseValueFromEquation(string eq, out double outVal)
        {
            outVal = 0;
            if (string.IsNullOrWhiteSpace(eq)) return false;

            int p = eq.IndexOf('=');
            if (p < 0) return false;

            var rhs = eq[(p + 1)..].Trim();

            if (rhs.Length >= 2 && rhs[0] == '"' && rhs[^1] == '"')
                rhs = rhs[1..^1];

            rhs = rhs.Trim().Replace(",", ".");

            if (double.TryParse(rhs, NumberStyles.Any, CultureInfo.InvariantCulture, out outVal))
                return true;

            outVal = LeadingDouble(rhs, out var ok);
            return ok;
        }

        private static bool TryParseDoubleString(string s, out double outD)
        {
            outD = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var t = s.Trim();

            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                t = t[1..^1].Trim();

            t = t.Replace(",", ".");

            if (double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out outD))
                return true;

            outD = LeadingDouble(t, out var ok);
            return ok;
        }

        private static string NormalizeEquationString(string varName, string eq)
        {
            if (TryParseValueFromEquation(eq, out var v))
                return $"\"{varName}\"={ToSwNumber(v)}";

            int p = eq.IndexOf('=');
            if (p < 0) return eq.Trim();

            var rhs = eq[(p + 1)..].Trim();
            return $"\"{varName}\"={rhs}";
        }

        private static double LeadingDouble(string s, out bool ok)
        {
            ok = false;
            if (string.IsNullOrWhiteSpace(s)) return 0;

            int i = 0;
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;

            int start = i;
            bool seenDigit = false;

            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;

            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsDigit(c)) { seenDigit = true; i++; continue; }
                if (c == '.' || c == ',') { i++; continue; }
                break;
            }

            if (!seenDigit) return 0;

            var token = s.Substring(start, i - start).Replace(",", ".");
            if (double.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                ok = true;
                return d;
            }

            return 0;
        }
    }
}
