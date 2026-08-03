using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DrawingAutomation.Metadata
{


    public static class MetadataApplier
    {


        public static void Apply(DrawingService ds, DrawingData drawing, WedgeData wedge)
        {
            if (ds is null) throw new ArgumentNullException(nameof(ds));
            if (drawing is null) throw new ArgumentNullException(nameof(drawing));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (ds.Model is null) throw new InvalidOperationException("No active drawing model available.");

            var model = ds.Model;


            ApplySummaryInfo(model, drawing, wedge);


            var props = BuildTitleBlockProps(drawing, wedge);
            ApplyCustomProperties(model, props);

            ApplyDrawingTypeMetadata(
                model,
                drawing.DrawingType);
        }


        public static void ApplyOverlay(
            DrawingService ds,
            DrawingData drawing,
            WedgeData wedge,
            WedgeType wedgeType)
        {
            if (ds is null) throw new ArgumentNullException(nameof(ds));
            if (drawing is null) throw new ArgumentNullException(nameof(drawing));
            if (wedge is null) throw new ArgumentNullException(nameof(wedge));
            if (ds.Model is null) throw new InvalidOperationException("No active drawing model available.");

            var model = ds.Model;


            ApplySummaryInfo(model, drawing, wedge);


            var overlayProps = BuildOverlayTitleBlockProps(drawing, wedge, wedgeType);
            ApplyCustomProperties(model, overlayProps);
        }


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

                model.SummaryInfo[(int)swSummInfoField_e.swSumInfoTitle] = description;
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


        private static IReadOnlyDictionary<string, string> BuildTitleBlockProps(DrawingData drawing, WedgeData wedge)
        {
            var md = drawing.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var wdp = wedge.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            string scaleToken = BuildScaleToken(drawing);
            var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SWFormatSize"] = FirstNonEmpty(Get(md, "SWFormatSize"), Get(md, "format_size")),
                ["Material"] = FirstNonEmpty(Get(md, "Material"), Get(wdp, "material")),
                ["Autor"] = FirstNonEmpty(Get(md, "Autor"), Get(md, "author"), "WAD.Runner"),
                ["COMPANY_NAME"] = FirstNonEmpty(Get(md, "COMPANY_NAME"), Get(md, "company"), Get(wdp, "company")),
                ["TITLE"] = BuildStandardTitleText(wdp),
                ["DRAWING_NUMBER"] = FirstNonEmpty(Get(md, "DRAWING_NUMBER"), Get(md, "number"), drawing.ArticleNumber),
                ["ADDRESS"] = FirstNonEmpty(Get(md, "ADDRESS"), Get(wdp, "address")),
                ["TYPE"] = FirstNonEmpty(Get(md, "TYPE"), drawing.DrawingType.ToString()),
                ["SCALING_FRONT_SIDE_TOP_VIEW"] = scaleToken,
                ["DRAWN_BY"] = FirstNonEmpty(Get(md, "DRAWN_BY"), Get(md, "author"), "WAD.Runner"),
                ["DRAWN_ON"] = FirstNonEmpty(Get(md, "DRAWN_ON"), today)
            };

            return map.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }


        private static IReadOnlyDictionary<string, string> BuildOverlayTitleBlockProps(
            DrawingData drawing,
            WedgeData wedge,
            WedgeType wedgeType)
        {
            var md = drawing.Metadata ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var wdp = wedge.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            var overlayTitle = BuildTitle(md, drawing);
            var generationDate = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var overlayDrawingNumber = BuildOverlayDrawingNumber(overlayTitle);

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DIMENSIONS"] = FirstNonEmpty(
                    Get(md, "DIMENSIONS"),
                    Get(md, "dimensions"),
                    Get(wdp, "dimensions")),

                ["OVERLAY_TITLE"] = overlayTitle,

                ["DESCRIPTION"] = BuildOverlayDescription(wedge, wedgeType),

                ["COINING"] = BuildCoiningText(
                    FirstNonEmpty(
                        Get(md, "COINING"),
                        Get(wdp, "Wed-Coining"))),

                ["ENGRAVING_NOTE"] = FirstNonEmpty(
                    Get(md, "ENGRAVING_NOTE"),
                    Get(md, "engraving_note"),
                    Get(wdp, "engraving_note"),
                    "ENGRAVED PER DWG"),

                ["ACAD FILE #"] = overlayTitle,
                ["DATE"] = generationDate,
                ["DRAWING #"] = overlayDrawingNumber
            };

            return map.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                      .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        private static string BuildTitle(IReadOnlyDictionary<string, string?> md, DrawingData drawing)
        {
            var explicitTitle = Get(md, "TITLE");
            if (!string.IsNullOrWhiteSpace(explicitTitle))
                return explicitTitle!;

            var number = FirstNonEmpty(Get(md, "number"), Get(md, "drawing_number"), drawing.ArticleNumber);
            var revision = FirstNonEmpty(Get(md, "revision"), Get(md, "rev"));

            return string.IsNullOrWhiteSpace(revision)
                ? $"{number}TF"
                : $"{number} Rev {revision} - {drawing.DrawingType}";
        }

        private static string BuildOverlayDrawingNumber(string overlayTitle)
        {
            if (string.IsNullOrWhiteSpace(overlayTitle))
                return string.Empty;

            var baseTitle = overlayTitle.Trim();


            baseTitle = Regex.Replace(
                baseTitle,
                @"(?:[\s\-_]*)TF\s*$",
                string.Empty,
                RegexOptions.IgnoreCase);

            baseTitle = baseTitle.Trim().TrimEnd('-', '_', ' ');

            return string.IsNullOrWhiteSpace(baseTitle)
                ? string.Empty
                : $"{baseTitle}-DW";
        }

        private static string BuildScaleToken(DrawingData drawing)
        {
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
                        int rc = mgr.Set2(kv.Key, kv.Value ?? string.Empty);
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


        private static string FirstNonEmpty(params string?[] candidates)
            => candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;

        private static string? Get(IReadOnlyDictionary<string, string?>? dict, string key)
            => (dict != null && dict.TryGetValue(key, out var v)) ? v : null;


        private static string BuildOverlayDescription(WedgeData wedge, WedgeType wedgeType)
        {
            var wdp = wedge.Properties ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var raw = GetCleanDescriptionText(wdp);
            var magnificationText = BuildOverlayMagnificationText(wedge, wedgeType);

            if (string.IsNullOrWhiteSpace(raw))
                return magnificationText;

            const int maxLineLength = 40;
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            var currentLen = 0;

            foreach (var word in words)
            {
                var extra = (currentLen == 0 ? 0 : 1) + word.Length;
                if (currentLen + extra > maxLineLength)
                {
                    sb.Append('\n');
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

            if (!string.IsNullOrWhiteSpace(magnificationText))
            {
                sb.Append('\n');
                sb.Append(magnificationText);
            }

            return sb.ToString();
        }

        private static string BuildOverlayMagnificationText(WedgeData wedge, WedgeType wedgeType)
        {
            var magnification = ComputeOverlayMagnification(wedge, wedgeType);
            var magToken = NormalizeOverlayMagnificationToken(magnification);
            return $"{magToken}X";
        }


        private static double ComputeOverlayMagnification(WedgeData wedge, WedgeType wedgeType)
            => OverlayMagnificationService.ComputeMagnification(wedge, wedgeType);

        private static int NormalizeOverlayMagnificationToken(double magnification)
        {
            if (double.IsNaN(magnification) || double.IsInfinity(magnification))
                return 100;

            return (int)Math.Round(magnification);
        }


        private static string BuildCoiningText(string? rawCoining)
        {
            if (string.IsNullOrWhiteSpace(rawCoining))
                return string.Empty;

            var firstToken = rawCoining
                .Split(';', StringSplitOptions.None)
                .FirstOrDefault()?
                .Trim();

            if (string.IsNullOrWhiteSpace(firstToken))
                return string.Empty;

            return $"{firstToken}";
        }

        private static string GetCleanDescriptionText(IReadOnlyDictionary<string, string?> wdp)
        {
            var raw = FirstNonEmpty(
                Get(wdp, "article_description"),
                Get(wdp, "description"));

            return string.IsNullOrWhiteSpace(raw)
                ? string.Empty
                : raw.Trim().TrimEnd(';');
        }

        private static string BuildStandardTitleText(IReadOnlyDictionary<string, string?> wdp)
        {
            var raw = GetCleanDescriptionText(wdp);
            return WrapToSecondLine(raw, 40);
        }

        private static string WrapToSecondLine(string text, int maxFirstLineLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Trim();

            if (text.Length <= maxFirstLineLength)
                return text;

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1)
                return text;

            var firstLine = new StringBuilder();
            var secondLineStartIndex = 0;

            for (int i = 0; i < words.Length; i++)
            {
                int extra = (firstLine.Length == 0 ? 0 : 1) + words[i].Length;
                if (firstLine.Length > 0 && firstLine.Length + extra > maxFirstLineLength)
                {
                    secondLineStartIndex = i;
                    break;
                }

                if (firstLine.Length > 0)
                    firstLine.Append(' ');

                firstLine.Append(words[i]);
                secondLineStartIndex = i + 1;
            }

            if (secondLineStartIndex >= words.Length)
                return text;

            var secondLine = string.Join(" ", words.Skip(secondLineStartIndex));
            return $"{firstLine}\n{secondLine}";
        }

        private static void ApplyDrawingTypeMetadata(
            ModelDoc2 model,
            DrawingType drawingType)
        {
            var customerDrawingValue = string.Empty;
            var productionDrawingValue = string.Empty;

            switch (drawingType)
            {
                case DrawingType.Customer:
                    customerDrawingValue = "**";
                    break;

                case DrawingType.Production:
                    productionDrawingValue = "**";
                    break;
            }

            /*
             * Do not filter empty values here.
             *
             * The empty value is intentional and ensures that
             * an old marker from a reused drawing template is
             * removed.
             */
            var drawingTypeProps =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["Customer_drawing"] =
                        customerDrawingValue,

                    ["Production_drawing"] =
                        productionDrawingValue
                };

            ApplyCustomProperties(
                model,
                drawingTypeProps);

            Logger.Info(
                "[MetadataApplier] Drawing type metadata applied -> " +
                $"DrawingType={drawingType}, " +
                $"Customer_drawing='{customerDrawingValue}', " +
                $"Production_drawing='{productionDrawingValue}'.");
        }
    }
}
