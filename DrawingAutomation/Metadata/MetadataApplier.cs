using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;                               // Logger
using WAD.Runner.DrawingAutomation.SolidWorks;             // DrawingService
using WAD.Runner.DataManagement.Domain.Drawing;            // DrawingData, ViewConfig
using WAD.Runner.DataManagement.Domain.Wedge;              // WedgeData

namespace WAD.Runner.DrawingAutomation.Metadata
{
    /// <summary>
    /// Applies drawing metadata to Summary Info and these title-block properties:
    /// SWFormatSize, Material, Autor, COMPANY_NAME, TITLE, DRAWING_NUMBER, ADDRESS,
    /// TYPE, SCALING_FRONT_SIDE_TOP_VIEW, DRAWN_BY, DRAWN_ON.
    /// 
    /// For overlay drawings, use <see cref="ApplyOverlay"/> which targets the
    /// overlay-specific properties:
    /// DIMENSIONS, OVERLAY_TITLE, DESCRIPTION, COINING, ENGRAVING_NOTE.
    /// </summary>
    public static class MetadataApplier
    {
        /// <summary>
        /// Standard metadata application (Production / Customer drawings).
        /// </summary>
        public static void Apply(DrawingService ds, DrawingData drawing, WedgeData wedge)
        {
            if (ds is null) throw new ArgumentNullException(nameof(ds));
            if (drawing is null) throw new ArgumentNullException(nameof(drawing));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (ds.Model is null) throw new InvalidOperationException("No active drawing model available.");

            var model = ds.Model;

            // 1) Summary Info (Title, Subject, etc.)
            ApplySummaryInfo(model, drawing, wedge);

            // 2) Custom properties (exact keys as in template for standard drawings)
            var props = BuildTitleBlockProps(drawing, wedge);
            ApplyCustomProperties(model, props);
        }

        /// <summary>
        /// Metadata application for OVERLAY drawings.
        /// Overlay templates use a different set of custom properties:
        /// DIMENSIONS, OVERLAY_TITLE, DESCRIPTION, COINING, ENGRAVING_NOTE.
        /// </summary>
        public static void ApplyOverlay(DrawingService ds, DrawingData drawing, WedgeData wedge)
        {
            if (ds is null) throw new ArgumentNullException(nameof(ds));
            if (drawing is null) throw new ArgumentNullException(nameof(drawing));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (ds.Model is null) throw new InvalidOperationException("No active drawing model available.");

            var model = ds.Model;

            // Summary info is still useful/valid for overlay drawings
            ApplySummaryInfo(model, drawing, wedge);

            // Overlay-specific properties (screenshot: DIMENSIONS, OVERLAY_TITLE, DESCRIPTION, COINING, ENGRAVING_NOTE)
            var overlayProps = BuildOverlayTitleBlockProps(drawing, wedge);
            ApplyCustomProperties(model, overlayProps);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Summary Info
        // ─────────────────────────────────────────────────────────────────────

        private static void ApplySummaryInfo(ModelDoc2 model, DrawingData drawing, WedgeData wedge)
        {
            try
            {
                var md = drawing.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                string number = FirstNonEmpty(
                    Get(md, "number"),
                    Get(md, "drawing_number"),
                    Get(md, "article"),
                    drawing.ArticleNumber);

                string revision = FirstNonEmpty(Get(md, "revision"), Get(md, "rev"));
                string description = FirstNonEmpty(
                    Get(md, "description"),
                    Get(wedge.Properties, "description"));

                string author = FirstNonEmpty(Get(md, "author"), "WAD.Runner");

                string title = string.IsNullOrWhiteSpace(revision)
                    ? $"{number} - {drawing.DrawingType}"
                    : $"{number} Rev {revision} - {drawing.DrawingType}";

                string subject = string.IsNullOrWhiteSpace(description)
                    ? $"{drawing.DrawingType} {number}".Trim()
                    : $"{drawing.DrawingType} {number} — {description}".Trim();

                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoTitle] = title;
                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoSubject] = subject;
                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoAuthor] = author;
                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoSavedBy] = author;
                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoKeywords] = number ?? string.Empty;
                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoComment] = $"{title} created by {author}";
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplySummaryInfo: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Title-block properties (standard drawings)
        // ─────────────────────────────────────────────────────────────────────

        private static IReadOnlyDictionary<string, string> BuildTitleBlockProps(DrawingData drawing, WedgeData wedge)
        {
            var md = drawing.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var wdp = wedge.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            // Scales (Front/Side/Top) if available in DrawingData.Views
            string scaleToken = BuildScaleToken(drawing);

            var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // Compose values with safe fallbacks
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // If provided in metadata, we pass through; otherwise we keep template value (omit)
                ["SWFormatSize"] = FirstNonEmpty(Get(md, "SWFormatSize"), Get(md, "format_size")),
                ["Material"] = FirstNonEmpty(Get(md, "Material"), Get(wdp, "material")),
                ["Autor"] = FirstNonEmpty(Get(md, "Autor"), Get(md, "author"), "WAD.Runner"),
                ["COMPANY_NAME"] = FirstNonEmpty(Get(md, "COMPANY_NAME"), Get(md, "company"), Get(wdp, "company")),
                ["TITLE"] = BuildTitle(md, drawing),
                ["DRAWING_NUMBER"] = FirstNonEmpty(Get(md, "DRAWING_NUMBER"), Get(md, "number"), drawing.ArticleNumber),
                ["ADDRESS"] = FirstNonEmpty(Get(md, "ADDRESS"), Get(wdp, "address")),
                ["TYPE"] = FirstNonEmpty(Get(md, "TYPE"), drawing.DrawingType.ToString()),
                ["SCALING_FRONT_SIDE_TOP_VIEW"] = scaleToken,
                ["DRAWN_BY"] = FirstNonEmpty(Get(md, "DRAWN_BY"), Get(md, "author"), "WAD.Runner"),
                ["DRAWN_ON"] = FirstNonEmpty(Get(md, "DRAWN_ON"), today)
            };

            // Remove keys where value is empty to avoid overwriting template defaults accidentally
            var cleaned = map.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                             .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            return cleaned;
        }

        /// <summary>
        /// Build overlay-specific title block properties:
        /// DIMENSIONS, OVERLAY_TITLE, DESCRIPTION, COINING, ENGRAVING_NOTE.
        /// </summary>
        private static IReadOnlyDictionary<string, string> BuildOverlayTitleBlockProps(DrawingData drawing, WedgeData wedge)
        {
            var md = drawing.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var wdp = wedge.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // DIMENSIONS column text (e.g. a short summary or table key)
                ["DIMENSIONS"] = FirstNonEmpty(
                    Get(md, "DIMENSIONS"),
                    Get(md, "dimensions"),
                    Get(wdp, "dimensions")),

                // OVERLAY_TITLE specific to overlay drawings
                ["OVERLAY_TITLE"] = FirstNonEmpty(
                    BuildTitle(md, drawing)),

                // DESCRIPTION – multi-line version for overlay drawings
                ["DESCRIPTION"] = BuildOverlayDescription(wdp),

                // COINING (from DB/template)
                ["COINING"] = FirstNonEmpty(
                    Get(md, "COINING"),
                    Get(wdp, "coining")),

                // ENGRAVING_NOTE (default if nothing provided)
                ["ENGRAVING_NOTE"] = FirstNonEmpty(
                    Get(md, "ENGRAVING_NOTE"),
                    Get(md, "engraving_note"),
                    Get(wdp, "engraving_note"),
                    "ENGRAVED PER DWG")
            };

            var cleaned = map.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                             .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            return cleaned;
        }

        private static string BuildTitle(IReadOnlyDictionary<string, string?> md, DrawingData drawing)
        {
            var explicitTitle = Get(md, "TITLE");
            if (!string.IsNullOrWhiteSpace(explicitTitle)) return explicitTitle!;

            var number = FirstNonEmpty(Get(md, "number"), Get(md, "drawing_number"), drawing.ArticleNumber);
            var revision = FirstNonEmpty(Get(md, "revision"), Get(md, "rev"));
            return string.IsNullOrWhiteSpace(revision)
                ? $"{number}TF"
                : $"{number} Rev {revision} - {drawing.DrawingType}";
        }

        private static string BuildScaleToken(DrawingData drawing)
        {
            // Compose "Front;Side;Top" (or empty if none available).
            // We keep raw scale (as configured) — adjust format if your block expects something else (e.g., "3:1").
            string fmt(double s) => s == 0 ? "1" : s.ToString("0.###", CultureInfo.InvariantCulture);

            drawing.Views.TryGetValue("Front", out ViewConfig? vFront);
            drawing.Views.TryGetValue("Side", out ViewConfig? vSide);
            drawing.Views.TryGetValue("Top", out ViewConfig? vTop);

            var parts = new List<string>();
            if (vFront != null) parts.Add(fmt(vFront.Scale));
            if (vSide != null) parts.Add(fmt(vSide.Scale));
            if (vTop != null) parts.Add(fmt(vTop.Scale));

            return parts.Count > 0 ? string.Join(";", parts) : string.Empty;
        }

        private static void ApplyCustomProperties(ModelDoc2 model, IReadOnlyDictionary<string, string> props)
        {
            try
            {
                var ext = model.Extension ?? throw new InvalidOperationException("Model.Extension is null.");
                var mgr = ext.get_CustomPropertyManager("")
                          ?? throw new InvalidOperationException("CustomPropertyManager is null.");

                foreach (var kv in props)
                {
                    try
                    {
                        int rc = mgr.Set2(kv.Key, kv.Value ?? string.Empty); // create/overwrite
                        if (rc != (int)swCustomInfoSetResult_e.swCustomInfoSetResult_OK)
                            Logger.Warn($"Custom property '{kv.Key}' set returned {rc}.");
                    }
                    catch (Exception exInner)
                    {
                        Logger.Warn($"Set custom property '{kv.Key}' failed: {exInner.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"ApplyCustomProperties: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string FirstNonEmpty(params string?[] candidates)
            => candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;

        private static string? Get(IReadOnlyDictionary<string, string?>? dict, string key)
            => (dict != null && dict.TryGetValue(key, out var v)) ? v : null;

        /// <summary>
        /// Builds a multi-line DESCRIPTION for overlay drawings.
        /// Uses '\n' so the linked note ($PRPSHEET:"DESCRIPTION") can render
        /// multiple lines in the overlay title block.
        /// </summary>
        private static string BuildOverlayDescription(IReadOnlyDictionary<string, string?> wdp)
        {
            var raw = FirstNonEmpty(
                Get(wdp, "article_description"),
                Get(wdp, "description"));

            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            const int maxLineLength = 40; // tweak to fit your overlay title-block width
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            var currentLen = 0;

            foreach (var word in words)
            {
                var extra = (currentLen == 0 ? 0 : 1) + word.Length; // +1 for space
                if (currentLen + extra > maxLineLength)
                {
                    sb.Append('\n');          // newline for SolidWorks note
                    sb.Append(word);
                    currentLen = word.Length;
                }
                else
                {
                    if (currentLen > 0)
                    {
                        sb.Append(' ');
                        currentLen++;
                    }

                    sb.Append(word);
                    currentLen += word.Length;
                }
            }

            return sb.ToString();
        }
    }
}
