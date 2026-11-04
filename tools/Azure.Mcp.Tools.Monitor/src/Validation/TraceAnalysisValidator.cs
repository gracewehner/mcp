// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;
using Microsoft.Mcp.Core.Commands;

namespace Azure.Mcp.Tools.Monitor.Validation;

internal static class TraceAnalysisValidator
{
    public static bool IsAnalysisIdentifier(string? value) =>
        value is { Length: > 0 and <= 1024 } &&
        Regex.IsMatch(value, @"\A[a-zA-Z_][a-zA-Z0-9_.]*\z", RegexOptions.NonBacktracking);

    public static void ValidateAnalysisWindow(DateTimeOffset start, DateTimeOffset end, ValidationResult result)
    {
        if (start >= end || end - start > TimeSpan.FromDays(1))
        {
            result.AddError("The analysis time range must be positive and no longer than one day.", "Invalid analysis time range.");
        }
    }

    public static void ValidateAnalysisMaxResults(int? maxResults, int maximum, ValidationResult result)
    {
        if (maxResults is < 1 || maxResults > maximum)
        {
            result.AddError($"--max-results must be between 1 and {maximum}.", "Invalid analysis max-results.");
        }
    }

    public static void ValidateCriticalPathPercentiles(int[]? percentiles, ValidationResult result)
    {
        if (percentiles is { Length: > 5 } || percentiles?.Any(p => p is < 1 or > 99) == true)
        {
            result.AddError("--percentiles accepts at most 5 values, each between 1 and 99.", "Invalid percentiles.");
        }
    }
}
