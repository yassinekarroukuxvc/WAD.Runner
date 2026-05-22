using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed record ManagedEquation(string Key, string Line);

public sealed class EquationPlan
{
    public EquationPlan(
        IReadOnlyDictionary<string, DomDim> dimensionsByKey,
        IReadOnlyDictionary<string, string> managedEquations,
        IReadOnlyCollection<string> zeroProvidedKeys,
        IReadOnlyCollection<string> missingKeysToZero,
        IReadOnlyCollection<string> angleKeys,
        bool writeZeros,
        bool missingDbKeysAsZero)
    {
        DimensionsByKey = dimensionsByKey ?? throw new ArgumentNullException(nameof(dimensionsByKey));
        ManagedEquations = managedEquations ?? throw new ArgumentNullException(nameof(managedEquations));
        ZeroProvidedKeys = zeroProvidedKeys ?? Array.Empty<string>();
        MissingKeysToZero = missingKeysToZero ?? Array.Empty<string>();
        AngleKeys = angleKeys ?? Array.Empty<string>();
        WriteZeros = writeZeros;
        MissingDbKeysAsZero = missingDbKeysAsZero;
    }

    public IReadOnlyDictionary<string, DomDim> DimensionsByKey { get; }
    public IReadOnlyDictionary<string, string> ManagedEquations { get; }
    public IReadOnlyCollection<string> ZeroProvidedKeys { get; }
    public IReadOnlyCollection<string> MissingKeysToZero { get; }
    public IReadOnlyCollection<string> AngleKeys { get; }
    public bool WriteZeros { get; }
    public bool MissingDbKeysAsZero { get; }
}
