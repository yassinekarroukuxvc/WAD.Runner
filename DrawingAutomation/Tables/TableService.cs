using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Infrastructure.Mapping;
using WAD.Runner.DrawingAutomation.Overlay;

namespace WAD.Runner.DrawingAutomation.Tables
{
    public sealed class TableService
    {
        private const double Eps = 1e-6;

        // Row heights (mm)
        private const double DimTableRowHeightMm = 4.0;
        private const double HowToOrderRowHeightMm = 3.048;
        private const double OverlayRowHeightMm = 2.5;

        // Font sizes (points — converted via PointsToMeters for SW)
        private const double DimTableFontSizePt = 6.0;
        private const double HowToOrderFontSizePt = 6.0;
        private const double OverlayFontSizePt = 4.0;

        // Extra vertical gap between estimated table bottom and anchor (m)
        private const double TableAnchorGapM = 0.005;

        private readonly SldWorks _app;
        private readonly ModelDoc2 _model;
        private readonly DrawingDoc _drawing;

        public TableService(SldWorks swApp, ModelDoc2 swModel)
        {
            _app = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _model = swModel ?? throw new ArgumentNullException(nameof(swModel));
            _drawing = swModel as DrawingDoc
                ?? throw new InvalidCastException("Active model is not a DrawingDoc.");
        }

        // -------------------------------------------------------------------------
        // Public table creators
        // -------------------------------------------------------------------------

        public bool CreateDimensionTable(
            WedgeData wedge,
            DrawingData draw,
            WedgeType wedgeType,
            string tableId = "DimTable",
            string header = "DIMENSIONS")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var rows = BuildDimensionRows_Filtered(wedge, draw, wedgeType);
            if (rows.Count == 0) return false;

            var configWidthM = ResolveWidthM(cfg, fallbackM: 0.14);
            var contentWidthM = EstimateMonospaceWidthM(rows, header,
                fontSizeM: PointsToMeters(DimTableFontSizePt),
                charWidthRatio: 0.8);
            var widthM = Math.Min(configWidthM, Math.Max(contentWidthM, 0.03));

            var posM = ToMeters(cfg.PositionMm);

            double tableHeightM = EstimateTableHeightM(rows.Count, DimTableRowHeightMm, includeTitle: true);
            double anchorY = posM.y + tableHeightM + TableAnchorGapM;

            var table = CreateOneColumnTable(posM.x, anchorY, rows.Count + 1, "Dimensions", widthM);
            if (table is null) return false;

            table.set_Text(0, 0, header);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i + 1, 0, rows[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, DimTableFontSizePt);
            TryApplyTypeface(table, "Monospac821 BT");
            SetAllRowHeights(table, DimTableRowHeightMm, includeTitle: true);
            TrimTrailingEmptyRows(table);

            return true;
        }

        public bool CreateOverlayDimensionTableAt(
            IReadOnlyList<OverlayDimensionRow> dimensions,
            double xMm,
            double yMm,
            double widthMm = 0.2,
            string header = "DIMENSIONS")
        {
            if (dimensions == null || dimensions.Count == 0) return false;

            var rows = BuildOverlayDimensionRowStrings(dimensions);
            if (rows.Count == 0) return false;

            var table = CreateOneColumnTable(
                xMm / 1000.0, yMm / 1000.0,
                rows.Count, "OverlayDimensions",
                widthMm / 1000.0);
            if (table is null) return false;

            table.TitleVisible = false;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i, 0, rows[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, OverlayFontSizePt);
            TryApplyTypeface(table, "Monospac821 BT");
            SetAllRowHeights(table, OverlayRowHeightMm, includeTitle: true);
            SetTableLayer(table, "annotation");
            HideAllTableBorders(table);
            TrimTrailingEmptyRows(table);

            return true;
        }

        public bool CreateHowToOrderTable(
            WedgeData wedge,
            DrawingData draw,
            string headerText = "HOW TO ORDER",
            string tableId = "HowToOrder")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var description = TryGetArticleDescription(wedge);
            var items = !string.IsNullOrWhiteSpace(description)
                ? WrapDescription(description, preferredLineLength: 56)
                : ReadLinesFromMetadata(draw, "HowToOrderInfo");

            if (items.Count == 0) return false;

            var posM = ToMeters(cfg.PositionMm);
            var table = CreateOneColumnTable(posM.x, posM.y, items.Count + 1, "HowToOrder", ResolveWidthM(cfg, fallbackM: 0.08));
            if (table is null) return false;

            table.set_Text(0, 0, headerText);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i + 1, 0, items[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, HowToOrderFontSizePt);
            TryApplyTypeface(table, "Monospac821 BT");
            SetAllRowHeights(table, HowToOrderRowHeightMm, includeTitle: true);
            TrimTrailingEmptyRows(table);

            return true;
        }

        public bool CreateLabelAsTable(DrawingData draw, string tableId = "LabelAs")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var items = ReadLinesFromMetadata(draw, "LabelAs");
            if (items.Count == 0) return false;

            var posM = ToMeters(cfg.PositionMm);
            var table = CreateOneColumnTable(posM.x, posM.y, items.Count, "LabelAs", ResolveWidthM(cfg, fallbackM: 0.08));
            if (table is null) return false;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i, 0, items[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            TrimTrailingEmptyRows(table);
            return true;
        }

        public bool CreatePolishTable(DrawingData draw, string tableId = "Polish")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var items = ReadLinesFromMetadata(draw, "PolishInstructions");
            if (items.Count == 0) return false;

            var posM = ToMeters(cfg.PositionMm);
            var table = CreateOneColumnTable(posM.x, posM.y, items.Count, "Polish", ResolveWidthM(cfg, fallbackM: 0.08));
            if (table is null) return false;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i, 0, items[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            TrimTrailingEmptyRows(table);
            return true;
        }

        // -------------------------------------------------------------------------
        // Table layer / border helpers
        // -------------------------------------------------------------------------

        public void SetTableLayer(TableAnnotation table, string layerName)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            if (string.IsNullOrWhiteSpace(layerName)) return;

            try
            {
                var ann = table.GetAnnotation() as Annotation;
                if (ann != null) ann.Layer = layerName;
                _model?.GraphicsRedraw2();
            }
            catch { }
        }

        private static void HideAllTableBorders(TableAnnotation table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            var none = (int)swLineWeights_e.swLW_NONE;
            table.BorderLineWeight = none;
            table.BorderLineWeightCustom = 1;
            table.GridLineWeight = none;
            table.GridLineWeightCustom = 1;
        }

        // -------------------------------------------------------------------------
        // Core table factory
        // -------------------------------------------------------------------------

        private TableAnnotation? CreateOneColumnTable(
            double xM, double yM, int rows, string title, double colWidthM)
        {
            try
            {
                var table = _drawing.InsertTableAnnotation2(
                    false, xM, yM, 1, "", rows, 1) as TableAnnotation;

                if (table == null) return null;

                table.SetColumnWidth(
                    0, colWidthM,
                    (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);

                table.GridLineWeight = (int)swLineWeights_e.swLW_NONE;
                table.Title = title;
                table.TitleVisible = true;

                return table;
            }
            catch { return null; }
        }

        // -------------------------------------------------------------------------
        // Row height
        // -------------------------------------------------------------------------

        private void SetAllRowHeights(TableAnnotation table, double rowHeightMm, bool includeTitle = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            try
            {
                double heightM = rowHeightMm / 1000.0;
                int startRow = includeTitle ? 0 : 1;

                for (int r = startRow; r < table.RowCount; r++)
                {
                    table.SetRowHeight(
                        r, heightM,
                        (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);
                }

                _model.GraphicsRedraw2();
            }
            catch { }
        }

        // -------------------------------------------------------------------------
        // Font helpers
        // -------------------------------------------------------------------------

        private void SetTableFontSize(TableAnnotation table, double fontSizePt, bool includeTitle = true)
        {
            try
            {
                double h = PointsToMeters(fontSizePt);

                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.CharHeight = h;
                    table.SetTextFormat(false, tf);
                    if (includeTitle) table.SetTextFormat(true, tf);
                }

                for (int r = 0; r < table.RowCount; r++)
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var cellTf = table.GetCellTextFormat(r, c) as TextFormat;
                        if (cellTf == null) continue;
                        cellTf.CharHeight = h;
                        // false = "do NOT use document format" — this is what clears the checkbox
                        table.SetCellTextFormat(r, c, false, cellTf);
                    }

                _model.GraphicsRedraw2();
            }
            catch { }
        }

        // Typeface only — never touches CharHeight to avoid double-scaling
        private void TryApplyTypeface(TableAnnotation table, string typeface)
        {
            try
            {
                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.TypeFaceName = typeface;
                    table.SetTextFormat(false, tf);
                    table.SetTextFormat(true, tf);
                }

                for (int r = 0; r < table.RowCount; r++)
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var cellTf = table.GetCellTextFormat(r, c) as TextFormat;
                        if (cellTf == null) continue;
                        cellTf.TypeFaceName = typeface;
                        // false = don't use document format
                        table.SetCellTextFormat(r, c, false, cellTf);
                    }

                _model.GraphicsRedraw2();
            }
            catch { }
        }

        private static double PointsToMeters(double pt) => pt * 0.0003527777778;

        // -------------------------------------------------------------------------
        // Table height / width estimation
        // -------------------------------------------------------------------------

        private static double EstimateTableHeightM(int dataRowCount, double rowHeightMm, bool includeTitle)
        {
            int totalRows = dataRowCount + (includeTitle ? 1 : 0);
            return totalRows * rowHeightMm / 1000.0;
        }

        private static double EstimateMonospaceWidthM(
            IReadOnlyList<string> rows,
            string header,
            double fontSizeM,
            double charWidthRatio = 0.60,
            double paddingMm = 1.0)
        {
            int maxChars = string.IsNullOrEmpty(header) ? 0 : header.Length;
            foreach (var r in rows)
                if (r != null && r.Length > maxChars) maxChars = r.Length;

            if (maxChars == 0) return 0.03;

            return fontSizeM * charWidthRatio * maxChars + paddingMm / 1000.0;
        }

        // -------------------------------------------------------------------------
        // Config / position helpers
        // -------------------------------------------------------------------------

        private static (double x, double y) ToMeters(double[] mm)
        {
            var x = (mm != null && mm.Length > 0) ? mm[0] / 1000.0 : 0.0;
            var y = (mm != null && mm.Length > 1) ? mm[1] / 1000.0 : 0.0;
            return (x, y);
        }

        private static double ResolveWidthM(TableConfig cfg, double fallbackM)
        {
            if (cfg?.SizeMm is { Length: >= 1 }) return cfg.SizeMm[0] / 1000.0;
            if (cfg?.Params != null && cfg.Params.TryGetValue("widthMm", out var wmm)) return wmm / 1000.0;
            return fallbackM;
        }

        private static bool TryGetCfg(DrawingData draw, string id, out TableConfig cfg)
        {
            cfg = null!;
            if (draw?.Tables != null
                && draw.Tables.TryGetValue(id, out var t)
                && t != null
                && t.PositionMm is { Length: >= 2 })
            {
                cfg = t;
                return true;
            }
            return false;
        }

        // -------------------------------------------------------------------------
        // Row trimming
        // -------------------------------------------------------------------------

        private static void TrimTrailingEmptyRows(TableAnnotation table)
        {
            if (table == null) return;

            try
            {
                for (int r = table.RowCount - 1; r >= 0; r--)
                {
                    bool allEmpty = true;
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var text = table.get_Text(r, c) as string ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text)) { allEmpty = false; break; }
                    }

                    if (allEmpty) table.DeleteRow(r);
                    else break;
                }
            }
            catch { }
        }

        // -------------------------------------------------------------------------
        // Row builders
        // -------------------------------------------------------------------------

        private static List<string> BuildDimensionRows_Filtered(
            WedgeData wedge, DrawingData draw, WedgeType wedgeType)
        {
            var rows = new List<string>();
            var drawingType = draw.DrawingType;
            var allowed = DimensionTableKeyFilter.GetAllowedKeys(wedgeType, drawingType, wedge.Subclass);

            foreach (var kv in wedge.Dimensions)
            {
                var key = kv.Key.Value;
                var d = kv.Value;
                bool isAngle = DimensionKeyPolicy.IsAngle(key);

                if (allowed != null && !allowed.Contains(key)) continue;
                if (drawingType == DrawingType.Overlay && isAngle) continue;

                if (isAngle)
                {
                    if (!d.Nominal.IsDeg) continue;
                    var deg = d.Nominal.AsDeg();
                    if (deg == 0m) continue;

                    var degStr = deg.ToString("0.###", CultureInfo.InvariantCulture);
                    var tolDeg = FormatTolDeg(d.Tol);
                    var text = string.IsNullOrEmpty(tolDeg)
                        ? $"{key}={degStr}°"
                        : $"{key}={degStr}° {tolDeg}";

                    if (IsZeroTol(d.Tol)) text += " (REF)";
                    rows.Add(text);
                    continue;
                }

                if (!d.Nominal.IsMm) continue;
                var mm = d.Nominal.AsMm();
                if (mm == 0m) continue;

                var inch = MmToIn(mm);
                if (Math.Abs((double)inch) < Eps) continue;

                var inchStr = TrimLeadingZero(inch.ToString("0.0000", CultureInfo.InvariantCulture));
                var mmStr = mm.ToString("0.###", CultureInfo.InvariantCulture);
                var tolIn = FormatTolInches(d.Tol, removeLeadingZero: true);
                var tolMm = FormatTolMm(d.Tol);

                var left = $"{key}={inchStr}";
                var middle = string.IsNullOrEmpty(tolIn) ? "" : " " + tolIn;
                var right = $"[{mmStr}{(string.IsNullOrEmpty(tolMm) ? "" : " " + tolMm)}]";

                var rowText = (left + middle + " " + right).Trim();
                if (IsZeroTol(d.Tol)) rowText += " (REF)";
                rows.Add(rowText);
            }

            return rows;
        }

        private static List<string> BuildOverlayDimensionRowStrings(IReadOnlyList<OverlayDimensionRow> dims)
        {
            var result = new List<string>();

            foreach (var row in dims)
            {
                if (row.Nominal.IsMm)
                {
                    var mm = row.Nominal.AsMm();
                    var inch = MmToIn(mm);
                    var inchStr = TrimLeadingZero(inch.ToString("0.0000", CultureInfo.InvariantCulture));

                    var lowerMm = row.TolLower.IsMm ? row.TolLower.AsMm() : 0m;
                    var upperMm = row.TolUpper.IsMm ? row.TolUpper.AsMm() : 0m;
                    var tolText = FormatOverlayLengthToleranceInches(lowerMm, upperMm);

                    result.Add(string.IsNullOrEmpty(tolText)
                        ? $"{row.Key}={inchStr} (REF)"
                        : $"{row.Key}={inchStr} {tolText}");
                }
                else if (row.Nominal.IsDeg)
                {
                    var deg = row.Nominal.AsDeg();
                    var degStr = deg.ToString("0.###", CultureInfo.InvariantCulture);

                    var lowerDeg = row.TolLower.IsDeg ? row.TolLower.AsDeg() : 0m;
                    var upperDeg = row.TolUpper.IsDeg ? row.TolUpper.AsDeg() : 0m;
                    var tolText = FormatOverlayAngleToleranceDegrees(lowerDeg, upperDeg);

                    result.Add(string.IsNullOrEmpty(tolText)
                        ? $"{row.Key}={degStr}° (REF)"
                        : $"{row.Key}={degStr}° {tolText}");
                }
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // Formatting helpers
        // -------------------------------------------------------------------------

        private static string FormatTolDeg(Tolerance tol)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m)) return "";
            return FormatTolerancePair(
                Math.Abs(tol.Lower.Value), Math.Abs(tol.Upper.Value),
                v => v.ToString("0.###", CultureInfo.InvariantCulture), "°");
        }

        private static string FormatTolInches(Tolerance tol, bool removeLeadingZero)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m)) return "";
            return FormatTolerancePair(
                MmToIn(Math.Abs(tol.Lower.Value)), MmToIn(Math.Abs(tol.Upper.Value)),
                v => FormatDecimal(v, "0.0000", removeLeadingZero), "");
        }

        private static string FormatTolMm(Tolerance tol)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m)) return "";
            return FormatTolerancePair(
                Math.Abs(tol.Lower.Value), Math.Abs(tol.Upper.Value),
                v => v.ToString("0.###", CultureInfo.InvariantCulture), "");
        }

        private static string FormatOverlayLengthToleranceInches(decimal lowerMm, decimal upperMm)
            => FormatTolerancePair(
                MmToIn(Math.Abs(lowerMm)), MmToIn(Math.Abs(upperMm)),
                v => FormatDecimal(v, "0.0000", removeLeadingZero: true), "");

        private static string FormatOverlayAngleToleranceDegrees(decimal lowerDeg, decimal upperDeg)
            => FormatTolerancePair(
                Math.Abs(lowerDeg), Math.Abs(upperDeg),
                v => v.ToString("0.###", CultureInfo.InvariantCulture), "°");

        private static string FormatTolerancePair(
            decimal lower, decimal upper,
            Func<decimal, string> formatValue,
            string suffix)
        {
            if (lower == 0m && upper == 0m) return "";
            if (lower == upper) return $"±{formatValue(upper)}{suffix}";
            if (lower == 0m) return $"+{formatValue(upper)}{suffix}";
            if (upper == 0m) return $"-{formatValue(lower)}{suffix}";
            return $"-{formatValue(lower)}{suffix} +{formatValue(upper)}{suffix}";
        }

        private static string FormatDecimal(decimal value, string format, bool removeLeadingZero)
        {
            var s = value.ToString(format, CultureInfo.InvariantCulture);
            return removeLeadingZero ? TrimLeadingZero(s) : s;
        }

        private static decimal MmToIn(decimal mm) => mm / 25.4m;

        private static string TrimLeadingZero(string s)
            => s.StartsWith("0.", StringComparison.Ordinal) ? s[1..] :
               s.StartsWith("-0.", StringComparison.Ordinal) ? "-" + s[2..] : s;

        private static bool IsZeroTol(Tolerance tol)
            => tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m);

        // -------------------------------------------------------------------------
        // Metadata / text helpers
        // -------------------------------------------------------------------------

        private static List<string> ReadLinesFromMetadata(DrawingData draw, string metaKey)
        {
            var lines = new List<string>();
            if (draw.Metadata != null
                && draw.Metadata.TryGetValue(metaKey, out var text)
                && !string.IsNullOrWhiteSpace(text))
            {
                foreach (var p in text.Replace("\r", "")
                                      .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = p.Trim();
                    if (t.Length > 0) lines.Add(t);
                }
            }
            return lines;
        }

        private static string? TryGetArticleDescription(WedgeData wedge)
        {
            if (wedge?.Properties == null) return null;
            foreach (var kv in wedge.Properties)
                if (string.Equals(kv.Key, "article_description", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
            return null;
        }

        private static List<string> WrapDescription(string text, int preferredLineLength)
        {
            var rawLines = text.Replace("\r", "")
                               .Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim())
                               .Where(s => s.Length > 0)
                               .ToList();

            if (rawLines.Count == 0) rawLines.Add(text.Trim());

            var result = new List<string>();
            foreach (var line in rawLines)
            {
                if (line.Length <= preferredLineLength) { result.Add(line); continue; }

                var cur = "";
                foreach (var w in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (cur.Length == 0) { cur = w; continue; }

                    if (cur.Length + 1 + w.Length <= preferredLineLength)
                        cur += " " + w;
                    else
                    { result.Add(cur); cur = w; }
                }
                if (cur.Length > 0) result.Add(cur);
            }

            return result;
        }
    }
}