using System;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Common
{
    /// <summary>
    /// Annotation font for production, customer and overlay drawings: ISOCP lettering (ISOCPEUR), bold.
    ///
    /// Plain "ISOCP" is a hairline font: in the PDF it prints so thin that decimal points almost
    /// vanish, and synthetic bold does not survive. ISOCPEUR is the same ISO lettering as a
    /// filled TrueType font, so it prints solid and takes bold. (The ISOCP_IV25/IV50 files are
    /// not registered fonts; SolidWorks silently falls back to Arial for them.)
    ///
    /// Only the typeface and weight change; every annotation keeps its own text height.
    /// Tables are styled by <see cref="Tables.TableService"/> (per-cell formats), so they are
    /// skipped here. The sheet format (title block) is not touched.
    /// </summary>
    public static class DrawingTextStyle
    {
        public const string Typeface = "ISOCPEUR";
        public const bool Bold = true;

        public static void ApplyToAnnotations(ModelDoc2? model)
        {
            if (model is not DrawingDoc drawing)
                return;

            var defaults = ApplyDocumentDefaults(model);
            var (annotations, formats, failed) = ApplyToExistingAnnotations(drawing);

            Logger.Info(
                $"[TextStyle] {Typeface}{(Bold ? " bold" : "")} -> document defaults={defaults}, " +
                $"annotations={annotations}, text formats={formats}, failed={failed}.");
        }

        private static int ApplyDocumentDefaults(ModelDoc2 model)
        {
            var ext = model.Extension;
            var count = 0;

            foreach (swUserPreferenceTextFormat_e setting in Enum.GetValues(typeof(swUserPreferenceTextFormat_e)))
            {
                try
                {
                    if (ext.GetUserPreferenceTextFormat(
                            (int)setting,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified) is not TextFormat tf)
                        continue;

                    Restyle(tf);

                    if (ext.SetUserPreferenceTextFormat(
                            (int)setting,
                            (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified,
                            tf))
                        count++;
                }
                catch
                {
                    // Not every text-format preference applies to drawings.
                }
            }

            return count;
        }

        private static (int Annotations, int Formats, int Failed) ApplyToExistingAnnotations(DrawingDoc drawing)
        {
            int annotations = 0, formats = 0, failed = 0;

            for (var view = drawing.GetFirstView() as View; view is not null; view = view.GetNextView() as View)
            {
                if (view.GetAnnotations() is not object[] items)
                    continue;

                foreach (var item in items)
                {
                    if (item is not Annotation ann)
                        continue;

                    try
                    {
                        if (ann.GetType() == (int)swAnnotationType_e.swTableAnnotation)
                            continue;

                        var count = ann.GetTextFormatCount();
                        if (count <= 0)
                            continue;

                        annotations++;

                        for (int i = 0; i < count; i++)
                        {
                            if (ann.GetTextFormat(i) is not TextFormat tf)
                                continue;

                            Restyle(tf);

                            if (ann.SetTextFormat(i, false, tf))
                                formats++;
                            else
                                failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Logger.Warn($"[TextStyle] Annotation in view '{view.Name}' failed: {ex.Message}");
                    }
                }
            }

            return (annotations, formats, failed);
        }

        private static void Restyle(TextFormat tf)
        {
            tf.TypeFaceName = Typeface;
            tf.Bold = Bold;
        }
    }
}
