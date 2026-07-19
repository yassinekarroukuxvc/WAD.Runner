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
        _pipelines = (pipelines ?? DefaultPipelines())
            .Where(pipeline => pipeline is not null)
            .ToArray();

        if (_pipelines.Count == 0)
            throw new ArgumentException("At least one drawing pipeline must be registered.", nameof(pipelines));
    }

    public void Run(DrawingAutomationContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var matches = _pipelines
            .Where(pipeline => pipeline.CanHandle(context))
            .Take(2)
            .ToArray();

        if (matches.Length == 0)
        {
            throw new NotSupportedException(
                $"No drawing pipeline is registered for '{context.DrawingData.DrawingType}'.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one drawing pipeline can handle '{context.DrawingData.DrawingType}'. " +
                "Pipeline conditions must be mutually exclusive.");
        }

        matches[0].Run(context);
    }

    private static IEnumerable<IDrawingPipeline> DefaultPipelines()
    {
        yield return new OverlayDrawingPipeline();
        yield return new ProductionDrawingPipeline();
    }
}
