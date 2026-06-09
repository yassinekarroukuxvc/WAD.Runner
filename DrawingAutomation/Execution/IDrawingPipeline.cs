using WAD.Runner.DrawingAutomation.Core;

namespace WAD.Runner.DrawingAutomation.Execution;

public interface IDrawingPipeline
{
    bool CanHandle(DrawingAutomationContext context);
    void Run(DrawingAutomationContext context);
}
