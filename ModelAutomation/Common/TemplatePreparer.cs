using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Common;

public static class TemplatePreparer
{
    /// <summary>
    /// Copies a template file to a working destination.
    /// Ensures the destination directory exists, optionally overwriting an existing file.
    /// </summary>
    public static void CopyTemplate(string source, string destination, bool overwrite = true)
    {
        Logger.Info($"[TemplatePreparer] Copy start → source='{source}', destination='{destination}', overwrite={overwrite}");

        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            Logger.Error("[TemplatePreparer] Source or destination is empty.");
            throw new ArgumentException("Source and destination must be non-empty paths.");
        }

        if (!File.Exists(source))
        {
            Logger.Error($"[TemplatePreparer] Template file not found: {source}");
            throw new FileNotFoundException("Template file not found.", source);
        }

        var destDir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destDir) && !Directory.Exists(destDir))
        {
            Logger.Info($"[TemplatePreparer] Creating destination directory: {destDir}");
            Directory.CreateDirectory(destDir);
            Logger.Success("[TemplatePreparer] Destination directory created.");
        }

        if (File.Exists(destination))
        {
            if (overwrite)
            {
                Logger.Warn($"[TemplatePreparer] Destination exists; deleting: {destination}");
                File.Delete(destination);
            }
            else
            {
                Logger.Blue($"[TemplatePreparer] Destination exists and overwrite=false; skipping copy: {destination}");
                return;
            }
        }

        File.Copy(source, destination);
        Logger.Success($"[TemplatePreparer] Copied template → {destination}");
    }
}