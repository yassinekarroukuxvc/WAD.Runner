using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
namespace WAD.Runner.ModelAutomation.Tolerances;

/// <summary>
/// Central dispatcher. One place to select rule sets by wedge type.
/// </summary>
public sealed class TolerancePlanner
{
    private readonly IToleranceRuleSet _cobRules = new CobToleranceRules();
    private readonly IToleranceRuleSet _ckvdRules = new CkvdToleranceRules();

    public TolerancePlan Build(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        return wedgeType switch
        {
            WedgeType.COB => _cobRules.Build(wedge, drawingType, subclass),
            WedgeType.CKVD => _ckvdRules.Build(wedge,drawingType,subclass),
            _ => TolerancePlan.Empty
        };
    }
}
