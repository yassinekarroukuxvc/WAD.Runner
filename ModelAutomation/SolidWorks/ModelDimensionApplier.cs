using System;
using WAD.Runner.Application;
using WAD.Runner.ModelAutomation.Equations;

namespace WAD.Runner.ModelAutomation.SolidWorks;

public enum DimensionApplyMode
{
    EquationFilePrimary = 0
}

public sealed record DimensionApplyResult(bool Success, string MethodUsed, string? Error = null);

public sealed class ModelDimensionApplier
{
    private readonly EquationFileWriter _writer;

    public ModelDimensionApplier(EquationFileWriter? writer = null)
    {
        _writer = writer ?? new EquationFileWriter();
    }

    public DimensionApplyResult Apply(ModelEditor editor, string equationsOutPath, EquationPlan plan)
    {
        if (editor is null) throw new ArgumentNullException(nameof(editor));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (string.IsNullOrWhiteSpace(equationsOutPath))
            throw new ArgumentException("Equation file path is required.", nameof(equationsOutPath));

        try
        {
            Logger.Info("[ModelDimensionApplier] Writing equation plan to file.");
            _writer.Write(equationsOutPath, plan);
            editor.ImportEquationsFromFile(equationsOutPath);
            return new DimensionApplyResult(true, "EquationFilePrimary");
        }
        catch (Exception ex)
        {
            Logger.Warn($"[ModelDimensionApplier] Failed: {ex.GetType().Name}: {ex.Message}");
            return new DimensionApplyResult(false, "EquationFilePrimary", ex.Message);
        }
    }
}
