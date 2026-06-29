using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Planning;
using WAD.Runner.DataManagement.Domain.Units;
using WAD.Runner.DataManagement.Domain.Wedge;

using WAD.Runner.DrawingAutomation.Common;
using WAD.Runner.DrawingAutomation.Metadata;
using WAD.Runner.DrawingAutomation.Overlay;
using WAD.Runner.DrawingAutomation.Profiles;
using WAD.Runner.DrawingAutomation.SolidWorks;
using WAD.Runner.DrawingAutomation.Tables;
using WAD.Runner.DrawingAutomation.Views;

namespace WAD.Runner.DrawingAutomation.Common.Overlay
{
    public static class OverlayPayloadBuilder
    {
        public static OverlayDrawingPayload BuildOverlayPayload(DrawingRun run, DrawingData drawingData, string[] dimKeys)
        {
            var overlayBuilder = new OverlayDrawingDataBuilder();
            var overlayData = overlayBuilder.Build(run.Wedge, drawingData, dimKeys);

            Logger.Info($"[OverlayData] Desc='{overlayData.DrawingDescription}', Coining='{overlayData.CoiningText ?? "(none)"}', DimCount={overlayData.Dimensions.Count}");
            return overlayData;
        }

    }
}
