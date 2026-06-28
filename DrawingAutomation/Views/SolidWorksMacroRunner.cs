using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WAD.Runner.Application;

namespace WAD.Runner.DrawingAutomation.Views;

public sealed class SolidWorksMacroRunner
{
    public string MacroPath { get; }
    public string ModuleName { get; }
    public string ProcedureName { get; }
    public swRunMacroOption_e Options { get; }


    public string ArgsFilePath { get; private set; }

    public SolidWorksMacroRunner(
        string macroPath,
        string moduleName = "Macro1",
        string procedureName = "main",
        swRunMacroOption_e options = swRunMacroOption_e.swRunMacroUnloadAfterRun)
    {
        MacroPath = macroPath;
        ModuleName = string.IsNullOrWhiteSpace(moduleName) ? "Macro1" : moduleName;
        ProcedureName = string.IsNullOrWhiteSpace(procedureName) ? "main" : procedureName;
        Options = options;

        ArgsFilePath = Path.Combine(Path.GetTempPath(), "SW_MacroArgs.ini");
    }


    public void PrepareArgs(string viewName, string sketchName, double xIn, double yIn)
    {
        try
        {
            var lines = new[]
            {
                    "VIEW_NAME=" + (viewName ?? ""),
                    "SKETCH_NAME=" + (sketchName ?? ""),
                    "X_IN=" + xIn.ToString(CultureInfo.InvariantCulture),
                    "Y_IN=" + yIn.ToString(CultureInfo.InvariantCulture)
                };
            File.WriteAllLines(ArgsFilePath, lines);


            System.Environment.SetEnvironmentVariable(
                "SW_MACRO_ARGS",
                ArgsFilePath,
                System.EnvironmentVariableTarget.Process);

            Logger.Info($"Macro args written to: {ArgsFilePath}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to write macro args: {ex.Message}");
        }
    }


    public bool Run(SldWorks swApp)
    {
        if (swApp == null) return Logger.WarnAndReturnFalse("MacroRunner: swApp is null.");
        if (string.IsNullOrWhiteSpace(MacroPath)) return Logger.WarnAndReturnFalse("MacroRunner: macro path is empty.");
        if (!File.Exists(MacroPath)) return Logger.WarnAndReturnFalse($"MacroRunner: file not found: {MacroPath}");


        if (TryRun(swApp, ModuleName, ProcedureName, out var firstErr))
            return true;


        if (IsNameOrIndexProblem(firstErr))
        {
            if (TryAutoDiscoverAndRun(swApp)) return true;
        }

        Logger.Error($"Macro execution failed for {Path.GetFileName(MacroPath)}::{ModuleName}.{ProcedureName} " +
                     $"(code {firstErr}). {DescribeRunMacroError(firstErr)}");
        return false;
    }


    public bool Run(SldWorks swApp, string moduleOverride, string procedureOverride)
    {
        var runner = new SolidWorksMacroRunner(
            MacroPath,
            string.IsNullOrWhiteSpace(moduleOverride) ? ModuleName : moduleOverride,
            string.IsNullOrWhiteSpace(procedureOverride) ? ProcedureName : procedureOverride,
            Options)
        {
            ArgsFilePath = this.ArgsFilePath
        };

        return runner.Run(swApp);
    }


    private bool TryRun(SldWorks swApp, string module, string proc, out int errorCode)
    {
        errorCode = 0;
        try
        {
            bool ok = swApp.RunMacro2(MacroPath, module, proc, (int)Options, out errorCode);
            if (ok)
            {
                Logger.Success($"Macro executed: {Path.GetFileName(MacroPath)}::{module}.{proc}");
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"RunMacro2 threw: {ex.Message}");
            errorCode = -1;
            return false;
        }
    }

    private bool TryAutoDiscoverAndRun(SldWorks swApp)
    {
        try
        {

            var methodsObj = swApp.GetMacroMethods(MacroPath, (int)swMacroMethods_e.swMethodsWithoutArguments);


            string[] methods = Array.Empty<string>();
            switch (methodsObj)
            {
                case null:
                    methods = Array.Empty<string>();
                    break;
                case object[] oArr:
                    methods = oArr.Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    break;
                case System.Array arr:
                    methods = arr.Cast<object>().Select(o => o?.ToString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    break;
            }

            if (methods.Length == 0)
            {
                Logger.Warn("GetMacroMethods returned no zero-arg methods; cannot auto-discover module/procedure.");
                return false;
            }

            var parsed = methods
                .Select(ParseMacroSignature)
                .Where(p => !string.IsNullOrWhiteSpace(p.module) && !string.IsNullOrWhiteSpace(p.method))
                .ToList();

            Logger.Info("Discovered macro methods: " +
                        string.Join(", ", parsed.Select(p => $"{p.module}.{p.method}")));


            (string module, string method) pick;
            var hasMain = parsed.Any(p => string.Equals(p.method, "main", StringComparison.OrdinalIgnoreCase));
            if (hasMain)
                pick = parsed.First(p => string.Equals(p.method, "main", StringComparison.OrdinalIgnoreCase));
            else
                pick = parsed[0];

            if (TryRun(swApp, pick.module, pick.method, out var err))
                return true;

            Logger.Error($"Auto-discovered call failed (code {err}) for {pick.module}.{pick.method}. {DescribeRunMacroError(err)}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"Auto-discovery failed: {ex.Message}");
            return false;
        }
    }

    private static (string module, string method) ParseMacroSignature(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return ("", "");
        if (s.Contains("@"))
        {
            var parts = s.Split('@');
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "main");
        }
        if (s.Contains("."))
        {
            var parts = s.Split('.');
            return (parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "main");
        }

        return ("Module1", s.Trim());
    }

    private static bool IsNameOrIndexProblem(int code)
    {


        return code == 2 || code == 3 || code == 22;
    }

    private static string DescribeRunMacroError(int code) => code switch
    {
        0 => "No error reported, but RunMacro2 returned false.",
        1 => "File path invalid / not found.",
        2 => "Module not found in macro (check the module name).",
        3 => "Procedure not found in module (check the procedure name).",
        4 => "Macro could not be run (general failure).",
        5 => "Unsupported macro format (expect .swp/.swb).",
        6 => "Macro threw an error during execution.",
        22 => "Invalid index (often wrong module/procedure string for this macro).",
        _ => "Unknown error code."
    };
}
