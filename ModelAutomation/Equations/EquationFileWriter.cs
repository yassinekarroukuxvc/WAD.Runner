using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class EquationFileWriter
{
    private static readonly Regex LinePattern = new(
        @"^\s*""(?<key>[^""]+)""\s*=.*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Write(string equationFilePath, EquationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
            throw new FileNotFoundException("Equation file not found.", equationFilePath);
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var managed = new Dictionary<string, string>(plan.ManagedEquations, StringComparer.OrdinalIgnoreCase);
        var dimensions = new Dictionary<string, WAD.Runner.DataManagement.Domain.Dimensions.Dimension>(
            plan.DimensionsByKey,
            StringComparer.OrdinalIgnoreCase);
        var zeroProvided = new HashSet<string>(plan.ZeroProvidedKeys, StringComparer.OrdinalIgnoreCase);
        var missingToZero = new HashSet<string>(plan.MissingKeysToZero, StringComparer.OrdinalIgnoreCase);
        var angleKeys = new HashSet<string>(plan.AngleKeys, StringComparer.OrdinalIgnoreCase);

        var encoding = DetectEncoding(equationFilePath);
        var raw = File.ReadAllText(equationFilePath, encoding);
        var newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var output = new List<string>();
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewritten = 0;

        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var match = LinePattern.Match(line);
            if (!match.Success)
            {
                output.Add(line);
                continue;
            }

            var key = match.Groups["key"].Value;
            existingKeys.Add(key);

            if (managed.TryGetValue(key, out var managedLine))
            {
                output.Add(managedLine);
                rewritten++;
                continue;
            }

            if (dimensions.TryGetValue(key, out var dimension))
            {
                if (!plan.WriteZeros && zeroProvided.Contains(key))
                {
                    output.Add(line);
                    continue;
                }

                output.Add(EquationFormatting.DimensionLine(key, dimension));
                rewritten++;
                continue;
            }

            if (plan.MissingDbKeysAsZero && missingToZero.Contains(key))
            {
                output.Add(MakeZeroLine(key, line, angleKeys));
                rewritten++;
                continue;
            }

            output.Add(line);
        }

        var appended = 0;

        foreach (var (key, line) in managed)
        {
            if (!existingKeys.Add(key)) continue;
            output.Add(line);
            appended++;
        }

        foreach (var (key, dimension) in dimensions)
        {
            if (managed.ContainsKey(key)) continue;
            if (!plan.WriteZeros && zeroProvided.Contains(key)) continue;
            if (!existingKeys.Add(key)) continue;

            output.Add(EquationFormatting.DimensionLine(key, dimension));
            appended++;
        }

        if (plan.MissingDbKeysAsZero)
        {
            foreach (var key in missingToZero)
            {
                if (!existingKeys.Add(key)) continue;
                output.Add($"\"{key}\" = 0{(angleKeys.Contains(key) ? "deg" : "in")}");
                appended++;
            }
        }

        WriteAtomically(equationFilePath, string.Join(newline, output), encoding);
        Logger.Success(
            $"[EquationFileWriter] Updated equation file. rewritten={rewritten}, " +
            $"appended={appended}, path={equationFilePath}");
    }

    private static string MakeZeroLine(string key, string existingLine, HashSet<string> angleKeys)
    {
        var isAngle =
            angleKeys.Contains(key) ||
            existingLine.Contains(
                "deg",
                StringComparison.OrdinalIgnoreCase);

        return $"\"{key}\" = 0{(isAngle ? "deg" : "in")}";
    }

    private static Encoding DetectEncoding(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        if (reader.Peek() >= 0) _ = reader.Read();
        return reader.CurrentEncoding;
    }

    private static void WriteAtomically(string destinationPath, string content, Encoding encoding)
    {
        var fullPath = Path.GetFullPath(destinationPath);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.WriteAllText(temporaryPath, content, encoding);
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
