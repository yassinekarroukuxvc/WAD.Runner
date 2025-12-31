using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.PartAutomation.Interfaces;

public interface IEquationInputNormalizer
{
    IReadOnlyDictionary<DimensionKey, Dimension> Normalize(
        WedgeData wedge,
        DrawingType drawingType);
}