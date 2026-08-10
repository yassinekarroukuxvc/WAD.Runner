using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.DrawingAutomation.Overlay.Positioning;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Catalogs;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Domain;
using WAD.Runner.DrawingAutomation.Rules.AnnotationCleanup.Resolution;

namespace WAD.Runner.DrawingAutomation.Wedges;

public interface IDrawingWedgeModule
{
    WedgeType WedgeType { get; }
    DrawingWedgeBehavior Behavior { get; }
    IReadOnlyList<DrawingProfile> Profiles { get; }
    IOverlayViewPositioningRule OverlayPositioningRule { get; }
    IReadOnlyList<IAnnotationRuleCatalog> AnnotationCatalogs { get; }
    IAnnotationWedgeContextResolver AnnotationContextResolver { get; }

    AnnotationCleanupProfile ResolveAnnotationProfile(
        WedgeSubclass subclass,
        DrawingType drawingType);

    string? ResolveReferencedConfiguration(
        string logicalView,
        WedgeSubclass subclass,
        DrawingType drawingType,
        bool hasVw,
        bool hasVr);

    IReadOnlySet<string>? GetAllowedDimensionTableKeys(
        WedgeSubclass subclass,
        DrawingType drawingType);
}
