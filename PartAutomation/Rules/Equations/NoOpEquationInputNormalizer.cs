// PartAutomation/Rules/Equations/NoOpEquationInputNormalizer.cs
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Drawing;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.PartAutomation.Interfaces;

namespace WAD.Runner.PartAutomation.Rules.Equations;

public sealed class NoOpEquationInputNormalizer : IEquationInputNormalizer
{
    public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
        => wedge.Dimensions;
}
