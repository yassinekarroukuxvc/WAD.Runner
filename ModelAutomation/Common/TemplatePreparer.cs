using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Common;

public static class TemplatePreparer
{


    public static void CopyTemplate(string source, string destination, bool overwrite = true)
    {

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
            Directory.CreateDirectory(destDir);
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
                return;
            }
        }

        File.Copy(source, destination);
    }
}
