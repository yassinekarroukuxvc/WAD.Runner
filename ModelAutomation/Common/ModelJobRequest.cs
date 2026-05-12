using System.Collections.Generic;
using WAD.Runner.DataManagement.Domain.Wedge;
using WAD.Runner.ModelAutomation.Rules;

namespace WAD.Runner.ModelAutomation.Common;

public sealed class ModelJobRequest
{
    public string ArticleNumber { get; init; } = string.Empty;
    public WedgeSubclass Subclass { get; init; }
    public DrawingType DrawingType { get; init; }

    public string OutputRoot { get; init; } = string.Empty;
    public string PartTemplatePath { get; init; } = string.Empty;
    public string EquationTemplatePath { get; init; } = string.Empty;
    public string? FileBase { get; init; }


    public WedgeData? WedgeData { get; init; }

    public WedgeType WedgeType { get; init; }


    public IReadOnlyList<FeatureToggleStep>? ToggleStepsOverride { get; init; }


    public string? FinalActiveConfigurationOverride { get; init; }
}
