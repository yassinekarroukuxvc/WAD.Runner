using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules;

public sealed class NoOpEquationInputNormalizer : IEquationInputNormalizer
{
    public IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType)
        => wedge.Dimensions;
}
