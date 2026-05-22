using WAD.Runner.DrawingAutomation.Core;

namespace WAD.Runner.DrawingAutomation.Execution;

/// <summary>
/// A drawing-type-specific workflow. Implementations should orchestrate drawing
/// services only. They must not update model equations or change model feature states.
/// </summary>
public interface IDrawingPipeline
{
    bool CanHandle(DrawingAutomationContext context);
    void Run(DrawingAutomationContext context);
}
