using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using WAD.Runner.Application;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomWedgeType = WAD.Runner.DataManagement.Domain.Wedge.WedgeType;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    internal sealed class EquationUpdatePlan
    {
        public EquationUpdatePlan(
            Dictionary<string, DomDim> dimensionsByKey,
            Dictionary<string, string> managedEquations,
            HashSet<string> zeroProvidedKeys,
            HashSet<string> missingKeysToZero,
            bool writeZeros,
            bool missingDbKeysAsZero)
        {
            DimensionsByKey = dimensionsByKey;
            ManagedEquations = managedEquations;
            ZeroProvidedKeys = zeroProvidedKeys;
            MissingKeysToZero = missingKeysToZero;
            WriteZeros = writeZeros;
            MissingDbKeysAsZero = missingDbKeysAsZero;
        }

        public Dictionary<string, DomDim> DimensionsByKey { get; }
        public Dictionary<string, string> ManagedEquations { get; }
        public HashSet<string> ZeroProvidedKeys { get; }
        public HashSet<string> MissingKeysToZero { get; }
        public bool WriteZeros { get; }
        public bool MissingDbKeysAsZero { get; }
    }

    internal static class EquationUpdaterPlanner
    {
        private sealed record WritePolicy(bool WriteZeros, bool MissingDbKeysAsZero)
        {
            public static WritePolicy For(DomWedgeType wedgeType)
            {
                bool isCkvd = wedgeType == DomWedgeType.CKVD;
                return new WritePolicy(WriteZeros: isCkvd, MissingDbKeysAsZero: isCkvd);
            }
        }

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static EquationUpdatePlan Build(
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            var policy = WritePolicy.For(wedgeType);
            var dimensionsByKey = BuildDimensionsByKey(effectiveDims);
            var managedEquations = BuildManagedEquations(wedge, wedgeType, drawingType);
            var zeroProvidedKeys = policy.WriteZeros
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : CollectZeroKeys(dimensionsByKey);
            var missingKeysToZero = policy.MissingDbKeysAsZero
                ? new HashSet<string>(EquationUpdaterCatalog.CkvdDbDrivenKeys, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return new EquationUpdatePlan(
                dimensionsByKey,
                managedEquations,
                zeroProvidedKeys,
                missingKeysToZero,
                policy.WriteZeros,
                policy.MissingDbKeysAsZero);
        }

        private static Dictionary<string, DomDim> BuildDimensionsByKey(
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            var result = new Dictionary<string, DomDim>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in effectiveDims)
            {
                string key = kv.Key.Value;

                if (EquationUpdaterCatalog.DbToModelKeyAlias.TryGetValue(key, out var alias))
                {
                    key = alias;
                    Logger.Info($"[EquationUpdater] Key alias: '{kv.Key.Value}' → '{alias}'");
                }

                result[key] = kv.Value;
            }

            return result;
        }

        private static Dictionary<string, string> BuildManagedEquations(
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [EquationUpdaterCatalog.EquationNames.EngravingStart] = BuildEngravingStartLine(wedge)
            };

            if (drawingType == DomDrawingType.Overlay)
            {
                double mag = ComputeOverlayMagnification(wedge, wedgeType);
                double scale = GetOverlayModelViewScaleDecimal(mag);
                string magStr = F(mag);

                Logger.Info($"[EquationUpdater] Overlay magnification resolved to {magStr} for wedgeType={wedgeType}");

                map[EquationUpdaterCatalog.EquationNames.OverlayCalibration1] =
                    $"\"{EquationUpdaterCatalog.EquationNames.OverlayCalibration1}\" = {magStr}";
                map[EquationUpdaterCatalog.EquationNames.Scale] =
                    $"\"{EquationUpdaterCatalog.EquationNames.Scale}\" = {F(scale)}";
                map["TL"] = $"\"TL\" = {F(30)}mm";
            }

            if (wedgeType != DomWedgeType.CKVD)
            {
                double gapMm = ComputeFunnelGapMm(wedge);
                map[EquationUpdaterCatalog.EquationNames.FunnelGap] =
                    $"\"{EquationUpdaterCatalog.EquationNames.FunnelGap}\" = {F(gapMm)}mm";
                Logger.Info($"[EquationUpdater] {wedgeType} funnel_gap = {F(gapMm)} mm");
            }

            if (ShouldManageNonStdCutEquation(wedgeType, drawingType))
            {
                double cutMm = ComputeNonStdCutMm(wedge, wedgeType, drawingType);
                map[EquationUpdaterCatalog.EquationNames.NonStdCut] =
                    $"\"{EquationUpdaterCatalog.EquationNames.NonStdCut}\" = {F(cutMm)}mm";
                Logger.Info($"[EquationUpdater] {wedgeType} non_std_cut = {F(cutMm)} mm");
            }

            return map;
        }

        private static bool ShouldManageNonStdCutEquation(
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            return wedgeType is DomWedgeType.COB or DomWedgeType.UTUS or DomWedgeType.FP;
        }

        private static HashSet<string> CollectZeroKeys(Dictionary<string, DomDim> dimensionsByKey)
        {
            var zeros = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, dim) in dimensionsByKey)
            {
                try
                {
                    double value = dim.Nominal.Unit == DomUnitKind.Degree
                        ? (double)dim.Nominal.AsDeg()
                        : (double)dim.Nominal.AsMm();

                    if (Math.Abs(value) < 1e-12)
                        zeros.Add(key);
                }
                catch
                {
                    zeros.Add(key);
                }
            }

            return zeros;
        }

        private static string BuildEngravingStartLine(DomWedgeData wedge)
        {
            double engravingMm = 0.0;

            if (wedge.KValue is not null)
            {
                engravingMm = (double)wedge.KValue.ValueMm.AsMm();
            }
            else if (wedge.Dimensions.TryGetValue(DomDimKey.From("TL"), out var tl)
                     && tl?.Nominal.Unit == DomUnitKind.Millimeter)
            {
                engravingMm = (double)tl.Nominal.AsMm() * 0.40;
            }

            return $"\"{EquationUpdaterCatalog.EquationNames.EngravingStart}\" = {F(engravingMm)}mm";
        }

        private static double ComputeFunnelGapMm(DomWedgeData wedge)
        {
            const double DefaultMm = 0.00762; // 0.0003 inch

            if (!TryGetMm(wedge, "FNO", out var fno) || fno <= 0.0) return DefaultMm;
            if (!TryGetDeg(wedge, "FNA", out var fna)) return DefaultMm;
            if (!TryGetDeg(wedge, "BA", out var ba)) return DefaultMm;
            if (!TryGetDeg(wedge, "RA", out var ra)) return DefaultMm;
            if (!TryGetMm(wedge, "H", out var h)) return DefaultMm;

            double alpha = (fna / 2.0) * Math.PI / 180.0;
            double k = (ba + ra) * Math.PI / 180.0;
            double sinAlpha = Math.Sin(alpha);

            if (Math.Abs(sinAlpha) < 1e-12) return DefaultMm;

            double tanA = Math.Tan(alpha);
            double tanK = Math.Tan(k);
            double denominator = 1.0 + (tanA * tanK);

            if (Math.Abs(denominator) < 1e-12) return DefaultMm;

            double fraction = (1.0 - (tanA * tanA) * (tanK * tanK)) / denominator;
            double bracket = (fno * fraction) - h;
            double funnelGap = bracket / (2.0 * sinAlpha);

            if (double.IsNaN(funnelGap) || double.IsInfinity(funnelGap) || funnelGap <= 0.0)
                return DefaultMm;

            return funnelGap;
        }

        private static double ComputeNonStdCutMm(
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            double rawMm = wedgeType == DomWedgeType.UTUS
                ? ComputeUtusNonStdCutMm(wedge)
                : ComputeCobLikeNonStdCutMm(wedge);

            // Only compress/clamp for Overlay.
            if (drawingType != DomDrawingType.Overlay)
                return rawMm;

            return ComputeOverlaySafeNonStdCutMm(rawMm, wedgeType);
        }

        private static double ComputeOverlaySafeNonStdCutMm(
            double rawMm,
            DomWedgeType wedgeType)
        {
            if (rawMm <= 0.0)
                return 0.0;

            // Overlay TL is already forced to 30 mm in this planner,
            // so we use that as a stable layout reference.
            const double OverlayTlMm = 30.0;

            // Tune these if needed after testing.
            double softCapMm = OverlayTlMm * 0.012;   // 0.36 mm
            double hardCapMm = OverlayTlMm * 0.015;   // 0.90 mm
            const double CompressionFactor = 0.25;

            if (rawMm <= softCapMm)
            {
                Logger.Info(
                    $"[EquationUpdater] {wedgeType} overlay non_std_cut kept raw = {F(rawMm)} mm");
                return rawMm;
            }

            double compressedMm = softCapMm + ((rawMm - softCapMm) * CompressionFactor);
            double finalMm = Math.Min(compressedMm, hardCapMm);

            Logger.Warn(
                $"[EquationUpdater] {wedgeType} overlay non_std_cut compressed from {F(rawMm)} mm to {F(finalMm)} mm " +
                $"(softCap={F(softCapMm)} mm, hardCap={F(hardCapMm)} mm, factor={F(CompressionFactor)})");

            return finalMm;
        }

        /// <summary>
        /// COB / FP behavior:
        /// non_std_cut = VR_MAX + VRR_MAX + (VR_MAX * 0.20)
        /// </summary>
        private static double ComputeCobLikeNonStdCutMm(DomWedgeData wedge)
        {
            const double ExtraClearanceFactor = 0.20;

            double vrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VR_MAX", baseKey: "VR", out var resolvedVrMax)
                ? resolvedVrMax
                : 0.0;

            double vrrMax = TryGetMaxLikeMm(wedge, explicitMaxKey: "VRR_MAX", baseKey: "VRR", out var resolvedVrrMax)
                ? resolvedVrrMax
                : 0.0;

            double clearance = vrMax * ExtraClearanceFactor;
            double result = vrMax + vrrMax + clearance;

            Logger.Info(
                $"[EquationUpdater] COB-like non_std_cut = VR_MAX({F(vrMax)}) + VRR_MAX({F(vrrMax)}) + clearance({F(clearance)}) = {F(result)} mm");

            return result;
        }

        /// <summary>
        /// UT/US behavior:
        /// non_std_cut = VR
        ///
        /// This intentionally uses the nominal VR value, because UT/US should
        /// display/use VR directly instead of the COB-like VR + VRR + clearance formula.
        /// </summary>
        private static double ComputeUtusNonStdCutMm(DomWedgeData wedge)
        {
            if (!TryGetMm(wedge, "VR", out var vr) || vr <= 0.0)
            {
                Logger.Info("[EquationUpdater] UTUS non_std_cut = VR is missing/zero → 0 mm");
                return 0.0;
            }

            Logger.Info($"[EquationUpdater] UTUS non_std_cut = VR({F(vr)}) mm");
            return vr;
        }

        private static bool TryGetMaxLikeMm(
            DomWedgeData wedge,
            string explicitMaxKey,
            string baseKey,
            out double value)
        {
            value = 0.0;

            if (TryGetMm(wedge, explicitMaxKey, out var explicitMax))
            {
                value = explicitMax;
                Logger.Info($"[EquationUpdater] Using explicit {explicitMaxKey} = {F(value)} mm");
                return true;
            }

            if (wedge?.Dimensions is null)
                return false;

            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(baseKey), out var dim) || dim is null)
                return false;

            if (dim.Nominal.Unit != DomUnitKind.Millimeter)
                return false;

            double nominal = (double)dim.Nominal.AsMm();
            double upperTolerance = (double)dim.Tol.Upper.Value;

            value = nominal + upperTolerance;

            Logger.Info(
                $"[EquationUpdater] Derived {explicitMaxKey} from {baseKey}: NOM({F(nominal)}) + UTOL({F(upperTolerance)}) = {F(value)} mm");

            return true;
        }

        private static double ComputeOverlayMagnification(DomWedgeData wedge, DomWedgeType wedgeType)
        {
            string dimensionKey = wedgeType == DomWedgeType.CKVD ? "FL" : "T";
            return ComputeOverlayMagnificationFromDimension(wedge, dimensionKey, wedgeType);
        }

        private static double ComputeOverlayMagnificationFromDimension(
            DomWedgeData wedge,
            string dimensionKey,
            DomWedgeType wedgeType)
        {
            const double Default = 100.0;

            if (!TryGetMm(wedge, dimensionKey, out var value) || value <= 0.0)
            {
                Logger.Warn(
                    $"[EquationUpdater] Overlay mag source '{dimensionKey}' missing/invalid for {wedgeType}. Using default {Default}.");
                return Default;
            }

            Logger.Info($"[EquationUpdater] Overlay mag source '{dimensionKey}' = {F(value)} mm for {wedgeType}");

            if (value <= 0.3403) return 400;
            if (value <= 0.4572) return 300;
            if (value <= 0.6908) return 200;
            return 100;
        }

        private static double GetOverlayModelViewScaleDecimal(double magnification)
            => (int)Math.Round(magnification) switch
            {
                400 => 246.0,
                300 => 183.0,
                200 => 122.7,
                _ => 60.8
            };

        private static bool TryGetMm(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null) return false;
            if (dim.Nominal.Unit != DomUnitKind.Millimeter) return false;
            value = (double)dim.Nominal.AsMm();
            return true;
        }

        private static bool TryGetDeg(DomWedgeData wedge, string key, out double value)
        {
            value = 0;
            if (wedge?.Dimensions is null) return false;
            if (!wedge.Dimensions.TryGetValue(DomDimKey.From(key), out var dim) || dim is null) return false;
            if (dim.Nominal.Unit != DomUnitKind.Degree) return false;
            value = (double)dim.Nominal.AsDeg();
            return true;
        }
    }
}