namespace Launcher.Core.Services;

public static class CfgEditor
{
    public static string Backup(string configPath, DateTimeOffset now)
    {
        var backupPath = $"{configPath}.bak.{now:yyyyMMddHHmmss}";
        File.Copy(configPath, backupPath, overwrite: false);
        return backupPath;
    }

    public static string UpsertNumSearchThreads(string cfgContent, int threads)
    {
        var lines = cfgContent.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var replaced = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("numSearchThreads", StringComparison.OrdinalIgnoreCase))
            {
                var indentation = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = $"{indentation}numSearchThreads = {threads}";
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            var next = lines.ToList();
            next.Add(string.Empty);
            next.Add($"numSearchThreads = {threads}");
            lines = next.ToArray();
        }

        return string.Join(Environment.NewLine, lines);
    }
}
