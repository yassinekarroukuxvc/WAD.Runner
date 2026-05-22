using System;
using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Dimensions;
using WAD.Runner.DataManagement.Domain.Units;

using DomDim = WAD.Runner.DataManagement.Domain.Dimensions.Dimension;

namespace WAD.Runner.ModelAutomation.Equations;

internal sealed class EquationPlanBuilder
{
    private readonly Dictionary<string, DomDim> _dimensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _managed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _zeroProvided = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingToZero = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _angleKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _writeZeros;
    private bool _missingAsZero;

    public EquationPlanBuilder WithDimensions(
        IReadOnlyDictionary<DimensionKey, DomDim> dimensions,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        foreach (var kv in dimensions)
        {
            var key = kv.Key.Value;
            if (aliases is not null && aliases.TryGetValue(key, out var alias))
                key = alias;
            _dimensions[key] = kv.Value;
        }
        return this;
    }

    public EquationPlanBuilder AddManaged(string key, string line)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(line))
            _managed[key.Trim()] = line;
        return this;
    }

    public EquationPlanBuilder SkipProvidedZeroDimensions()
    {
        foreach (var (key, dim) in _dimensions)
        {
            try
            {
                var value = dim.Nominal.Unit == UnitKind.Degree ? dim.Nominal.AsDeg() : dim.Nominal.AsMm();
                if (Math.Abs(value) <= 0.000000001m)
                    _zeroProvided.Add(key);
            }
            catch
            {
                _zeroProvided.Add(key);
            }
        }
        return this;
    }

    public EquationPlanBuilder WriteProvidedZeros()
    {
        _writeZeros = true;
        _zeroProvided.Clear();
        return this;
    }

    public EquationPlanBuilder ZeroMissingKeys(IEnumerable<string> keys, IEnumerable<string>? angleKeys = null)
    {
        _missingAsZero = true;
        foreach (var key in keys)
            if (!string.IsNullOrWhiteSpace(key)) _missingToZero.Add(key.Trim());
        if (angleKeys is not null)
            foreach (var key in angleKeys)
                if (!string.IsNullOrWhiteSpace(key)) _angleKeys.Add(key.Trim());
        return this;
    }

    public EquationPlan Build()
        => new(_dimensions, _managed, _zeroProvided, _missingToZero, _angleKeys, _writeZeros, _missingAsZero);
}
