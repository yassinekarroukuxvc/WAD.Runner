// Cli/FeatureTreeCommands.cs
// CLI command: export-feature-tree
// Opens a SolidWorks model read-only and writes its feature tree to a Markdown file.
//
// Usage:
//   dotnet run -- export-feature-tree --model "C:\path\model.sldprt" [--out "C:\path\tree.md"]
//   dotnet run -- export-feature-tree "C:\path\model.sldprt" [--out "C:\path\tree.md"]

using System.Runtime.InteropServices;
using System.Text;

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using static WAD.Runner.Cli.CliArgs;

namespace WAD.Runner.Cli
{
    public static class FeatureTreeCommands
    {
        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID(
            [MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(
            ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        public static Task ExportFeatureTreeAsync(IServiceProvider services, string[] args)
        {
            string? modelArg = GetArgValue(args, "--model");
            if (string.IsNullOrWhiteSpace(modelArg) && args.Length > 1 && !args[1].StartsWith("--"))
            {
                modelArg = args[1];
            }

            if (string.IsNullOrWhiteSpace(modelArg))
            {
                Console.WriteLine("Usage: export-feature-tree --model <path to .sldprt/.sldasm/.slddrw> [--out <file.md>]");
                System.Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            string modelPath = Path.GetFullPath(modelArg);
            if (!File.Exists(modelPath))
            {
                Console.WriteLine("[export-feature-tree] File not found: " + modelPath);
                System.Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            string? outArg = GetArgValue(args, "--out");
            string outputPath;
            if (!string.IsNullOrWhiteSpace(outArg))
            {
                outputPath = Path.GetFullPath(outArg);
            }
            else
            {
                string dir = Path.GetDirectoryName(modelPath) ?? Directory.GetCurrentDirectory();
                outputPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(modelPath) + "_FeatureTree.md");
            }

            SldWorks? swApp = null;
            ModelDoc2? model = null;
            bool startedByUs = false;

            try
            {
                swApp = ConnectToSolidWorks(out startedByUs);
                if (swApp == null)
                {
                    Console.WriteLine("[export-feature-tree] Could not start or connect to SolidWorks.");
                    System.Environment.ExitCode = 2;
                    return Task.CompletedTask;
                }

                model = OpenModel(swApp, modelPath);
                if (model == null)
                {
                    System.Environment.ExitCode = 3;
                    return Task.CompletedTask;
                }

                string markdown = BuildMarkdown(model, modelPath);
                File.WriteAllText(outputPath, markdown, new UTF8Encoding(false));
                Console.WriteLine("[export-feature-tree] Written: " + outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[export-feature-tree] ERROR: " + ex);
                System.Environment.ExitCode = 99;
            }
            finally
            {
                try
                {
                    if (swApp != null && model != null)
                    {
                        swApp.CloseDoc(model.GetTitle());
                    }
                    if (swApp != null && startedByUs)
                    {
                        swApp.ExitApp();
                    }
                }
                catch
                {
                    // ignore cleanup errors
                }
            }

            return Task.CompletedTask;
        }

        private static SldWorks? ConnectToSolidWorks(out bool startedByUs)
        {
            startedByUs = false;

            // Reuse a running SolidWorks instance if there is one
            // (Marshal.GetActiveObject does not exist on modern .NET, so call ole32/oleaut32 directly)
            try
            {
                Guid clsid;
                if (CLSIDFromProgID("SldWorks.Application", out clsid) == 0)
                {
                    object running;
                    if (GetActiveObject(ref clsid, IntPtr.Zero, out running) == 0)
                    {
                        SldWorks? existing = running as SldWorks;
                        if (existing != null)
                        {
                            return existing;
                        }
                    }
                }
            }
            catch (COMException)
            {
                // fall through and start a new instance
            }

            Type? swType = Type.GetTypeFromProgID("SldWorks.Application");
            if (swType == null)
            {
                return null;
            }

            SldWorks? app = Activator.CreateInstance(swType) as SldWorks;
            if (app != null)
            {
                startedByUs = true;
                app.Visible = true;
            }
            return app;
        }

        private static ModelDoc2? OpenModel(SldWorks swApp, string path)
        {
            int docType;
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".sldprt":
                    docType = (int)swDocumentTypes_e.swDocPART;
                    break;
                case ".sldasm":
                    docType = (int)swDocumentTypes_e.swDocASSEMBLY;
                    break;
                case ".slddrw":
                    docType = (int)swDocumentTypes_e.swDocDRAWING;
                    break;
                default:
                    Console.WriteLine("[export-feature-tree] Unsupported extension: " + ext);
                    return null;
            }

            int errors = 0;
            int warnings = 0;
            int options = (int)swOpenDocOptions_e.swOpenDocOptions_Silent
                        | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly;

            ModelDoc2? model = swApp.OpenDoc6(path, docType, options, "", ref errors, ref warnings);
            if (model == null)
            {
                Console.WriteLine("[export-feature-tree] OpenDoc6 failed. Errors: " + errors + ", warnings: " + warnings);
            }
            else if (warnings != 0)
            {
                Console.WriteLine("[export-feature-tree] Opened with warnings (code " + warnings + ").");
            }
            return model;
        }

        private static string BuildMarkdown(ModelDoc2 model, string modelPath)
        {
            StringBuilder sb = new StringBuilder();

            string configName = "";
            try
            {
                ConfigurationManager cfgMgr = model.ConfigurationManager;
                if (cfgMgr != null && cfgMgr.ActiveConfiguration != null)
                {
                    configName = cfgMgr.ActiveConfiguration.Name;
                }
            }
            catch
            {
                // drawings have no active configuration
            }

            sb.AppendLine("# Feature Tree: " + Path.GetFileName(modelPath));
            sb.AppendLine();
            sb.AppendLine("- **Path:** `" + modelPath + "`");
            sb.AppendLine("- **Document type:** " + (swDocumentTypes_e)model.GetType());
            if (configName.Length > 0)
            {
                sb.AppendLine("- **Active configuration:** " + configName);
            }
            sb.AppendLine("- **Exported:** " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("## Features");
            sb.AppendLine();

            Feature? feature = model.FirstFeature() as Feature;
            while (feature != null)
            {
                WriteFeature(sb, feature, 0);
                feature = feature.GetNextFeature() as Feature;
            }

            return sb.ToString();
        }

        private static void WriteFeature(StringBuilder sb, Feature feature, int depth)
        {
            string indent = new string(' ', depth * 2);

            sb.Append(indent);
            sb.Append("- **");
            sb.Append(feature.Name);
            sb.Append("** (`");
            sb.Append(feature.GetTypeName2());
            sb.Append("`)");

            if (feature.IsSuppressed())
            {
                sb.Append(" [suppressed]");
            }

            bool isWarning;
            int errorCode = feature.GetErrorCode2(out isWarning);
            if (errorCode != 0)
            {
                sb.Append(isWarning ? " [warning " : " [error ");
                sb.Append(errorCode);
                sb.Append(']');
            }

            string? comment = feature.Description;
            if (!string.IsNullOrWhiteSpace(comment))
            {
                sb.Append(" - ");
                sb.Append(comment.Replace("\r", " ").Replace("\n", " "));
            }

            sb.AppendLine();

            Feature? sub = feature.GetFirstSubFeature() as Feature;
            while (sub != null)
            {
                WriteFeature(sb, sub, depth + 1);
                sub = sub.GetNextSubFeature() as Feature;
            }
        }
    }
}