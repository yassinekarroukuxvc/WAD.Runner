using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Tolerances;

public interface IToleranceRuleSet
{
    TolerancePlan Build(WedgeData wedge, DrawingType drawingType, WedgeSubclass subclass);
}
