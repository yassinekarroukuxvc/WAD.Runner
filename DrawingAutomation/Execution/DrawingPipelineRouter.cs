using System;
using System.Collections.Generic;
using System.Linq;

using WAD.Runner.DrawingAutomation.Core;
using WAD.Runner.DrawingAutomation.Execution.Overlay;
using WAD.Runner.DrawingAutomation.Execution.Production;

namespace WAD.Runner.DrawingAutomation.Execution;

public sealed class DrawingPipelineRouter
{
    private readonly IReadOnlyList<IDrawingPipeline> _pipelines;

    public DrawingPipelineRouter(IEnumerable<IDrawingPipeline>? pipelines = null)
    {
        _pipelines = (pipelines ?? DefaultPipelines()).ToList();
    }

    public void Run(DrawingAutomationContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var pipeline = _pipelines.FirstOrDefault(p => p.CanHandle(context));
        if (pipeline is null)
            throw new NotSupportedException($"No drawing pipeline registered for '{context.DrawingData.DrawingType}'.");

        pipeline.Run(context);
    }

    private static IEnumerable<IDrawingPipeline> DefaultPipelines()
    {
        yield return new OverlayDrawingPipeline();
        yield return new ProductionDrawingPipeline();
    }
}
