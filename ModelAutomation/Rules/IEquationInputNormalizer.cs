using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.ModelAutomation.Rules;

public interface IEquationInputNormalizer
{
    IReadOnlyDictionary<DimensionKey, Dimension> Normalize(WedgeData wedge, DrawingType drawingType);
}