using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public class CfgEditorTests
{
    [Fact]
    public void UpsertNumSearchThreads_ShouldReplaceExistingValue()
    {
        const string cfg = """
                           maxVisits = 400
                           numSearchThreads = 4
                           ponderingEnabled = true
                           """;

        var output = CfgEditor.UpsertNumSearchThreads(cfg, 12);
        Assert.Contains("numSearchThreads = 12", output);
        Assert.DoesNotContain("numSearchThreads = 4", output);
    }
}
