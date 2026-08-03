using System;
using System.Globalization;
using System.IO;
using System.Linq;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Overlay.Positioning;

internal sealed class SolidWorksMacroRunner
{
    private const string DefaultModule = "Macro1";
    private const string DefaultProcedure = "main";

    private readonly string _macroPath;
    private readonly string _moduleName;
    private readonly string _procedureName;
    private readonly swRunMacroOption_e _options;
    private readonly string _argsFilePath;

    public SolidWorksMacroRunner(
        string macroPath,
        string moduleName = DefaultModule,
        string procedureName = DefaultProcedure,
        swRunMacroOption_e options = swRunMacroOption_e.swRunMacroUnloadAfterRun)
    {
        _macroPath = macroPath;
        _moduleName = string.IsNullOrWhiteSpace(moduleName) ? DefaultModule : moduleName;
        _procedureName = string.IsNullOrWhiteSpace(procedureName) ? DefaultProcedure : procedureName;
        _options = options;
        _argsFilePath = Path.Combine(Path.GetTempPath(), "SW_MacroArgs.ini");
    }

    public void PrepareArgs(
        string viewName,
        string referencePointName,
        double xIn,
        double yIn)
    {
        try
        {
            File.WriteAllLines(
                _argsFilePath,
                new[]
                {
                    "VIEW_NAME=" + (viewName ?? string.Empty),
                    "SKETCH_NAME=" + (referencePointName ?? string.Empty),
                    "X_IN=" + xIn.ToString(CultureInfo.InvariantCulture),
                    "Y_IN=" + yIn.ToString(CultureInfo.InvariantCulture)
                });

            System.Environment.SetEnvironmentVariable(
                "SW_MACRO_ARGS",
                _argsFilePath,
                System.EnvironmentVariableTarget.Process);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[Overlay/Position] Failed to write macro arguments: {ex.Message}");
        }
    }

    public bool Run(SldWorks swApp)
    {
        if (swApp is null)
            return Logger.WarnAndReturnFalse("[Overlay/Position] SolidWorks is null.");

        if (string.IsNullOrWhiteSpace(_macroPath))
            return Logger.WarnAndReturnFalse("[Overlay/Position] Macro path is empty.");

        if (!File.Exists(_macroPath))
            return Logger.WarnAndReturnFalse($"[Overlay/Position] Macro not found: {_macroPath}");

        if (TryRun(swApp, _moduleName, _procedureName, out var firstError))
            return true;

        if (IsNameOrIndexProblem(firstError) && TryAutoDiscoverAndRun(swApp))
            return true;

        Logger.Error(
            $"[Overlay/Position] Macro execution failed for " +
            $"{Path.GetFileName(_macroPath)}::{_moduleName}.{_procedureName} " +
            $"(code {firstError}). {DescribeRunMacroError(firstError)}");
        return false;
    }

    private bool TryRun(
        SldWorks swApp,
        string module,
        string procedure,
        out int errorCode)
    {
        errorCode = 0;

        try
        {
            var succeeded = swApp.RunMacro2(
                _macroPath,
                module,
                procedure,
                (int)_options,
                out errorCode);

            if (succeeded)
            {
                Logger.Success(
                    $"[Overlay/Position] Macro executed: " +
                    $"{Path.GetFileName(_macroPath)}::{module}.{procedure}");
            }

            return succeeded;
        }
        catch (Exception ex)
        {
            errorCode = -1;
            Logger.Error($"[Overlay/Position] RunMacro2 failed: {ex.Message}");
            return false;
        }
    }

    private bool TryAutoDiscoverAndRun(SldWorks swApp)
    {
        try
        {
            var methodsObject = swApp.GetMacroMethods(
                _macroPath,
                (int)swMacroMethods_e.swMethodsWithoutArguments);

            var methods = methodsObject switch
            {
                object[] objects => objects
                    .Select(value => value?.ToString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray(),
                Array array => array
                    .Cast<object>()
                    .Select(value => value?.ToString() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray(),
                _ => Array.Empty<string>()
            };

            var candidates = methods
                .Select(ParseMacroSignature)
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.Module) &&
                    !string.IsNullOrWhiteSpace(candidate.Procedure))
                .ToArray();

            if (candidates.Length == 0)
            {
                Logger.Warn(
                    "[Overlay/Position] No zero-argument macro methods were discovered.");
                return false;
            }

            var selected = candidates.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Procedure,
                    DefaultProcedure,
                    StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(selected.Module))
                selected = candidates[0];

            return TryRun(
                swApp,
                selected.Module,
                selected.Procedure,
                out _);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Overlay/Position] Macro discovery failed: {ex.Message}");
            return false;
        }
    }

    private static (string Module, string Procedure) ParseMacroSignature(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (string.Empty, string.Empty);

        var separator = value.Contains('@') ? '@' : value.Contains('.') ? '.' : '\0';
        if (separator == '\0')
            return ("Module1", value.Trim());

        var parts = value.Split(separator);
        return (
            parts[0].Trim(),
            parts.Length > 1 ? parts[1].Trim() : DefaultProcedure);
    }

    private static bool IsNameOrIndexProblem(int code)
        => code is 2 or 3 or 22;

    private static string DescribeRunMacroError(int code)
        => code switch
        {
            0 => "No error was reported, but RunMacro2 returned false.",
            1 => "The macro path is invalid or missing.",
            2 => "The macro module was not found.",
            3 => "The macro procedure was not found.",
            4 => "SolidWorks could not run the macro.",
            5 => "The macro format is unsupported.",
            6 => "The macro failed during execution.",
            22 => "The macro module or procedure index is invalid.",
            _ => "Unknown SolidWorks macro error."
        };
}
