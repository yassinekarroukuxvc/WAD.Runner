// DrawingAutomation/Tables/TableService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.DataManagement.Domain.Wedge;      // WedgeData
using WAD.Runner.DataManagement.Domain.Drawing;    // DrawingData, TableConfig
using WAD.Runner.DataManagement.Domain.Dimensions; // Dimension, DimensionKey
using WAD.Runner.DataManagement.Domain.Units;      // Quantity, Tolerance
using WAD.Runner.DataManagement.Infrastructure.Mapping; // DimensionKeyPolicy
using WAD.Runner.DrawingAutomation.Overlay;        // OverlayDimensionRow

namespace WAD.Runner.DrawingAutomation.Tables
{
    public sealed class TableService
    {
        private const double Eps = 1e-6;

        private readonly SldWorks _app;
        private readonly ModelDoc2 _model;
        private readonly DrawingDoc _drawing;

        public TableService(SldWorks swApp, ModelDoc2 swModel)
        {
            _app = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _model = swModel ?? throw new ArgumentNullException(nameof(swModel));
            _drawing = swModel as DrawingDoc ?? throw new InvalidCastException("Active model is not a DrawingDoc.");
        }

        // ---------- PUBLIC, SIMPLE ENTRY POINTS ----------

        public bool CreateDimensionTable(WedgeData wedge, DrawingData draw, string tableId = "DimTable", string header = "DIMENSIONS")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var rows = BuildDimensionRows_Filtered(wedge);  // filtered: non-zero lengths only, no angles
            if (rows.Count == 0) return false;

            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);  // default ≈80mm
            var posM = ToMeters(cfg.PositionMm);

            var table = CreateOneColumnTable(posM.x, posM.y, rows.Count + 1, "Dimensions", widthM);
            if (table is null) return false;

            // Header
            table.set_Text(0, 0, header);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            // Rows
            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i + 1, 0, rows[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            // Font & style (optional, safe)
            SetTableFontSize(table, 7);
            TryApplyTypeface(table, "Monospac821 BT", scaleCharHeight: 0.90);
            return true;
        }

        /// <summary>
        /// Creates a one-column Overlay dimension table at the given position (in millimeters),
        /// using the rows from overlayData.Dimensions.
        /// </summary>
        /// <param name="dimensions">overlayData.Dimensions</param>
        /// <param name="xMm">X position on sheet in millimeters</param>
        /// <param name="yMm">Y position on sheet in millimeters</param>
        /// <param name="widthMm">Column width in millimeters (default ~80mm)</param>
        /// <param name="header">Header text (default "DIMENSIONS")</param>
        public bool CreateOverlayDimensionTableAt(
            IReadOnlyList<OverlayDimensionRow> dimensions,
            double xMm,
            double yMm,
            double widthMm = 0.2,
            string header = "DIMENSIONS")
        {
            if (dimensions == null || dimensions.Count == 0)
                return false;

            // Convert mm → meters for SolidWorks
            var xM = xMm / 1000.0;
            var yM = yMm / 1000.0;
            var widthM = widthMm / 1000.0;

            // Build text lines from OverlayDimensionRow
            var rows = BuildOverlayDimensionRowStrings(dimensions);
            if (rows.Count == 0) return false;

            var table = CreateOneColumnTable(xM, yM, rows.Count + 1, "OverlayDimensions", widthM);
            if (table is null) return false;

            // Header
            table.set_Text(0, 0, header);
            table.CellTextHorizontalJustification[0, 0] =
                (int)swTextJustification_e.swTextJustificationLeft;

            // Rows
            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i + 1, 0, rows[i]);
                table.CellTextHorizontalJustification[i + 1, 0] =
                    (int)swTextJustification_e.swTextJustificationLeft;
            }

            // Styling
            SetTableFontSize(table, 4);
            TryApplyTypeface(table, "Monospac821 BT", scaleCharHeight: 0.90);
            SetTableRowHeights(table, rowHeightMm: 0);
            HideAllTableBorders(table);
            SetTableLayer(table, "annotation");

            return true;
        }

        public void SetTableLayer(TableAnnotation table, string layerName)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            if (string.IsNullOrWhiteSpace(layerName))
                return;

            try
            {
                // Get the underlying annotation and set its layer
                var ann = table.GetAnnotation() as Annotation;
                if (ann != null)
                {
                    ann.Layer = layerName;
                }

                _model?.GraphicsRedraw2();
            }
            catch
            {
                // Keep safe – you can plug logging here if needed
            }
        }


        private static void HideAllTableBorders(TableAnnotation table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            // "No line" weight
            var none = (int)swLineWeights_e.swLW_NONE;

            // Outer border
            table.BorderLineWeight = none;
            table.BorderLineWeightCustom = 1;   // 1 = use custom weight

            // Inner grid lines
            table.GridLineWeight = none;
            table.GridLineWeightCustom = 1;     // 1 = use custom weight
        }

        /// <summary>
        /// Creates the "How to Order" table using the article description we fetched from DB:
        /// WedgeData.Properties["article_description"] (case-insensitive).
        /// Falls back to DrawingData.Metadata["HowToOrderInfo"] if not found.
        /// </summary>
        public bool CreateHowToOrderTable(WedgeData wedge, DrawingData draw, string headerText = "HOW TO ORDER", string tableId = "HowToOrder")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            // 1) Try WedgeData.Properties["article_description"]
            var description = TryGetArticleDescription(wedge);

            // 2) Fallback to old metadata if DB description is missing
            List<string> items;
            if (!string.IsNullOrWhiteSpace(description))
            {
                // Wrap long text into multiple lines for a one-column table
                items = WrapDescription(description, preferredLineLength: 56);
            }
            else
            {
                items = ReadLinesFromMetadata(draw, "HowToOrderInfo");
            }

            if (items.Count == 0) return false;

            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);
            var posM = ToMeters(cfg.PositionMm);

            // +1 for header line
            var table = CreateOneColumnTable(posM.x, posM.y, items.Count + 1, "HowToOrder", widthM);
            if (table is null) return false;

            table.set_Text(0, 0, headerText);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i + 1, 0, items[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, 6);
            TryApplyTypeface(table, "Monospac821 BT", null);
            return true;
        }

        public bool CreateLabelAsTable(DrawingData draw, string tableId = "LabelAs")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var items = ReadLinesFromMetadata(draw, "LabelAs");
            if (items.Count == 0) return false;

            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);
            var posM = ToMeters(cfg.PositionMm);

            var table = CreateOneColumnTable(posM.x, posM.y, items.Count, "LabelAs", widthM);
            if (table is null) return false;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i, 0, items[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }
            return true;
        }

        public bool CreatePolishTable(DrawingData draw, string tableId = "Polish")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var items = ReadLinesFromMetadata(draw, "PolishInstructions");
            if (items.Count == 0) return false;

            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);
            var posM = ToMeters(cfg.PositionMm);

            var table = CreateOneColumnTable(posM.x, posM.y, items.Count, "Polish", widthM);
            if (table is null) return false;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i, 0, items[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }
            return true;
        }

        // ---------- INTERNAL HELPERS (ONLY APIS FROM YOUR SNIPPET) ----------

        private TableAnnotation? CreateOneColumnTable(double xM, double yM, int rows, string title, double colWidthM)
        {
            try
            {
                // Insert a 1-column general table at (xM,yM)
                var table = _drawing.InsertTableAnnotation2(
                    false, xM, yM, 1, /*template*/"", rows, 1) as TableAnnotation;

                if (table == null) return null;

                table.SetColumnWidth(
                    0,
                    colWidthM,
                    (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);

                table.GridLineWeight = (int)swLineWeights_e.swLW_NONE; // optional
                table.Title = title;
                table.TitleVisible = true;

                return table;
            }
            catch
            {
                return null;
            }
        }

        private static (double x, double y) ToMeters(double[] mm)
        {
            var x = (mm != null && mm.Length > 0) ? mm[0] / 1000.0 : 0.0;
            var y = (mm != null && mm.Length > 1) ? mm[1] / 1000.0 : 0.0;
            return (x, y);
        }

        private static double ResolveWidthM(TableConfig cfg, double fallbackM)
        {
            // Prefer SizeMm[0] if present; else Params["widthMm"]; else fallbackM
            if (cfg?.SizeMm is { Length: >= 1 }) return cfg.SizeMm[0] / 1000.0;
            if (cfg?.Params != null && cfg.Params.TryGetValue("widthMm", out var wmm)) return wmm / 1000.0;
            return fallbackM;
        }

        private static bool TryGetCfg(DrawingData draw, string id, out TableConfig cfg)
        {
            cfg = null!;
            if (draw?.Tables != null && draw.Tables.TryGetValue(id, out var t) && t != null && t.PositionMm is { Length: >= 2 })
            {
                cfg = t;
                return true;
            }
            return false;
        }

        private static readonly Regex FontTagRegex = new(@"</?FONT[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void TryApplyTypeface(TableAnnotation table, string typeface, double? scaleCharHeight)
        {
            try
            {
                // Base format
                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.TypeFaceName = typeface;
                    if (scaleCharHeight.HasValue && tf.CharHeight > 0)
                        tf.CharHeight *= scaleCharHeight.Value;
                    table.SetTextFormat(false, tf);   // cells
                    table.SetTextFormat(true, tf);    // title/header
                }

                // Each cell
                for (int r = 0; r < table.RowCount; r++)
                {
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var cellTf = table.GetCellTextFormat(r, c) as TextFormat;
                        if (cellTf == null) continue;
                        cellTf.TypeFaceName = typeface;
                        if (scaleCharHeight.HasValue && cellTf.CharHeight > 0)
                            cellTf.CharHeight *= scaleCharHeight.Value;
                        table.SetCellTextFormat(r, c, false, cellTf);
                    }
                }

                _model.GraphicsRedraw2();
            }
            catch { /* keep safe */ }
        }

        private void SetTableFontSize(TableAnnotation table, double fontSizePoints, bool includeTitle = true)
        {
            try
            {
                double h = PointsToMeters(fontSizePoints);

                // Base
                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.CharHeight = h;
                    table.SetTextFormat(false, tf);
                    if (includeTitle) table.SetTextFormat(true, tf);
                }

                // Cells
                for (int r = 0; r < table.RowCount; r++)
                {
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var cellTf = table.GetCellTextFormat(r, c) as TextFormat;
                        if (cellTf == null) continue;
                        cellTf.CharHeight = h;
                        table.SetCellTextFormat(r, c, false, cellTf);
                    }
                }

                _model.GraphicsRedraw2();
            }
            catch { /* safe */ }
        }

        private static double PointsToMeters(double pt) => pt * 0.0003527777778;

        // ---------- CONTENT BUILDERS (ONE-COLUMN STRINGS) ----------

        // FILTERED: include only length dimensions with non-zero nominal (skip angles & zeros)
        // Row format: "TL=.0250 ±.0010 [0.635 ±0.025]"
        private static List<string> BuildDimensionRows_Filtered(WedgeData wedge)
        {
            var rows = new List<string>();

            foreach (var kv in wedge.Dimensions)
            {
                var key = kv.Key.Value;
                if (DimensionKeyPolicy.IsAngle(key)) continue; // exclude angles by key

                var d = kv.Value;

                // Only include lengths with non-zero nominal
                if (!d.Nominal.IsMm) continue;
                var mm = d.Nominal.AsMm(); // decimal
                if (mm == 0m) continue;

                var inch = MmToIn(mm);
                if (Math.Abs((double)inch) < Eps) continue; // treat as zero in (defensive)

                var inchStr = TrimLeadingZero(inch.ToString("0.0000", CultureInfo.InvariantCulture));
                var mmStr = mm.ToString("0.###", CultureInfo.InvariantCulture);

                var tolIn = FormatTolInches(d.Tol, removeLeadingZero: true);  // "±.0010" or ""/ "0"
                var tolMm = FormatTolMm(d.Tol);                                // "±0.025" or ""/ "0"

                var refFlag = IsZeroTol(d.Tol);

                var left = $"{key}={inchStr}";
                var right = $"[{mmStr}{(string.IsNullOrEmpty(tolMm) ? "" : " " + tolMm)}]";
                var middle = string.IsNullOrEmpty(tolIn) ? "" : " " + tolIn;

                var text = (left + middle + " " + right).Trim();
                if (refFlag) text += " (REF)";

                rows.Add(text);
            }

            return rows;
        }

        private static List<string> ReadLinesFromMetadata(DrawingData draw, string metaKey)
        {
            var lines = new List<string>();
            if (draw.Metadata != null && draw.Metadata.TryGetValue(metaKey, out var text) && !string.IsNullOrWhiteSpace(text))
            {
                var parts = text.Replace("\r", "")
                                .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var p in parts)
                {
                    var t = p.Trim();
                    if (t.Length > 0) lines.Add(t);
                }
            }
            return lines;
        }

        // ---------- FORMATTING ----------

        /// <summary>
        /// Builds display strings for overlay dimension rows.
        /// Requirements:
        /// - Use only millimeters (no inches).
        /// - Skip zero-valued dimensions (builder already does that).
        /// - If both tolerances are zero → mark as REF:
        ///     FL=.0354 (REF)
        /// - Otherwise: show ± tolerance in mm:
        ///     FL=.0354 ±.0010
        /// </summary>
        private static List<string> BuildOverlayDimensionRowStrings(IReadOnlyList<OverlayDimensionRow> dims)
        {
            var result = new List<string>();

            foreach (var row in dims)
            {
                // Length dimensions (mm)
                if (row.Nominal.IsMm)
                {
                    var mm = row.Nominal.AsMm();

                    // Format nominal in mm, 4 decimals, no leading zero (".0354" style)
                    var mmStr = TrimLeadingZero(mm.ToString("0.0000", CultureInfo.InvariantCulture));

                    string text;

                    if (row.IsZeroTolerance)
                    {
                        // REF: no explicit tolerance, mark the value as reference
                        text = $"{row.Key}={mmStr} (REF)";
                    }
                    else
                    {
                        var lowerMm = row.TolLower.IsMm ? row.TolLower.AsMm() : 0m;
                        var upperMm = row.TolUpper.IsMm ? row.TolUpper.AsMm() : 0m;

                        var maxAbsMm = Math.Max(Math.Abs(lowerMm), Math.Abs(upperMm));

                        // If somehow both are zero but IsZeroTolerance is false, still treat as REF
                        if (maxAbsMm == 0m)
                        {
                            text = $"{row.Key}={mmStr} (REF)";
                        }
                        else
                        {
                            var tolStr = TrimLeadingZero(maxAbsMm.ToString("0.0000", CultureInfo.InvariantCulture));
                            text = $"{row.Key}={mmStr} ±{tolStr}";
                        }
                    }

                    // Comment is intentionally NOT appended anymore.
                    result.Add(text);
                }
                // Angle dimensions (degrees) – keep them if they ever appear
                else if (row.Nominal.IsDeg)
                {
                    var deg = row.Nominal.AsDeg();
                    var degStr = deg.ToString("0.###", CultureInfo.InvariantCulture);

                    string text;

                    if (row.IsZeroTolerance)
                    {
                        text = $"{row.Key}={degStr}° (REF)";
                    }
                    else
                    {
                        var lowerDeg = row.TolLower.IsDeg ? row.TolLower.AsDeg() : 0m;
                        var upperDeg = row.TolUpper.IsDeg ? row.TolUpper.AsDeg() : 0m;
                        var maxAbsDeg = Math.Max(Math.Abs(lowerDeg), Math.Abs(upperDeg));

                        if (maxAbsDeg == 0m)
                        {
                            text = $"{row.Key}={degStr}° (REF)";
                        }
                        else
                        {
                            var tolStr = maxAbsDeg.ToString("0.###", CultureInfo.InvariantCulture);
                            text = $"{row.Key}={degStr}° ±{tolStr}°";
                        }
                    }

                    // Comment is intentionally NOT appended anymore.
                    result.Add(text);
                }
                else
                {
                    // Unknown unit kind – skip for safety
                    continue;
                }
            }

            return result;
        }

        private static decimal MmToIn(decimal mm) => mm / 25.4m;

        private static string TrimLeadingZero(string s)
            => s.StartsWith("0.", StringComparison.Ordinal) ? s[1..] :
               (s.StartsWith("-0.", StringComparison.Ordinal) ? "-" + s[2..] : s);

        private static bool IsZeroTol(Tolerance tol)
            => tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m);

        private static string FormatTolInches(Tolerance tol, bool removeLeadingZero)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m)) return "";
            var maxAbsMm = Math.Max(Math.Abs(tol.Lower.Value), Math.Abs(tol.Upper.Value));
            var maxAbsIn = MmToIn(maxAbsMm);
            var s = maxAbsIn.ToString("0.0000", CultureInfo.InvariantCulture);
            s = removeLeadingZero ? TrimLeadingZero(s) : s;
            return $"±{s}";
        }

        private static string FormatTolMm(Tolerance tol)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m)) return "";
            var maxAbs = Math.Max(Math.Abs(tol.Lower.Value), Math.Abs(tol.Upper.Value));
            return $"±{maxAbs:0.###}";
        }

        // ---------- NEW: description helpers ----------

        private static string? TryGetArticleDescription(WedgeData wedge)
        {
            if (wedge?.Properties == null) return null;
            // case-insensitive lookup for "article_description"
            foreach (var kv in wedge.Properties)
            {
                if (string.Equals(kv.Key, "article_description", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
            }
            return null;
        }

        /// <summary>
        /// Wrap a free-text description into lines for a one-column table.
        /// Splits by existing newlines/semicolons first, then wraps long lines
        /// to ~preferredLineLength characters without breaking words.
        /// </summary>
        private static List<string> WrapDescription(string text, int preferredLineLength)
        {
            var rawLines = text
                .Replace("\r", "")
                .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToList();

            if (rawLines.Count == 0) rawLines.Add(text.Trim());

            var result = new List<string>();
            foreach (var line in rawLines)
            {
                if (line.Length <= preferredLineLength)
                {
                    result.Add(line);
                    continue;
                }

                // Greedy word-wrap at or below preferredLineLength
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var cur = "";
                foreach (var w in words)
                {
                    if (cur.Length == 0)
                    {
                        cur = w;
                        continue;
                    }

                    if (cur.Length + 1 + w.Length <= preferredLineLength)
                    {
                        cur += " " + w;
                    }
                    else
                    {
                        result.Add(cur);
                        cur = w;
                    }
                }
                if (cur.Length > 0) result.Add(cur);
            }

            return result;
        }

        private void SetTableRowHeights(TableAnnotation table, double rowHeightMm, bool includeTitle = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            try
            {
                // SolidWorks expects meters
                var heightM = rowHeightMm / 1000.0;

                int startRow = includeTitle ? 0 : 1;
                for (int r = startRow; r < table.RowCount; r++)
                {
                    table.SetRowHeight(
                        r,
                        heightM,
                        (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);
                }

                _model.GraphicsRedraw2();
            }
            catch
            {
                // keep safe
            }
        }

    }
}
