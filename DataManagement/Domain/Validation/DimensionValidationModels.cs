using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using WAD.Runner.DataManagement.Domain.Wedge;

namespace WAD.Runner.DataManagement.Domain.Validation;

public sealed record DimensionValidationIssue(
    string ArticleNumber,
    WedgeType WedgeType,
    string RequirementType,
    string RuleName,
    string Dimension,
    string Message);

public sealed record DimensionValidationResult(
    string ArticleNumber,
    WedgeType WedgeType,
    IReadOnlyList<DimensionValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public string ToUserMessage()
    {
        if (IsValid)
        {
            return
                $"Dimension validation passed for article " +
                $"{ArticleNumber} ({WedgeType}).";
        }

        var articleCount = Issues
            .Select(i => i.ArticleNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var sb = new StringBuilder();

        sb.AppendLine(
            articleCount == 1
                ? $"Validation failed for article {ArticleNumber} " +
                  $"({WedgeType}). Automation aborted."
                : $"Validation failed for {articleCount} articles. " +
                  "Automation aborted.");

        sb.AppendLine("Validation issues:");

        foreach (var issue in Issues)
        {
            sb.AppendLine(
                $" - Article {issue.ArticleNumber} " +
                $"({issue.WedgeType}) - {issue.Dimension}: " +
                $"{issue.Message} " +
                $"[{issue.RequirementType}; {issue.RuleName}]");
        }

        return sb.ToString().TrimEnd();
    }
}

public sealed class WedgeDimensionValidationException
    : InvalidOperationException
{
    public DimensionValidationResult Result { get; }

    public WedgeDimensionValidationException(
        DimensionValidationResult result)
        : base(
            result?.ToUserMessage() ??
            "Dimension validation failed.")
    {
        Result =
            result ??
            throw new ArgumentNullException(nameof(result));
    }
}
