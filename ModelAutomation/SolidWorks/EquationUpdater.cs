using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;
using DomDimKey = WAD.Runner.DataManagement.Domain.Dimensions.DimensionKey;
using DomDrawingType = WAD.Runner.DataManagement.Domain.Wedge.DrawingType;
using DomUnitKind = WAD.Runner.DataManagement.Domain.Units.UnitKind;
using DomWedgeData = WAD.Runner.DataManagement.Domain.Wedge.WedgeData;
using DomWedgeType = WAD.Runner.DataManagement.Domain.Wedge.WedgeType;

namespace WAD.Runner.ModelAutomation.SolidWorks
{
    public static class EquationUpdater
    {
        private static readonly Regex LineRx =
            new Regex(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

        private static string F(double v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static void UpdateEquationFile(
            string equationFilePath,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            UpdateEquationFile(equationFilePath, wedge.Dimensions, wedge, wedgeType, drawingType);
        }

        public static void UpdateEquationFile(
            string equationFilePath,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType)
        {
            if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
                throw new FileNotFoundException("Equation file not found.", equationFilePath);
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));

            DumpEffectiveDims("EquationUpdater.UpdateEquationFile", effectiveDims);

            var plan = EquationUpdaterPlanner.Build(effectiveDims, wedge, wedgeType, drawingType);

            Logger.Info(
                $"[EquationUpdater] UpdateEquationFile → '{equationFilePath}', " +
                $"drawingType={drawingType}, wedgeType={wedgeType}, " +
                $"writeZeros={plan.WriteZeros}, missingAsZero={plan.MissingDbKeysAsZero}");

            var encoding = GetFileEncoding(equationFilePath);
            var raw = File.ReadAllText(equationFilePath, encoding);
            var newline = DetectNewline(raw);
            var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var output = new List<string>(lines.Count + 64);

            int rewritten = 0;

            foreach (var line in lines)
            {
                var match = LineRx.Match(line);
                if (!match.Success)
                {
                    output.Add(line);
                    continue;
                }

                var key = match.Groups["key"].Value;

                if (plan.ManagedEquations.TryGetValue(key, out var managedLine))
                {
                    output.Add(managedLine);
                    rewritten++;
                    continue;
                }

                if (plan.DimensionsByKey.TryGetValue(key, out var dim))
                {
                    if (plan.ZeroProvidedKeys.Contains(key))
                    {
                        output.Add(line); // keep template value for zeros
                        continue;
                    }

                    WriteDimensionLine(output, key, dim);
                    rewritten++;
                    continue;
                }

                if (plan.MissingDbKeysAsZero && plan.MissingKeysToZero.Contains(key))
                {
                    output.Add(MakeZeroLine(key, line));
                    rewritten++;
                    Logger.Info($"[EquationUpdater] CKVD missing key → zero: {key}");
                    continue;
                }

                output.Add(line);
            }

            int appended = 0;

            foreach (var (key, line) in plan.ManagedEquations)
            {
                if (LineExists(output, key))
                    continue;

                output.Add(line);
                appended++;
            }

            foreach (var (key, dim) in plan.DimensionsByKey)
            {
                if (plan.ManagedEquations.ContainsKey(key))
                    continue;
                if (plan.ZeroProvidedKeys.Contains(key))
                    continue;
                if (LineExists(output, key))
                    continue;

                WriteDimensionLine(output, key, dim);
                appended++;
            }

            if (plan.MissingDbKeysAsZero)
            {
                foreach (var key in plan.MissingKeysToZero)
                {
                    if (LineExists(output, key))
                        continue;

                    output.Add($"\"{key}\" = 0{(EquationUpdaterCatalog.CkvdAngleKeys.Contains(key) ? "deg" : "mm")}");
                    appended++;
                    Logger.Info($"[EquationUpdater] CKVD missing line appended as zero: {key}");
                }
            }

            Logger.Info($"[EquationUpdater] Rewritten={rewritten}, Appended={appended}");
            File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
            Logger.Success($"[EquationUpdater] Equation file updated: {equationFilePath}");
        }

        /// <summary>
        /// Direct upsert into model EquationMgr (fallback / alternate path).
        /// No rebuild here — orchestrator owns the single rebuild at the end.
        /// </summary>
        public static void UpsertEquationsInModel(
            ModelDoc2 model,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims,
            DomWedgeData wedge,
            DomWedgeType wedgeType,
            DomDrawingType drawingType,
            bool rebuild = false)
        {
            if (model is null) throw new ArgumentNullException(nameof(model));
            if (effectiveDims is null) throw new ArgumentNullException(nameof(effectiveDims));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));

            var plan = EquationUpdaterPlanner.Build(effectiveDims, wedge, wedgeType, drawingType);

            var mgr = (EquationMgr)model.GetEquationMgr()
                ?? throw new InvalidOperationException("EquationMgr is null.");

            var index = BuildEquationIndex(mgr);
            int upserted = 0;
            int skippedZero = 0;

            foreach (var (key, dim) in plan.DimensionsByKey)
            {
                if (plan.ManagedEquations.ContainsKey(key))
                    continue;

                bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
                double value;

                try
                {
                    value = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
                }
                catch
                {
                    skippedZero++;
                    continue;
                }

                if (!plan.WriteZeros && Math.Abs(value) < 1e-12)
                {
                    skippedZero++;
                    continue;
                }

                UpsertEquation(mgr, index, key, $"\"{key}\" = {(isAngle ? $"{F(value)}deg" : $"{F(value)}mm")}");
                upserted++;
            }

            foreach (var (key, line) in plan.ManagedEquations)
            {
                UpsertEquation(mgr, index, key, line);
                upserted++;
            }

            if (rebuild)
                model.EditRebuild3();

            Logger.Success(
                $"[EquationUpdater] UpsertEquationsInModel → upserted={upserted}, " +
                $"skippedZero={skippedZero}, rebuild={rebuild}, " +
                $"wedgeType={wedgeType}, writeZeros={plan.WriteZeros}");
        }

        private static void WriteDimensionLine(List<string> sink, string key, DomDim dim)
        {
            bool isAngle = dim.Nominal.Unit == DomUnitKind.Degree;
            double value = (double)(isAngle ? dim.Nominal.AsDeg() : dim.Nominal.AsMm());
            sink.Add($"\"{key}\" = {F(value)}{(isAngle ? "deg" : "mm")}");
        }

        /// <summary>
        /// Preserves the unit (deg/mm/in) of the existing line when zeroing a CKVD key.
        /// </summary>
        private static string MakeZeroLine(string key, string existingLine)
        {
            string unit =
                existingLine.IndexOf("deg", StringComparison.OrdinalIgnoreCase) >= 0 ? "deg" :
                existingLine.IndexOf("mm", StringComparison.OrdinalIgnoreCase) >= 0 ? "mm" :
                existingLine.IndexOf("in", StringComparison.OrdinalIgnoreCase) >= 0 ? "in" :
                (EquationUpdaterCatalog.CkvdAngleKeys.Contains(key) ? "deg" : "mm");

            return $"\"{key}\" = 0{unit}";
        }

        private static bool LineExists(List<string> lines, string key)
        {
            foreach (var line in lines)
            {
                var match = LineRx.Match(line);
                if (match.Success && match.Groups["key"].Value.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static Encoding GetFileEncoding(string path)
        {
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            if (reader.Peek() >= 0)
                _ = reader.Read();
            return reader.CurrentEncoding;
        }

        private static string DetectNewline(string content)
        {
            if (content.Contains("\r\n")) return "\r\n";
            if (content.Contains('\n')) return "\n";
            return "\r\n";
        }

        private static void UpsertEquation(
            EquationMgr mgr,
            Dictionary<string, int> index,
            string key,
            string equationText)
        {
            if (index.TryGetValue(key, out var existingIndex))
            {
                try
                {
                    mgr.Equation[existingIndex] = equationText;
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[EquationUpdater] Failed to set Equation[{existingIndex}] for '{key}': {ex.Message}");
                }
            }

            try
            {
                _ = mgr.Add3(-1, equationText, true, (int)swInConfigurationOpts_e.swThisConfiguration, null);
                index[key] = mgr.GetCount() - 1;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[EquationUpdater] Failed to add equation for '{key}': {ex.Message}");
            }
        }

        private static Dictionary<string, int> BuildEquationIndex(EquationMgr mgr)
        {
            int count = mgr.GetCount();
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < count; i++)
            {
                var lhs = ExtractLhsName(mgr.Equation[i] ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(lhs) && !map.ContainsKey(lhs))
                    map.Add(lhs, i);
            }

            return map;
        }

        private static string ExtractLhsName(string equation)
        {
            int eqIndex = equation.IndexOf('=');
            string lhs = (eqIndex >= 0 ? equation[..eqIndex] : equation).Trim();

            if (lhs.StartsWith("\"") && lhs.EndsWith("\"") && lhs.Length >= 2)
                lhs = lhs[1..^1];

            return lhs;
        }

        private static void DumpEffectiveDims(
            string tag,
            IReadOnlyDictionary<DomDimKey, DomDim> effectiveDims)
        {
            if (effectiveDims is null)
            {
                Logger.Warn($"[{tag}] effectiveDims = <null>");
                return;
            }

            Logger.Info($"[{tag}] effectiveDims.Count = {effectiveDims.Count}");

            foreach (var kv in effectiveDims.OrderBy(k => k.Key.Value, StringComparer.OrdinalIgnoreCase))
            {
                var key = kv.Key.Value ?? "<null-key>";
                var dim = kv.Value;

                if (dim is null)
                {
                    Logger.Warn($"[{tag}] {key} = <null-dim>");
                    continue;
                }

                try
                {
                    bool isDegree = dim.Nominal.Unit == DomUnitKind.Degree;
                    double value = isDegree ? (double)dim.Nominal.AsDeg() : (double)dim.Nominal.AsMm();
                    Logger.Info($"[{tag}] {key} = {F(value)}{(isDegree ? "deg" : "mm")}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[{tag}] {key} = <read error>: {ex.Message}");
                }
            }
        }
    }
}
