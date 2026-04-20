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

    public WedgeData WedgeData { get; init; } = null!;
    public WedgeType WedgeType { get; init; }

    /// <summary>
    /// Optional explicit per-configuration toggle steps.
    ///
    /// Use this when different reference configurations must receive different
    /// suppress/unsuppress plans. Each step activates one configuration and lets
    /// the feature rules build a plan for that specific configuration/profile.
    /// </summary>
    public IReadOnlyList<FeatureToggleStep>? ToggleStepsOverride { get; init; }

    /// <summary>
    /// Optional final active configuration override.
    ///
    /// When <see cref="ToggleStepsOverride"/> is supplied, the orchestrator still
    /// needs to know which configuration should remain active after the toggle phase.
    /// Leave null to keep the wedge-type rule's normal decision.
    /// </summary>
    public string? FinalActiveConfigurationOverride { get; init; }
}
