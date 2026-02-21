using System.Text.RegularExpressions;

namespace Launcher.Core.Services;

public static partial class BenchmarkOutputParser
{
    [GeneratedRegex(@"(?i)(recommended|suggested)\D{0,20}(threads?)\D{0,20}(?<num>\d+)")]
    private static partial Regex RecommendedThreadsRegex();

    [GeneratedRegex(@"(?i)numSearchThreads\D{0,8}(?<num>\d+)")]
    private static partial Regex NumSearchThreadsRegex();

    public static int? ParseRecommendedThreads(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var regex1 = RecommendedThreadsRegex().Match(output);
        if (regex1.Success && int.TryParse(regex1.Groups["num"].Value, out var value1))
        {
            return value1;
        }

        var regex2 = NumSearchThreadsRegex().Match(output);
        if (regex2.Success && int.TryParse(regex2.Groups["num"].Value, out var value2))
        {
            return value2;
        }

        return null;
    }
}
