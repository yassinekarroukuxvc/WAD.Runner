// PartAutomation/Execution/PartAutomationService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.PartAutomation.Interfaces;
using WAD.Runner.PartAutomation.Rules;
using WAD.Runner.PartAutomation.SolidWorks;
using WAD.Runner.PartAutomation.SolidWorks.Interop;

namespace WAD.Runner.PartAutomation.Execution
{
    /// <summary>
    /// Macro-equivalent PartAutomation orchestrator:
    ///  - Plan features (no COM side effects)
    ///  - Apply plan (OFF then ON) WITHOUT rebuild
    ///  - Upsert globals with EquationMgr.AutomaticSolveOrder/AutomaticRebuild disabled
    ///  - Rebuild (single sync point)
    ///  - Apply tolerances
    ///  - Rebuild (final sync point)
    /// </summary>
    public sealed class PartAutomationService : IPartAutomationService
    {
        private SldWorks? _sw;
        private PartEditor? _editor;

        public ModelDoc2 Model
        {
            get
            {
                EnsureAttached();
                return _editor!.Model;
            }
        }

        public void Attach(SldWorks swApp)
        {
            _sw = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _editor = new PartEditor(_sw);
            Logger.Info("[PartAutomationService] Attached to SolidWorks.");
        }

        public void OpenPart(string partPath)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] OpenPart → '{partPath}'");
            _editor!.Open(partPath);
            Logger.Success("[PartAutomationService] Part opened.");
        }

        public void ActivateConfiguration(WedgeSubclass subclass, DrawingType drawingType)
        {
            EnsureAttached();

            var cfg =
                subclass switch
                {
                    WedgeSubclass.PGB when drawingType == DrawingType.Overlay => "PGB_OVERLAY",
                    WedgeSubclass.PGB when drawingType == DrawingType.Customer => "PGB_CUSTOMER_DRAWING",
                    WedgeSubclass.PGB => "PGB_DRAWING",
                    _ when drawingType == DrawingType.Overlay => "FG_OVERLAY",
                    _ when drawingType == DrawingType.Customer => "FG_CUSTOMER_DRAWING",
                    _ => "FG_PRODUCTION_DRAWING"
                };

            Logger.Info($"[PartAutomationService] ActivateConfiguration → {cfg}");
            _editor!.ActivateConfiguration(cfg);
            Logger.Success("[PartAutomationService] Configuration activated.");
        }

        /// <summary>
        /// WARNING:
        /// This imports from an external equation file and may cause linkage to that file (template path problem).
        /// Prefer the macro pipeline (RunMacroStyle) which uses EquationUpsert (push values into EquationMgr)
        /// instead of linking the model to an external equations.txt.
        /// </summary>
        [Obsolete("Prefer RunMacroStyle(...) + UpsertGlobalsFromEffectiveDims(...) instead of importing external equations files.")]
        public void UpdateEquations(string equationFilePath)
        {
            EnsureAttached();
            Logger.Info($"[PartAutomationService] UpdateEquations (obsolete) → '{equationFilePath}'");
            _editor!.UpdateEquations(equationFilePath);
            Logger.Success("[PartAutomationService] Equations updated (obsolete path).");
        }

        /// <summary>
        /// Single macro-equivalent pipeline.
        /// Centralized order:
        ///   Apply plan → Upsert globals → Rebuild → Tolerances → Rebuild
        /// Notes:
        /// - Plan application is done WITHOUT rebuild in PartEditor.ApplyFeaturePlan.
        /// - EquationMgr.AutomaticSolveOrder/AutomaticRebuild are disabled during upsert.
        /// </summary>
        public FeatureTogglePlan RunMacroStyle(
            WedgeType wedgeType,
            WedgeData wedge,
            DrawingType drawingType,
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
            IEnumerable<DimensionKey> toleranceKeys,
            double eps = 1e-6)
        {
            EnsureAttached();
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));

            Logger.Info($"[PartAutomationService] RunMacroStyle → wedgeType={wedgeType}, drawingType={drawingType}");

            var editor = _editor!;

            // 0) Properties / text first (macro-style)
            var engraving = ResolveEngravingText(wedge);
            Logger.Info($"[PartAutomationService] Engraving text → '{engraving ?? "(null)"}'");
            editor.SetEngraving(engraving);

            // 1) Plan (pure) + Apply plan (NO rebuild inside apply)
            var plan = BuildPlan(wedgeType, wedge, drawingType);

            if (plan.Notes.Count > 0)
                Logger.Info("[PartAutomationService] Plan notes: " + string.Join(" | ", plan.Notes));

            editor.ApplyFeaturePlan(plan, msg => Logger.Info("[PlanApply] " + msg));

            // 2) Upsert globals (NO rebuild during upsert)
            UpsertGlobalsFromEffectiveDims(effectiveDims, wedge, drawingType, plan, eps);

            // 3) Rebuild (sync point after feature state + globals)
            editor.Rebuild();

            // 4) Apply tolerances (NO rebuild inside ApplyLengthTolerances)
            ApplyLengthTolerances(wedge, toleranceKeys ?? Array.Empty<DimensionKey>());

            // 5) Rebuild (final sync point)
            editor.Rebuild();

            Logger.Success("[PartAutomationService] RunMacroStyle → done");
            return plan;
        }

        public void UpsertGlobalsFromEffectiveDims(
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
            WedgeData wedge,
            DrawingType drawingType,
            double eps = 1e-6)
        {
            UpsertGlobalsFromEffectiveDims(effectiveDims, wedge, drawingType, plan: null, eps);
        }

        /// <summary>
        /// New overload: includes FeatureTogglePlan so EquationUpsert can use __suppressedGroups.
        /// </summary>
        public void UpsertGlobalsFromEffectiveDims(
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
            WedgeData wedge,
            DrawingType drawingType,
            FeatureTogglePlan? plan,
            double eps = 1e-6)
        {
            EnsureAttached();
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var model = _editor!.Model;
            var eqMgr = (EquationMgr)model.GetEquationMgr();

            var prevSolve = eqMgr.AutomaticSolveOrder;
            var prevRebuild = eqMgr.AutomaticRebuild;

            var updates = BuildGlobalsUpdateMap(effectiveDims, wedge, drawingType, plan?.SuppressedGroups);

            try
            {
                // REQUIRED: disable during upsert
                eqMgr.AutomaticSolveOrder = false;
                eqMgr.AutomaticRebuild = false;

                Logger.Info($"[PartAutomationService] UpsertGlobalsFromEffectiveDims → count={updates.Count}, drawingType={drawingType}");
                EquationUpsert.BatchUpsertGlobals(
                    eqMgr,
                    updates,
                    eps,
                    msg => Logger.Info("[EquationUpsert] " + msg),
                    allowAdds: false);

                Logger.Success("[PartAutomationService] Globals upsert done.");
            }
            finally
            {
                eqMgr.AutomaticSolveOrder = prevSolve;
                eqMgr.AutomaticRebuild = prevRebuild;
            }
        }

        public void EnsureAllEquationsExist(WedgeData wedge)
        {
            EnsureAttached();
            Logger.Info("[PartAutomationService] EnsureAllEquationsExist → start");
            EquationUpdater.EnsureAllEquationsExist(_editor!.Model, wedge);
            Logger.Success("[PartAutomationService] EnsureAllEquationsExist → done");
        }

        public void ApplyLengthTolerances(WedgeData wedge, IEnumerable<DimensionKey> keys)
        {
            EnsureAttached();

            var list = keys?.Select(k => k.Value).ToArray() ?? Array.Empty<string>();
            Logger.Info($"[PartAutomationService] ApplyLengthTolerances → [{string.Join(", ", list)}]");

            _editor!.ApplyLengthTolerances(wedge, keys ?? Array.Empty<DimensionKey>());
            Logger.Success("[PartAutomationService] Tolerances applied.");
        }

        [Obsolete("Use RunMacroStyle(...) to avoid rule files toggling/rebuilding directly.")]
        public void ApplyPostRules(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType)
        {
            EnsureAttached();
            Logger.Warn("[PartAutomationService] ApplyPostRules is obsolete. Use RunMacroStyle(...) instead.");

            var editor = _editor!;
            editor.SetEngraving(ResolveEngravingText(wedge));

            switch (wedgeType)
            {
                case WedgeType.CKVD:
                    BasicPartRules.ApplyCkvdRules(editor, wedge, drawingType);
                    break;
                case WedgeType.COB:
                    BasicPartRules.ApplyCobRules(editor, wedge, drawingType);
                    break;
                case WedgeType.OSG7:
                    BasicPartRules.ApplyOsg7Rules(editor, wedge, drawingType);
                    break;
                default:
                    if (drawingType is DrawingType.Production or DrawingType.Customer)
                        BasicPartRules.ApplyEngravingToggle(editor);
                    editor.Rebuild();
                    break;
            }
        }

        public void RebuildPart()
        {
            EnsureAttached();
            Logger.Info("[PartAutomationService] RebuildPart");
            _editor!.Rebuild();
            Logger.Success("[PartAutomationService] Rebuild complete.");
        }

        public void SaveAndClose()
        {
            Logger.Info("[PartAutomationService] SaveAndClose → start");
            try
            {
                _editor?.Save();
                _editor?.Close();
                Logger.Success("[PartAutomationService] Part saved and closed.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[PartAutomationService] Save/Close warning: {ex.Message}");
            }
            finally
            {
                _editor = null;
                _sw = null;
                Logger.Info("[PartAutomationService] Cleanup complete.");
            }
        }

        private void EnsureAttached()
        {
            if (_sw is null || _editor is null)
                throw new InvalidOperationException("Not attached. Call Attach(swApp) first.");
        }

        // --------------------------- plan selection ---------------------------

        private static FeatureTogglePlan BuildPlan(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType)
        {
            return wedgeType switch
            {
                WedgeType.OSG7 => OSG7FeaturePlanner.Build(wedge, drawingType),
                WedgeType.COB => COBFeaturePlanner.Build(wedge, drawingType),
                WedgeType.CKVD => new FeatureTogglePlan(),
                _ => new FeatureTogglePlan(notes: new[] { $"No planner for wedgeType={wedgeType}; plan empty." })
            };
        }

        private static string? ResolveEngravingText(WedgeData wedge)
        {
            if (!string.IsNullOrWhiteSpace(wedge.Marking?.Text))
                return wedge.Marking!.Text;

            if (wedge.Properties != null &&
                wedge.Properties.TryGetValue("Marking", out var s) &&
                !string.IsNullOrWhiteSpace(s))
                return s;

            return null;
        }

        // --------------------------- globals build helpers ---------------------------

        private static IDictionary<string, object> BuildGlobalsUpdateMap(
            IReadOnlyDictionary<DimensionKey, WAD.Runner.DataManagement.Domain.Dimensions.Dimension> effectiveDims,
            WedgeData wedge,
            DrawingType drawingType,
            IReadOnlyDictionary<string, bool>? suppressedGroups)
        {
            var updates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // Provide suppression map to EquationUpsert (macro behavior)
            if (suppressedGroups != null && suppressedGroups.Count > 0)
                updates["__suppressedGroups"] = new Dictionary<string, bool>(suppressedGroups, StringComparer.OrdinalIgnoreCase);

            // 1) All dimension globals (numeric, NO units)
            foreach (var kv in effectiveDims)
            {
                var key = kv.Key.Value;
                var dim = kv.Value;
                if (string.IsNullOrWhiteSpace(key) || dim is null) continue;

                if (string.Equals(key, "EngravingStart", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (dim.Nominal.Unit == UnitKind.Degree)
                {
                    updates[key] = (double)dim.Nominal.AsDeg();
                }
                else if (dim.Nominal.Unit == UnitKind.Millimeter)
                {
                    updates[key] = (double)dim.Nominal.AsMm();
                }
                else
                {
                    // decimal (non-nullable) -> always numeric
                    updates[key] = (double)dim.Nominal.Value;
                }
            }

            // 2) EngravingStart (numeric mm)
            updates["EngravingStart"] = ComputeEngravingStartMm(wedge);

            // 3) Overlay globals (numeric)
            if (drawingType == DrawingType.Overlay)
            {
                var overlayMag = ComputeOverlayMagnificationFromFl(wedge);
                var overlayScale = GetOverlayModelViewScaleDecimal(overlayMag);

                updates["overlay_calibration1"] = overlayMag;
                updates["scale"] = overlayScale;
                updates["TL"] = 30.0; // your overlay TL override
            }

            // 4) COB funnel_gap (numeric mm)
            if (IsCobFunnelGapInputsPresent(wedge))
            {
                var gapMm = ComputeCobFunnelGapMm(wedge);
                updates["funnel_gap"] = gapMm;
            }

            return updates;
        }

        private static double ComputeEngravingStartMm(WedgeData wedge)
        {
            if (wedge?.KValue is not null)
                return (double)wedge.KValue.ValueMm.AsMm();

            if (wedge?.Dimensions != null &&
                wedge.Dimensions.TryGetValue(DimensionKey.From("TL"), out var tl) &&
                tl is not null &&
                tl.Nominal.Unit == UnitKind.Millimeter)
            {
                return (double)tl.Nominal.AsMm() * 0.40;
            }

            return 0.0;
        }

        private static double ComputeOverlayMagnificationFromFl(WedgeData wedge)
        {
            const double defaultMag = 100.0;

            if (wedge?.Dimensions is null)
                return defaultMag;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From("FL"), out var flDim) ||
                flDim is null ||
                flDim.Nominal.Unit != UnitKind.Millimeter)
            {
                return defaultMag;
            }

            double fl = (double)flDim.Nominal.AsMm();
            if (double.IsNaN(fl) || double.IsInfinity(fl) || fl <= 0.0)
                return defaultMag;

            if (fl <= 0.3403) return 400;
            if (fl <= 0.4572) return 300;
            if (fl <= 0.6908) return 200;
            if (fl <= 1.3766) return 100;
            return 100;
        }

        private static double GetOverlayModelViewScaleDecimal(double overlayMagnification)
        {
            int token = NormalizeScalingToken(overlayMagnification);
            return token switch
            {
                100 => 60.8,
                200 => 122.7,
                300 => 183.0,
                400 => 246.0,
                _ => 60.8
            };
        }

        private static int NormalizeScalingToken(object? overlayScaling)
        {
            if (overlayScaling is null) return 100;

            if (double.TryParse(overlayScaling.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                if (d < 10.0) return (int)Math.Round(d * 100.0);
                return (int)Math.Round(d);
            }

            var s = overlayScaling.ToString()?.Trim() ?? "";
            s = s.ToUpperInvariant().Replace(" ", "");
            if (s.StartsWith("X")) s = s[1..];
            if (s.EndsWith("X")) s = s[..^1];
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 100;
        }

        // -------- COB funnel_gap --------

        private static bool IsCobFunnelGapInputsPresent(WedgeData wedge)
        {
            if (wedge?.Dimensions is null) return false;
            return wedge.Dimensions.ContainsKey(DimensionKey.From("FNO")) &&
                   wedge.Dimensions.ContainsKey(DimensionKey.From("FNA")) &&
                   wedge.Dimensions.ContainsKey(DimensionKey.From("BA")) &&
                   wedge.Dimensions.ContainsKey(DimensionKey.From("RA")) &&
                   wedge.Dimensions.ContainsKey(DimensionKey.From("FND")) &&
                   wedge.Dimensions.ContainsKey(DimensionKey.From("H"));
        }

        private static double ComputeCobFunnelGapMm(WedgeData wedge)
        {
            const double DefaultGapMm = 0.0003;

            if (!TryGetMm(wedge, "FNO", out var fno) || fno <= 0)
                return DefaultGapMm;

            if (!TryGetDeg(wedge, "FNA", out var fna) ||
                !TryGetDeg(wedge, "BA", out var ba) ||
                !TryGetDeg(wedge, "RA", out var ra) ||
                !TryGetMm(wedge, "FND", out var fnd) ||
                !TryGetMm(wedge, "H", out var h))
                return DefaultGapMm;

            double alpha = (fna / 2.0) * Math.PI / 180.0;
            double k = (ba + ra) * Math.PI / 180.0;

            double t2 = Math.Tan(alpha) * Math.Tan(alpha) * Math.Tan(k) * Math.Tan(k);
            double frac = (1 - t2) / (1 + t2);

            double inside = fnd * frac - h;
            double denom = 2.0 * Math.Sin(alpha);
            if (Math.Abs(denom) < 1e-12) return DefaultGapMm;

            return inside / denom;
        }

        private static bool TryGetMm(WedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != UnitKind.Millimeter)
                return false;

            value = (double)dim.Nominal.AsMm();
            return true;
        }

        private static bool TryGetDeg(WedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;

            if (!wedge.Dimensions.TryGetValue(DimensionKey.From(key), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != UnitKind.Degree)
                return false;

            value = (double)dim.Nominal.AsDeg();
            return true;
        }
    }
}
