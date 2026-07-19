using System;
using System.Collections.Generic;

using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DrawingAutomation.SolidWorks;

namespace WAD.Runner.DrawingAutomation.Views;

/// <summary>
/// Compatibility facade for existing callers.
///
/// New production layout code should use:
/// - ViewPositionService
/// - ViewScaleService
/// - DrawingViewLayoutCoordinator
///
/// This class remains so existing pipelines do not need to be migrated at
/// the same time.
/// </summary>
public sealed class ViewPlacementService
{
    private readonly DrawingService _drawingService;
    private readonly ViewPositionService _positions;
    private readonly ViewScaleService _scales;

    public ViewPlacementService(
        DrawingService drawingService,
        IDictionary<string, string>? logicalToActual = null)
    {
        _drawingService =
            drawingService
            ?? throw new ArgumentNullException(
                nameof(drawingService));

        _positions =
            new ViewPositionService(
                drawingService,
                logicalToActual);

        _scales =
            new ViewScaleService(
                drawingService,
                logicalToActual);
    }

    /// <summary>
    /// Legacy behavior: configured scale + configured position + rebuild.
    ///
    /// New layout code should not use this combined operation.
    /// </summary>
    public bool Apply(
        string logicalKey,
        DrawingData drawingData)
    {
        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        _positions.PrepareForMovement(
            new[]
            {
                logicalKey
            });

        _scales.ApplyConfiguredScale(
            drawingData,
            logicalKey);

        var positioned =
            _positions.ApplyConfiguredPosition(
                logicalKey,
                drawingData);

        _drawingService.Rebuild();

        return positioned;
    }

    public bool ApplyPositionOnly(
        string logicalKey,
        DrawingData drawingData)
    {
        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        return
            _positions.ApplyConfiguredPosition(
                logicalKey,
                drawingData);
    }

    public void ApplyFinalPositions(
        DrawingData drawingData,
        IEnumerable<string> logicalViews)
    {
        if (drawingData is null)
            throw new ArgumentNullException(nameof(drawingData));

        if (logicalViews is null)
            throw new ArgumentNullException(nameof(logicalViews));

        _positions.ApplyConfiguredPositions(
            drawingData,
            logicalViews);

        _drawingService.Rebuild();
    }

    public void ApplyFrontSideTop(
        DrawingData drawingData)
    {
        Apply(
            "Front",
            drawingData);

        Apply(
            "Side",
            drawingData);

        Apply(
            "Top",
            drawingData);
    }

    public void ApplyDetailAndSection(
        DrawingData drawingData)
    {
        Apply(
            "Detail",
            drawingData);

        Apply(
            "Section",
            drawingData);
    }
}
