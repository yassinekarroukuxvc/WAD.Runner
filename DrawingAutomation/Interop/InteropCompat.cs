using System;
using System.Globalization;

using SolidWorks.Interop.sldworks;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Interop;

internal static class InteropCompat
{
    public static bool TryBreakAlignment(View view)
    {
        if (view is null)
            return false;

        try
        {
            view.RemoveAlignment();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"Could not remove alignment from view '{view.GetName2()}': " +
                ex.Message);
            return false;
        }
    }

    public static void TryUnlock(View view)
    {
        if (view is null)
            return;

        try
        {
            view.PositionLocked = false;
        }
        catch
        {
            // Some SolidWorks view types do not expose PositionLocked.
        }
    }

    public static double GetScaleDecimalOr(View view, double fallback = 1.0)
    {
        if (view is null)
            return fallback;

        try
        {
            return view.ScaleDecimal;
        }
        catch
        {
            return fallback;
        }
    }

    public static void TrySetScale(View view, double value)
    {
        if (view is null)
            return;

        try
        {
            view.ScaleDecimal = value;
            return;
        }
        catch
        {
            // Fall through to older interop variants.
        }

        try
        {
            view.GetType()
                .GetMethod("SetScale")?
                .Invoke(view, new object[] { value });
        }
        catch
        {
            // The caller validates the resulting scale where required.
        }
    }

    public static bool TryGetViewOutline(
        View view,
        out double x1,
        out double y1,
        out double x2,
        out double y2)
    {
        x1 = y1 = x2 = y2 = 0.0;
        if (view is null)
            return false;

        try
        {
            var outline = view.GetOutline();
            if (outline is double[] doubles && doubles.Length >= 4)
            {
                x1 = doubles[0];
                y1 = doubles[1];
                x2 = doubles[2];
                y2 = doubles[3];
                return true;
            }

            if (outline is object[] objects && objects.Length >= 4)
            {
                x1 = Convert.ToDouble(objects[0], CultureInfo.InvariantCulture);
                y1 = Convert.ToDouble(objects[1], CultureInfo.InvariantCulture);
                x2 = Convert.ToDouble(objects[2], CultureInfo.InvariantCulture);
                y2 = Convert.ToDouble(objects[3], CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch
        {
            // Fall through to the ref-parameter interop variant.
        }

        try
        {
            double refX1 = 0.0;
            double refY1 = 0.0;
            double refX2 = 0.0;
            double refY2 = 0.0;
            dynamic dynamicView = view;
            dynamicView.GetOutline(ref refX1, ref refY1, ref refX2, ref refY2);

            x1 = refX1;
            y1 = refY1;
            x2 = refX2;
            y2 = refY2;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
