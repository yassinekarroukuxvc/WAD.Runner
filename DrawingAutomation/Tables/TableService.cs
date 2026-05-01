// DrawingAutomation/Tables/TableService.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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

        private readonly SldWorks _app;
        private readonly ModelDoc2 _model;
        private readonly DrawingDoc _drawing;

        public TableService(SldWorks swApp, ModelDoc2 swModel)
        {
            _app = swApp ?? throw new ArgumentNullException(nameof(swApp));
            _model = swModel ?? throw new ArgumentNullException(nameof(swModel));
            _drawing = swModel as DrawingDoc ?? throw new InvalidCastException("Active model is not a DrawingDoc.");
        }

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

            // Derive width from content, capped between a min and the config value
            var configWidthM = ResolveWidthM(cfg, fallbackM: 0.08);
            var contentWidthM = EstimateMonospaceWidthM(rows, header, fontSizePt: 6.0, scaleCharHeight: 0.90, charWidthRatio: 0.8);
            var widthM = Math.Min(configWidthM, Math.Max(contentWidthM, 0.03)); // floor at 30mm

            var posM = ToMeters(cfg.PositionMm);

            // Shift Y so the table grows upward from the config anchor point.
            // If your anchor is the TOP-LEFT corner of the table (SW default),
            // and you want it to grow downward, skip this and use posM.y directly.
            double tableHeightM = EstimateTableHeightM(rows.Count, rowHeightMm: 3.5, includeTitle: true);
            double adjustedY = posM.y + tableHeightM + 0.005;

            var table = CreateOneColumnTable(posM.x, adjustedY, rows.Count + 1, "Dimensions", widthM);

            if (table is null) return false;

            table.set_Text(0, 0, header);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i + 1, 0, rows[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, 6);
            TryApplyTypeface(table, "Monospac821 BT", scaleCharHeight: 0.90);
            SetTableRowHeights(table, rowHeightMm: 2.0, includeTitle: true);
            ShrinkTableHeight(table, includeTitle: true);
            TrimTrailingEmptyRows(table);

            return true;
        }

        /// <summary>
        /// Creates a one-column Overlay dimension table at the given position (in millimeters),
        /// using the rows from overlayData.Dimensions.
        /// No header row, no visible title band.
        /// </summary>
        /// <param name="dimensions">overlayData.Dimensions</param>
        /// <param name="xMm">X position on sheet in millimeters</param>
        /// <param name="yMm">Y position on sheet in millimeters</param>
        /// <param name="widthMm">Column width in millimeters (default ~80mm)</param>
        /// <param name="header">Kept for signature compatibility, not used.</param>
        public bool CreateOverlayDimensionTableAt(
            IReadOnlyList<OverlayDimensionRow> dimensions,
            double xMm,
            double yMm,
            double widthMm = 0.2,
            string header = "DIMENSIONS")
        {
            if (dimensions == null || dimensions.Count == 0)
                return false;

            var xM = xMm / 1000.0;
            var yM = yMm / 1000.0;

            var widthM = widthMm / 1000.0;

            var rows = BuildOverlayDimensionRowStrings(dimensions);
            if (rows.Count == 0) return false;

            var table = CreateOneColumnTable(xM, yM, rows.Count, "OverlayDimensions", widthM);
            if (table is null) return false;

            table.TitleVisible = false;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i, 0, rows[i]);
                table.CellTextHorizontalJustification[i, 0] =
                    (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, 4);
            TryApplyTypeface(table, "Monospac821 BT", null);
            SetTableRowHeights(table, rowHeightMm: 2.5, includeTitle: true);
            SetTableLayer(table, "annotation");
            HideAllTableBorders(table);
            TrimTrailingEmptyRows(table);

            return true;
        }

        private void ShrinkTableHeight(TableAnnotation table, bool includeTitle = true)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            try
            {
                int startRow = includeTitle ? 0 : 1;

                double currentMm = 0.1;
                for (int iteration = 0; iteration < 5; iteration++)
                {
                    var heightM = currentMm / 1000.0;

                    for (int r = startRow; r < table.RowCount; r++)
                    {
                        table.SetRowHeight(
                            r,
                            heightM,
                            (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);
                    }

                    _model.GraphicsRedraw2();

                    currentMm /= 2.0;
                    if (currentMm < 0.01) break;
                }
            }
            catch
            {
            }
        }

        public void SetTableLayer(TableAnnotation table, string layerName)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            if (string.IsNullOrWhiteSpace(layerName))
                return;

            try
            {
                var ann = table.GetAnnotation() as Annotation;
                if (ann != null)
                {
                    ann.Layer = layerName;
                }

                _model?.GraphicsRedraw2();
            }
            catch
            {
            }
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

        /// <summary>
        /// Creates the "How to Order" table using the article description we fetched from DB:
        /// WedgeData.Properties["article_description"] (case-insensitive).
        /// Falls back to DrawingData.Metadata["HowToOrderInfo"] if not found.
        /// </summary>
        public bool CreateHowToOrderTable(WedgeData wedge, DrawingData draw, string headerText = "HOW TO ORDER", string tableId = "HowToOrder")
        {
            if (!TryGetCfg(draw, tableId, out var cfg)) return false;

            var description = TryGetArticleDescription(wedge);

            List<string> items;
            if (!string.IsNullOrWhiteSpace(description))
            {
                items = WrapDescription(description, preferredLineLength: 56);
            }
            else
            {
                items = ReadLinesFromMetadata(draw, "HowToOrderInfo");
            }

            if (items.Count == 0) return false;

            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);
            var posM = ToMeters(cfg.PositionMm);

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
            SetTableRowHeights(table, rowHeightMm: 3.048, includeTitle: true);

            TrimTrailingEmptyRows(table);

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

            TrimTrailingEmptyRows(table);

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

            TrimTrailingEmptyRows(table);

            return true;
        }

        private TableAnnotation? CreateOneColumnTable(double xM, double yM, int rows, string title, double colWidthM)
        {
            try
            {
                var table = _drawing.InsertTableAnnotation2(
                    false, xM, yM, 1, "", rows, 1) as TableAnnotation;

                if (table == null) return null;

                table.SetColumnWidth(
                    0,
                    colWidthM,
                    (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);

                table.GridLineWeight = (int)swLineWeights_e.swLW_NONE;
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
                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.TypeFaceName = typeface;
                    if (scaleCharHeight.HasValue && tf.CharHeight > 0)
                        tf.CharHeight *= scaleCharHeight.Value;
                    table.SetTextFormat(false, tf);
                    table.SetTextFormat(true, tf);
                }

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
            catch
            {
            }
        }

        private void SetTableFontSize(TableAnnotation table, double fontSizePoints, bool includeTitle = true)
        {
            try
            {
                double h = PointsToMeters(fontSizePoints);

                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.CharHeight = h;
                    table.SetTextFormat(false, tf);
                    if (includeTitle) table.SetTextFormat(true, tf);
                }

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
            catch
            {
            }
        }

        private static double PointsToMeters(double pt) => pt * 0.0003527777778;

        private static List<string> BuildDimensionRows_Filtered(WedgeData wedge, DrawingData draw, WedgeType wedgeType)
        {
            var rows = new List<string>();

            var subclass = wedge.Subclass;
            var drawingType = draw.DrawingType;

            // null => no filtering for that wedgeType/subclass/drawingType
            var allowed = DimensionTableKeyFilter.GetAllowedKeys(wedgeType, drawingType, subclass);

            foreach (var kv in wedge.Dimensions)
            {
                var key = kv.Key.Value;

                if (allowed != null && !allowed.Contains(key))
                    continue;

                var d = kv.Value;
                bool isAngle = DimensionKeyPolicy.IsAngle(key);

                // Overlay table is handled elsewhere; keep this filtered table focused on Production/Customer behavior
                if (drawingType == DrawingType.Overlay && isAngle)
                    continue;

                if (isAngle)
                {
                    if (!d.Nominal.IsDeg) continue;

                    var deg = d.Nominal.AsDeg();
                    if (deg == 0m) continue;

                    var degStr = deg.ToString("0.###", CultureInfo.InvariantCulture);
                    var tolDeg = FormatTolDeg(d.Tol);
                    var refFlag = IsZeroTol(d.Tol);

                    var text = string.IsNullOrEmpty(tolDeg)
                        ? $"{key}={degStr}°"
                        : $"{key}={degStr}° {tolDeg}";

                    if (refFlag) text += " (REF)";

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

                var refFlagLength = IsZeroTol(d.Tol);

                var left = $"{key}={inchStr}";
                var right = $"[{mmStr}{(string.IsNullOrEmpty(tolMm) ? "" : " " + tolMm)}]";
                var middle = string.IsNullOrEmpty(tolIn) ? "" : " " + tolIn;

                var rowText = (left + middle + " " + right).Trim();
                if (refFlagLength) rowText += " (REF)";

                rows.Add(rowText);
            }

            return rows;
        }

        private static string FormatTolDeg(Tolerance tol)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m))
                return "";

            return FormatTolerancePair(
                Math.Abs(tol.Lower.Value),
                Math.Abs(tol.Upper.Value),
                value => value.ToString("0.###", CultureInfo.InvariantCulture),
                suffix: "°");
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

        /// <summary>
        /// Builds display strings for overlay dimension rows.
        /// Requirements:
        /// - Length dimensions are shown in inches.
        /// - Angle dimensions remain in degrees.
        /// - If lower = 0 and upper = 0 -> mark as REF.
        /// - If lower != upper -> show asymmetric -lower +upper.
        /// - If lower = upper and both are non-zero -> show symmetric ±upper.
        /// - If only one side is non-zero -> show only that side.
        /// </summary>
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

                    var text = string.IsNullOrEmpty(tolText)
                        ? $"{row.Key}={inchStr} (REF)"
                        : $"{row.Key}={inchStr} {tolText}";

                    result.Add(text);
                }
                else if (row.Nominal.IsDeg)
                {
                    var deg = row.Nominal.AsDeg();
                    var degStr = deg.ToString("0.###", CultureInfo.InvariantCulture);

                    var lowerDeg = row.TolLower.IsDeg ? row.TolLower.AsDeg() : 0m;
                    var upperDeg = row.TolUpper.IsDeg ? row.TolUpper.AsDeg() : 0m;

                    var tolText = FormatOverlayAngleToleranceDegrees(lowerDeg, upperDeg);

                    var text = string.IsNullOrEmpty(tolText)
                        ? $"{row.Key}={degStr}° (REF)"
                        : $"{row.Key}={degStr}° {tolText}";

                    result.Add(text);
                }
                else
                {
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
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m))
                return "";

            var lowerIn = MmToIn(Math.Abs(tol.Lower.Value));
            var upperIn = MmToIn(Math.Abs(tol.Upper.Value));

            return FormatTolerancePair(
                lowerIn,
                upperIn,
                value => FormatDecimal(value, "0.0000", removeLeadingZero),
                suffix: "");
        }

        private static string FormatTolMm(Tolerance tol)
        {
            if (tol == null || (tol.Lower.Value == 0m && tol.Upper.Value == 0m))
                return "";

            return FormatTolerancePair(
                Math.Abs(tol.Lower.Value),
                Math.Abs(tol.Upper.Value),
                value => value.ToString("0.###", CultureInfo.InvariantCulture),
                suffix: "");
        }

        private static string FormatOverlayLengthToleranceInches(decimal lowerMm, decimal upperMm)
        {
            var lowerIn = MmToIn(Math.Abs(lowerMm));
            var upperIn = MmToIn(Math.Abs(upperMm));

            return FormatTolerancePair(
                lowerIn,
                upperIn,
                value => FormatDecimal(value, "0.0000", removeLeadingZero: true),
                suffix: "");
        }

        private static string FormatOverlayAngleToleranceDegrees(decimal lowerDeg, decimal upperDeg)
        {
            return FormatTolerancePair(
                Math.Abs(lowerDeg),
                Math.Abs(upperDeg),
                value => value.ToString("0.###", CultureInfo.InvariantCulture),
                suffix: "°");
        }

        private static string FormatTolerancePair(
            decimal lower,
            decimal upper,
            Func<decimal, string> formatValue,
            string suffix)
        {
            // Case A:
            // lower = 0 and upper = 0
            // Caller treats empty tolerance as REF.
            if (lower == 0m && upper == 0m)
                return "";

            // Case C:
            // lower = upper and both are non-zero.
            if (lower == upper)
                return $"±{formatValue(upper)}{suffix}";

            // Case D:
            // lower = 0, upper is non-zero.
            if (lower == 0m)
                return $"+{formatValue(upper)}{suffix}";

            // Case D:
            // upper = 0, lower is non-zero.
            if (upper == 0m)
                return $"-{formatValue(lower)}{suffix}";

            // Case B:
            // lower != upper and both are non-zero.
            return $"-{formatValue(lower)}{suffix} +{formatValue(upper)}{suffix}";
        }

        private static string FormatDecimal(decimal value, string format, bool removeLeadingZero)
        {
            var s = value.ToString(format, CultureInfo.InvariantCulture);
            return removeLeadingZero ? TrimLeadingZero(s) : s;
        }

        private static string? TryGetArticleDescription(WedgeData wedge)
        {
            if (wedge?.Properties == null) return null;
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
            }
        }

        /// <summary>
        /// Removes trailing rows that are completely empty (all cells whitespace).
        /// Defensive against SolidWorks adding an extra row or content pipelines
        /// accidentally leaving a blank line at the end.
        /// </summary>
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
                        var textObj = table.get_Text(r, c);
                        var text = textObj as string ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            allEmpty = false;
                            break;
                        }
                    }

                    if (allEmpty)
                    {
                        table.DeleteRow(r);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Estimates the required column width in meters for a monospaced font table,
        /// based on the longest string across all rows and the header.
        /// 
        /// Monospac821 BT at 6pt with scaleCharHeight=0.90 produces a glyph whose
        /// advance width is roughly 60% of the character height (typical for monospace).
        /// We add a small fixed padding (2mm each side) to avoid the text touching borders.
        /// </summary>
        private static double EstimateMonospaceWidthM(
            IReadOnlyList<string> rows,
            string header,
            double fontSizePt,
            double? scaleCharHeight,
            double charWidthRatio = 0.60,
            double paddingMm = 8.0)
        {
            int maxChars = string.IsNullOrEmpty(header) ? 0 : header.Length;
            foreach (var r in rows)
                if (r != null && r.Length > maxChars)
                    maxChars = r.Length;

            if (maxChars == 0) return 0.03;

            // Character height in meters (same formula as PointsToMeters)
            double charHeightM = fontSizePt * 0.0003527777778;
            if (scaleCharHeight.HasValue)
                charHeightM *= scaleCharHeight.Value;

            double charWidthM = charHeightM * charWidthRatio;
            double contentM = charWidthM * maxChars;
            double paddingM = paddingMm / 1000.0;

            return contentM + paddingM;
        }

        /// <summary>
        /// Computes the total rendered height of the table in meters.
        /// Used to reposition the table anchor so it grows in a predictable direction.
        /// </summary>
        private static double EstimateTableHeightM(int dataRowCount, double rowHeightMm, bool includeTitle)
        {
            int totalRows = dataRowCount + (includeTitle ? 1 : 0); // +1 for header row
            return (totalRows * rowHeightMm) / 1000.0;
        }
    }
}