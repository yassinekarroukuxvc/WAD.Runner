using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WAD.Runner.Application;

namespace WAD.Runner.ModelAutomation.Equations;

public sealed class EquationFileWriter
{
    private static readonly Regex LineRx = new(@"^\s*""(?<key>[^""]+)""\s*=.*$", RegexOptions.Compiled);

    public void Write(string equationFilePath, EquationPlan plan)
    {
        if (string.IsNullOrWhiteSpace(equationFilePath) || !File.Exists(equationFilePath))
            throw new FileNotFoundException("Equation file not found.", equationFilePath);
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var managed = new Dictionary<string, string>(plan.ManagedEquations, StringComparer.OrdinalIgnoreCase);
        var dims = new Dictionary<string, WAD.Runner.DataManagement.Domain.Dimensions.Dimension>(plan.DimensionsByKey, StringComparer.OrdinalIgnoreCase);
        var zeros = new HashSet<string>(plan.ZeroProvidedKeys, StringComparer.OrdinalIgnoreCase);
        var missing = new HashSet<string>(plan.MissingKeysToZero, StringComparer.OrdinalIgnoreCase);
        var angleKeys = new HashSet<string>(plan.AngleKeys, StringComparer.OrdinalIgnoreCase);

        var encoding = GetEncoding(equationFilePath);
        var raw = File.ReadAllText(equationFilePath, encoding);
        var newline = raw.Contains("\r\n") ? "\r\n" : "\n";
        var output = new List<string>();
        var rewritten = 0;

        foreach (var line in raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var m = LineRx.Match(line);
            if (!m.Success)
            {
                output.Add(line);
                continue;
            }

            var key = m.Groups["key"].Value;
            if (managed.TryGetValue(key, out var managedLine))
            {
                output.Add(managedLine);
                rewritten++;
                continue;
            }

            if (dims.TryGetValue(key, out var dim))
            {
                if (!plan.WriteZeros && zeros.Contains(key))
                {
                    output.Add(line);
                    continue;
                }

                output.Add(EquationFormatting.DimensionLine(key, dim));
                rewritten++;
                continue;
            }

            if (plan.MissingDbKeysAsZero && missing.Contains(key))
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
            if (LineExists(output, key)) continue;
            output.Add(line);
            appended++;
        }

        foreach (var (key, dim) in dims)
        {
            if (managed.ContainsKey(key)) continue;
            if (!plan.WriteZeros && zeros.Contains(key)) continue;
            if (LineExists(output, key)) continue;
            output.Add(EquationFormatting.DimensionLine(key, dim));
            appended++;
        }

        if (plan.MissingDbKeysAsZero)
        {
            foreach (var key in missing)
            {
                if (LineExists(output, key)) continue;
                output.Add($"\"{key}\" = 0{(angleKeys.Contains(key) ? "deg" : "mm")}");
                appended++;
            }
        }

        File.WriteAllText(equationFilePath, string.Join(newline, output), encoding);
        Logger.Success($"[EquationFileWriter] Updated equation file. rewritten={rewritten}, appended={appended}, path={equationFilePath}");
    }

    private static bool LineExists(IEnumerable<string> lines, string key)
    {
        foreach (var line in lines)
        {
            var m = LineRx.Match(line);
            if (m.Success && string.Equals(m.Groups["key"].Value, key, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string MakeZeroLine(string key, string existingLine, HashSet<string> angleKeys)
    {
        var unit = existingLine.Contains("deg", StringComparison.OrdinalIgnoreCase) ? "deg" :
            existingLine.Contains("mm", StringComparison.OrdinalIgnoreCase) ? "mm" :
            existingLine.Contains("in", StringComparison.OrdinalIgnoreCase) ? "in" :
            angleKeys.Contains(key) ? "deg" : "mm";
        return $"\"{key}\" = 0{unit}";
    }

    private static Encoding GetEncoding(string path)
    {
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        if (reader.Peek() >= 0) _ = reader.Read();
        return reader.CurrentEncoding;
    }
}
