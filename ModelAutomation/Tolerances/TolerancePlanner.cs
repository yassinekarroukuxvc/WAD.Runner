using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules.CKVD;
using WAD.Runner.ModelAutomation.Rules.COB;
using WAD.Runner.ModelAutomation.Rules.FP;
using WAD.Runner.ModelAutomation.Rules.UTUS;
namespace WAD.Runner.ModelAutomation.Tolerances;

/// <summary>
/// Central dispatcher. One place to select rule sets by wedge type.
/// </summary>
public sealed class TolerancePlanner
{
    private readonly IToleranceRuleSet _cobRules = new CobToleranceRules();
    private readonly IToleranceRuleSet _ckvdRules = new CkvdToleranceRules();
    private readonly IToleranceRuleSet _utusRules = new UtusToleranceRules();
    private readonly IToleranceRuleSet _fpRules = new FpToleranceRules();

    public TolerancePlan Build(WedgeType wedgeType, WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass)
    {
        if (wedge is null) throw new ArgumentNullException(nameof(wedge));

        return wedgeType switch
        {
            WedgeType.COB => _cobRules.Build(wedge, drawingType, subclass),
            WedgeType.CKVD => _ckvdRules.Build(wedge,drawingType,subclass),
            WedgeType.UTUS => _utusRules.Build(wedge, drawingType, subclass),
            WedgeType.FP => _fpRules.Build(wedge, drawingType, subclass),
            _ => TolerancePlan.Empty
        };
    }
}
