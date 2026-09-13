using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;

namespace WAD.Runner.DrawingAutomation.Overlay
{
    public static class OverlayPayloadBuilder
    {
        public static OverlayDrawingPayload BuildOverlayPayload(
            DrawingRun run,
            DrawingData drawingData,
            IEnumerable<string> dimKeys)
        {
            if (run is null)
                throw new ArgumentNullException(nameof(run));

            if (drawingData is null)
                throw new ArgumentNullException(nameof(drawingData));

            if (dimKeys is null)
                throw new ArgumentNullException(nameof(dimKeys));

            var keys = dimKeys
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Logger.Info(
                $"[OverlayData] Build payload -> " +
                $"wedge={run.WedgeType}, subclass={run.Wedge.Subclass}, " +
                $"drawingType={drawingData.DrawingType}, " +
                $"sourceDimensions={run.Wedge.Dimensions?.Count ?? 0}, " +
                $"filterKeys={keys.Length}.");

            var overlayBuilder = new OverlayDrawingDataBuilder();
            var overlayData = overlayBuilder.Build(run.Wedge, drawingData, keys);

            Logger.Info(
                $"[OverlayData] Desc='{overlayData.DrawingDescription}', " +
                $"Coining='{overlayData.CoiningText ?? "(none)"}', " +
                $"DimCount={overlayData.Dimensions.Count}.");

            if (overlayData.Dimensions.Count == 0)
            {
                var actualKeys = run.Wedge.Dimensions is null
                    ? "<none>"
                    : string.Join(
                        ", ",
                        run.Wedge.Dimensions.Keys
                            .Select(k => k.Value)
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

                Logger.Warn(
                    $"[OverlayData] Overlay payload contains ZERO dimension rows -> " +
                    $"wedge={run.WedgeType}, subclass={run.Wedge.Subclass}. " +
                    $"Allowed=[{string.Join(", ", keys)}]. " +
                    $"Actual wedge dimension keys=[{actualKeys}].");
            }

            return overlayData;
        }
    }
}
