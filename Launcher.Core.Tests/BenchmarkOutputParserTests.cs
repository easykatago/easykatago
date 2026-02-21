using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public class BenchmarkOutputParserTests
{
    [Fact]
    public void ParseRecommendedThreads_ShouldReturnThreadCount()
    {
        const string output = "Recommended threads: 10";
        var result = BenchmarkOutputParser.ParseRecommendedThreads(output);
        Assert.Equal(10, result);
    }
}
