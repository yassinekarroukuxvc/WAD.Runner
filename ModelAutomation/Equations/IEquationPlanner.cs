using WAD.Runner.ModelAutomation.Core;

namespace WAD.Runner.ModelAutomation.Equations;

public interface IEquationPlanner
{
    EquationPlan Build(ModelAutomationContext context);
}
