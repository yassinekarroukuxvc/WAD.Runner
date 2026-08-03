using System;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Wedges;

namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

public sealed class OverlayViewPositioningContext
{
    public OverlayViewPositioningContext(
        DrawingRun run,
        double overlayMagnification)
    {
        Run = run ?? throw new ArgumentNullException(nameof(run));
        Facts = new DrawingWedgeFacts(run.Wedge);

        var module = DrawingWedgeModuleRegistry.Get(run.WedgeType);
        Behavior = module.Behavior;
        OverlayMagnification = overlayMagnification;
        OverlayScale = OverlayMagnificationService.GetViewScale(overlayMagnification);
    }

    public DrawingRun Run { get; }
    public DrawingWedgeFacts Facts { get; }
    public DrawingWedgeBehavior Behavior { get; }
    public double OverlayMagnification { get; }
    public double OverlayScale { get; }

    public WedgeType WedgeType => Run.WedgeType;
    public bool IsReverse180 => Facts.ShankType == DrawingShankType.Reverse180;
    public bool RepositionPrimaryViews => Behavior.RepositionPrimaryOverlayViews;

    public string BaseReferencePointName =>
        IsReverse180
            ? OverlayReferencePointNames.Reverse180
            : Behavior.OverlayReferencePointSketch;

    public bool HasPositiveLength(string dimensionName)
        => Facts.HasPositiveLength(dimensionName);

    public double GetLengthMmOrNaN(string dimensionName)
        => Facts.GetLengthMmOrNaN(dimensionName);

    public string NormalizedPropertyToken(params string[] keys)
    {
        var raw = Facts.GetProperty(keys);
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var token = raw.Trim();
        var separatorIndex = token.IndexOf(';');
        if (separatorIndex >= 0)
            token = token[..separatorIndex];

        return token.Trim();
    }
}
