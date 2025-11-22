// PartAutomation/Interfaces/PartAutomationOptions.cs
namespace WAD.Runner.PartAutomation.Interfaces;

public sealed class PartAutomationOptions
{
    public string TemplatePartPath { get; init; } = "";
    public string EquationFilePath { get; init; } = "";
    public string OutputPartPath { get; init; } = "";
    public bool ShowSolidWorks { get; init; } = false;
}
