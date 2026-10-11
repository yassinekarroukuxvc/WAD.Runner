using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DataManagement.Domain.Config;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Infrastructure.Mapping;
using WAD.Runner.DrawingAutomation.Overlay;

using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Tables
{
    public sealed class TableService
    {
        private const double Eps = 1e-6;

        // Row heights (mm)
        private const double DimTableRowHeightMm = 2.8;
        private const double HowToOrderRowHeightMm = 3.048;
        private const double OverlayRowHeightMm = 2.5;

        // Font sizes (points — converted via PointsToMeters for SW)
        private const double DimTableFontSizePt = 5.0;
        private const double HowToOrderFontSizePt = 6.0;
        // Measured on 4516 drawings: 6 pt bold text takes about 1.52 mm per character.
        private const double HowToOrderCharWidthRatio = 0.72;
        private const double HowToOrderPaddingMm = 3.0;
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
            if (wedge is null)
                throw new ArgumentNullException(nameof(wedge));

            if (draw is null)
                throw new ArgumentNullException(nameof(draw));

            if (!TryGetCfg(draw, tableId, out var cfg))
            {
                var configuredTables = draw.Tables == null
                    ? "<none>"
                    : string.Join(", ", draw.Tables.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

                Logger.Warn(
                    $"[Tables] '{tableId}' config missing/incomplete -> " +
                    $"wedge={wedgeType}, subclass={wedge.Subclass}, drawingType={draw.DrawingType}. " +
                    $"Configured table ids=[{configuredTables}].");
                return false;
            }

            var allowed = DrawingWedgeModuleRegistry
                .Get(wedgeType)
                .GetAllowedDimensionTableKeys(wedge.Subclass, draw.DrawingType);

            var rows = BuildDimensionRows_Filtered(wedge, draw, wedgeType);

            Logger.Info(
                $"[Tables] Dimension rows built -> " +
                $"wedge={wedgeType}, subclass={wedge.Subclass}, drawingType={draw.DrawingType}, " +
                $"sourceDimensions={wedge.Dimensions?.Count ?? 0}, " +
                $"allowedKeys={(allowed?.Count.ToString(CultureInfo.InvariantCulture) ?? "<unfiltered>")}, " +
                $"rows={rows.Count}.");

            if (rows.Count == 0)
            {
                var actualKeys = wedge.Dimensions is null
                    ? "<none>"
                    : string.Join(
                        ", ",
                        wedge.Dimensions
                            .Select(kv =>
                                $"{kv.Key.Value}={kv.Value.Nominal.Value.ToString(CultureInfo.InvariantCulture)} {kv.Value.Nominal.Unit}")
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

                var allowedKeys = allowed is null
                    ? "<unfiltered>"
                    : string.Join(", ", allowed.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

                Logger.Warn(
                    $"[Tables] '{tableId}' has ZERO writable dimension rows -> " +
                    $"wedge={wedgeType}, subclass={wedge.Subclass}, drawingType={draw.DrawingType}. " +
                    $"Allowed=[{allowedKeys}]. Actual=[{actualKeys}]. " +
                    "Rows are only written for non-zero dimensions whose units match the expected length/angle units.");
                return false;
            }

            if (WedgeEnhancementPolicy.SplitDimensionTables(wedgeType) &&
                (DrawingWedgeModuleRegistry.Get(wedgeType).Behavior.UseSplitDimensionTables?.Invoke(wedge, draw) ?? true))
                return CreateSplitDimensionTables(wedge, rows, cfg.PositionMm);

            var configWidthM = ResolveWidthM(cfg, fallbackM: 0.14);
            var contentWidthM = EstimateMonospaceWidthM(rows, header,
                fontSizeM: PointsToMeters(DimTableFontSizePt),
                charWidthRatio: 0.8);
            var widthM = Math.Min(configWidthM, Math.Max(contentWidthM, 0.03));

            var posM = ToMeters(cfg.PositionMm);

            double tableHeightM = EstimateTableHeightM(rows.Count, DimTableRowHeightMm, includeTitle: true);
            double anchorY = posM.y + tableHeightM + TableAnchorGapM;

            Logger.Info(
                $"[Tables] Inserting '{tableId}' -> " +
                $"x={posM.x * 1000.0:0.###} mm, yAnchor={anchorY * 1000.0:0.###} mm, " +
                $"width={widthM * 1000.0:0.###} mm, rows={rows.Count + 1}.");

            var table = CreateOneColumnTable(posM.x, anchorY, rows.Count + 1, "Dimensions", widthM);
            if (table is null)
            {
                Logger.Warn(
                    $"[Tables] SolidWorks did not create '{tableId}' -> " +
                    $"wedge={wedgeType}, subclass={wedge.Subclass}, drawingType={draw.DrawingType}.");
                return false;
            }

            table.set_Text(0, 0, header);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i + 1, 0, rows[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, DimTableFontSizePt);
            TryApplyTypeface(table, DrawingTextStyle.Typeface, DrawingTextStyle.Bold);
            SetAllRowHeights(table, DimTableRowHeightMm, includeTitle: true);
            TrimTrailingEmptyRows(table);

            // SolidWorks may draw rows taller than requested (minimum height for the font), so the
            // estimated anchor can push the bottom rows off the sheet. Re-anchor on the real height
            // so the table bottom always sits at the configured position.
            KeepBottomAt(table, posM.x, posM.y + TableAnchorGapM, anchorY);

            Logger.Success(
                $"[Tables] '{tableId}' inserted successfully -> " +
                $"wedge={wedgeType}, subclass={wedge.Subclass}, drawingType={draw.DrawingType}, dataRows={rows.Count}.");

            return true;
        }

        private bool CreateSplitDimensionTables(WedgeData wedge, IReadOnlyList<string> selectedRows, double[] positionMm)
        {
            var split = (selectedRows.Count + 1) / 2;
            var left = selectedRows.Take(split).ToArray();
            var right = selectedRows.Skip(split).ToArray();
            // Both halves share one width, fitted to the longest row of either.
            var widthM = SplitDimensionTableWidthM(selectedRows);
            var firstCreated = CreateDimensionTableHalf(left, positionMm, widthM);
            if (!firstCreated || right.Length == 0) return firstCreated;
            // Keep the second table above the title block and align its header with the first.
            var secondPosition = new[] { positionMm[0] + widthM * 1000.0 + 4.0, positionMm[1] + (left.Length - right.Length) * 3.5 };
            return CreateDimensionTableHalf(right, secondPosition, widthM);
        }

        // Tight to the longest "KEY=nominal ±tol [mm ±tol]" row on one line: 6 pt text measures
        // about 1.31 mm per character (ratio 0.62); 3 mm covers the cell margins.
        private static double SplitDimensionTableWidthM(IReadOnlyList<string> rows)
            => EstimateMonospaceWidthM(rows, "DIMENSIONS", PointsToMeters(6.0),
                charWidthRatio: 0.62, paddingMm: 3.0);

        private bool CreateDimensionTableHalf(IReadOnlyList<string> selectedRows, double[] positionMm, double widthM)
        {
            var position = ToMeters(positionMm);
            var anchorY = position.y + (selectedRows.Count + 1) * 0.0035 + TableAnchorGapM;
            var table = _drawing.InsertTableAnnotation2(false, position.x, anchorY, 1, "",
                selectedRows.Count + 1, 1) as TableAnnotation;
            if (table is null) return false;
            table.TitleVisible = false;
            table.SetColumnWidth(0, widthM, (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);
            table.set_Text(0, 0, "DIMENSIONS");
            for (var row = 0; row < selectedRows.Count; row++)
            {
                table.set_Text(row + 1, 0, selectedRows[row]);
                table.CellTextHorizontalJustification[row + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }
            SetTableFontSize(table, 6.0);
            TryApplyTypeface(table, DrawingTextStyle.Typeface, DrawingTextStyle.Bold);
            SetAllRowHeights(table, 3.5, includeTitle: true);
            table.BorderLineWeight = (int)swLineWeights_e.swLW_THIN;
            table.GridLineWeight = (int)swLineWeights_e.swLW_NONE;
            KeepBottomAt(table, position.x, position.y + TableAnchorGapM, anchorY);
            Logger.Success($"[Tables] Split dimension table inserted -> dataRows={selectedRows.Count}, width={widthM * 1000.0:0.##} mm, font=6 pt.");
            return true;
        }

        public bool CreateOverlayDimensionTableAt(
            IReadOnlyList<OverlayDimensionRow> dimensions,
            double xMm,
            double yMm,
            double widthMm = 30.0,
            string header = "DIMENSIONS")
        {
            if (dimensions == null || dimensions.Count == 0)
            {
                Logger.Warn("[Tables/Overlay] No overlay dimensions were supplied; table creation skipped.");
                return false;
            }

            var rows = BuildOverlayDimensionRowStrings(dimensions);
            if (rows.Count == 0)
            {
                var supplied = string.Join(
                    ", ",
                    dimensions.Select(d => $"{d.Key}={d.Nominal.Value.ToString(CultureInfo.InvariantCulture)} {d.Nominal.Unit}"));

                Logger.Warn(
                    $"[Tables/Overlay] Overlay dimensions produced ZERO writable rows. " +
                    $"Supplied=[{supplied}].");
                return false;
            }

            if (!double.IsFinite(xMm) || !double.IsFinite(yMm))
            {
                Logger.Warn(
                    $"[Tables/Overlay] Invalid table position: x={xMm}, y={yMm} mm.");
                return false;
            }

            if (!double.IsFinite(widthMm) || widthMm <= 0.0)
                widthMm = 30.0;

            Logger.Info(
                $"[Tables/Overlay] Inserting overlay dimension table -> " +
                $"x={xMm:0.###} mm, y={yMm:0.###} mm, width={widthMm:0.###} mm, rows={rows.Count}.");

            var table = CreateOneColumnTable(
                xMm / 1000.0, yMm / 1000.0,
                rows.Count, "OverlayDimensions",
                widthMm / 1000.0);
            if (table is null)
            {
                Logger.Warn("[Tables/Overlay] SolidWorks did not create the overlay dimension table.");
                return false;
            }

            table.TitleVisible = false;

            for (int i = 0; i < rows.Count; i++)
            {
                table.set_Text(i, 0, rows[i]);
                table.CellTextHorizontalJustification[i, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, OverlayFontSizePt);
            TryApplyTypeface(table, DrawingTextStyle.Typeface, DrawingTextStyle.Bold);
            SetAllRowHeights(table, OverlayRowHeightMm, includeTitle: true);
            SetTableLayer(table, "annotation");
            HideAllTableBorders(table);
            TrimTrailingEmptyRows(table);
            AlignRowGapsWithSheetLines(table, xMm, yMm, widthMm);

            Logger.Success(
                $"[Tables/Overlay] Overlay dimension table inserted successfully -> dataRows={rows.Count}.");

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

            // "right_edge_mm": size the table to the description on one line and keep its right
            // edge there (PositionMm[1] stays the top). Lines wrap only past max_width_mm.
            var rightAnchored = cfg.Params.TryGetValue("right_edge_mm", out var rightEdgeMm) && rightEdgeMm > 0;
            var maxWidthMm = cfg.Params.TryGetValue("max_width_mm", out var maxMm) && maxMm > 0 ? maxMm : 150.0;
            var charWidthMm = PointsToMeters(HowToOrderFontSizePt) * HowToOrderCharWidthRatio * 1000.0;
            var lineLength = rightAnchored
                ? Math.Max(20, (int)((maxWidthMm - HowToOrderPaddingMm) / charWidthMm))
                : 56;

            var items = !string.IsNullOrWhiteSpace(description)
                ? WrapDescription(description, preferredLineLength: lineLength)
                : ReadLinesFromMetadata(draw, "HowToOrderInfo");

            if (items.Count == 0) return false;

            var posM = ToMeters(cfg.PositionMm);
            var widthM = ResolveWidthM(cfg, fallbackM: 0.08);
            if (rightAnchored)
            {
                widthM = EstimateMonospaceWidthM(items, headerText, PointsToMeters(HowToOrderFontSizePt),
                    charWidthRatio: HowToOrderCharWidthRatio, paddingMm: HowToOrderPaddingMm);
                posM = (rightEdgeMm / 1000.0 - widthM, posM.y);
            }

            var table = CreateOneColumnTable(posM.x, posM.y, items.Count + 1, "HowToOrder", widthM);
            if (table is null) return false;
            table.TitleVisible = false;

            table.set_Text(0, 0, headerText);
            table.CellTextHorizontalJustification[0, 0] = (int)swTextJustification_e.swTextJustificationLeft;

            for (int i = 0; i < items.Count; i++)
            {
                table.set_Text(i + 1, 0, items[i]);
                table.CellTextHorizontalJustification[i + 1, 0] = (int)swTextJustification_e.swTextJustificationLeft;
            }

            SetTableFontSize(table, HowToOrderFontSizePt);
            TryApplyTypeface(table, DrawingTextStyle.Typeface, DrawingTextStyle.Bold);
            SetAllRowHeights(table, HowToOrderRowHeightMm, includeTitle: true);
            table.BorderLineWeight = (int)swLineWeights_e.swLW_THIN;
            table.GridLineWeight = (int)swLineWeights_e.swLW_THIN;

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

                if (table == null)
                {
                    Logger.Warn(
                        $"[Tables] InsertTableAnnotation2 returned null -> " +
                        $"title='{title}', x={xM:0.######} m, y={yM:0.######} m, " +
                        $"rows={rows}, width={colWidthM:0.######} m.");
                    return null;
                }

                table.SetColumnWidth(
                    0, colWidthM,
                    (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange);

                table.GridLineWeight = (int)swLineWeights_e.swLW_NONE;
                table.Title = title;
                table.TitleVisible = true;

                return table;
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"[Tables] SolidWorks table insertion threw an exception -> " +
                    $"title='{title}', x={xM:0.######} m, y={yM:0.######} m, " +
                    $"rows={rows}, width={colWidthM:0.######} m. Error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Moves a top-anchored, borderless table by at most half a row so that a visible horizontal
        /// sheet-format line crossing it (e.g. the overlay's centerline) runs between two rows
        /// instead of through a row's text. Uses the rows' actual heights: SolidWorks enlarges
        /// rows beyond the requested height to fit the font.
        /// </summary>
        private void AlignRowGapsWithSheetLines(TableAnnotation table, double xMm, double topMm, double widthMm)
        {
            var boundariesMm = new List<double> { topMm };
            for (int r = 0; r < table.RowCount; r++)
                if (!table.RowHidden[r]) boundariesMm.Add(boundariesMm[^1] - table.GetRowHeight(r) * 1000.0);
            double bottomMm = boundariesMm[^1];

            var crossingsMm = new List<double>();
            var layers = (LayerMgr)_model.GetLayerManager();
            _drawing.EditTemplate();
            try
            {
                var segments = ((Sheet)_drawing.GetCurrentSheet()).GetTemplateSketch()?.GetSketchSegments() as object[];
                foreach (var line in (segments ?? Array.Empty<object>()).OfType<SketchLine>())
                {
                    var layerName = ((SketchSegment)line).Layer;
                    if (!string.IsNullOrEmpty(layerName) && layers.GetLayer(layerName) is Layer { Visible: false }) continue;
                    var a = (SketchPoint)line.GetStartPoint2();
                    var b = (SketchPoint)line.GetEndPoint2();
                    double yLineMm = a.Y * 1000.0;
                    if (Math.Abs(a.Y - b.Y) > 1e-7 || yLineMm >= topMm || yLineMm <= bottomMm) continue;
                    if (Math.Max(a.X, b.X) * 1000.0 <= xMm || Math.Min(a.X, b.X) * 1000.0 >= xMm + widthMm) continue;
                    crossingsMm.Add(yLineMm);
                }
            }
            finally { _drawing.EditSheet(); }

            if (crossingsMm.Count == 0) return;
            if (crossingsMm.Distinct().Count() > 1)
                Logger.Warn($"[Tables/Overlay] {crossingsMm.Distinct().Count()} sheet lines cross the dimension table; aligning the gap with the topmost only.");

            double lineMm = crossingsMm.Max();
            double nearest = boundariesMm.OrderBy(y => Math.Abs(y - lineMm)).First();
            double shiftMm = lineMm - nearest;
            if (Math.Abs(shiftMm) < 1e-3) return;

            var annotation = (Annotation)table.GetAnnotation();
            var position = (double[])annotation.GetPosition();
            if (!annotation.SetPosition2(position[0], position[1] + shiftMm / 1000.0, 0))
                throw new InvalidOperationException("Cannot move the overlay dimension table off the sheet line.");
            Logger.Info(FormattableString.Invariant(
                $"[Tables/Overlay] Moved the dimension table {shiftMm:+0.###;-0.###} mm so the sheet line at y={lineMm:0.###} mm runs between two rows."));
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
        private void TryApplyTypeface(TableAnnotation table, string typeface, bool bold = false)
        {
            try
            {
                var tf = table.GetTextFormat() as TextFormat;
                if (tf != null)
                {
                    tf.TypeFaceName = typeface;
                    tf.Bold = bold;
                    table.SetTextFormat(false, tf);
                    table.SetTextFormat(true, tf);
                }

                for (int r = 0; r < table.RowCount; r++)
                    for (int c = 0; c < table.ColumnCount; c++)
                    {
                        var cellTf = table.GetCellTextFormat(r, c) as TextFormat;
                        if (cellTf == null) continue;
                        cellTf.TypeFaceName = typeface;
                        cellTf.Bold = bold;
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

        private void KeepBottomAt(TableAnnotation table, double xM, double bottomYM, double currentTopYM)
        {
            try
            {
                double actualHeightM = 0.0;
                for (int r = 0; r < table.RowCount; r++)
                    actualHeightM += Math.Max(0.0, table.GetRowHeight(r));

                if (actualHeightM <= Eps)
                    return;

                double topYM = bottomYM + actualHeightM;
                if (Math.Abs(topYM - currentTopYM) <= 0.0002)
                    return;

                if (table.GetAnnotation() is Annotation ann && ann.SetPosition2(xM, topYM, 0.0))
                {
                    Logger.Info(
                        $"[Tables] Re-anchored on measured height {actualHeightM * 1000.0:0.##} mm -> " +
                        $"top y={topYM * 1000.0:0.##} mm (estimated top was {currentTopYM * 1000.0:0.##} mm).");
                    _model?.GraphicsRedraw2();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[Tables] Could not re-anchor table on its measured height: {ex.Message}");
            }
        }

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
            if (cfg?.SizeMm is { Length: >= 1 } &&
                double.IsFinite(cfg.SizeMm[0]) &&
                cfg.SizeMm[0] > 0.0)
            {
                return cfg.SizeMm[0] / 1000.0;
            }

            if (cfg?.Params != null &&
                cfg.Params.TryGetValue("widthMm", out var wmm) &&
                double.IsFinite(wmm) &&
                wmm > 0.0)
            {
                return wmm / 1000.0;
            }

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
            var allowed = DrawingWedgeModuleRegistry
                .Get(wedgeType)
                .GetAllowedDimensionTableKeys(wedge.Subclass, drawingType);

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
