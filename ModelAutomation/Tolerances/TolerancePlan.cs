using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WAD.Runner.ModelAutomation.Tolerances;

public sealed record TolerancePlan(IReadOnlyList<ToleranceUpdate> Updates)
{
    public static readonly TolerancePlan Empty = new(Array.Empty<ToleranceUpdate>());

    public int Count => Updates?.Count ?? 0;

    public override string ToString()
        => $"TolerancePlan(Updates={Count})";

    public IEnumerable<ToleranceUpdate> NonEmpty()
        => (Updates ?? Array.Empty<ToleranceUpdate>()).Where(u => u is not null);
}

public sealed record ToleranceUpdate(
    string TargetDimensionName,
    decimal Value,
    ToleranceUnit Unit
);

public enum ToleranceUnit
{
    LengthMm,
    AngleDeg
}
