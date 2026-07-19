using System;
using System.IO;
using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Common;

public static class TemplatePreparer
{
    public static void CopyTemplate(string source, string destination, bool overwrite = true)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("Source and destination must be non-empty paths.");

        var sourceFullPath = Path.GetFullPath(source);
        var destinationFullPath = Path.GetFullPath(destination);

        if (!File.Exists(sourceFullPath))
            throw new FileNotFoundException("Template file not found.", sourceFullPath);

        if (string.Equals(sourceFullPath, destinationFullPath, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info($"[TemplatePreparer] Source already matches destination: {sourceFullPath}");
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationFullPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        if (File.Exists(destinationFullPath) && !overwrite)
        {
            Logger.Info($"[TemplatePreparer] Destination exists; overwrite disabled: {destinationFullPath}");
            return;
        }

        var temporaryPath = destinationFullPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.Copy(sourceFullPath, temporaryPath, overwrite: true);

            if (File.Exists(destinationFullPath))
            {
                var attributes = File.GetAttributes(destinationFullPath);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(destinationFullPath, attributes & ~FileAttributes.ReadOnly);
            }

            File.Move(temporaryPath, destinationFullPath, overwrite: true);
            Logger.Info($"[TemplatePreparer] Copied template -> {destinationFullPath}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
