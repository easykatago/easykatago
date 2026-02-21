using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Launcher.Core.Abstractions;

namespace Launcher.Core.Services;

public sealed class LizzieConfigService : ILizzieConfigService
{
    private static readonly Encoding Utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding GbkEncoding = CreateGbkEncoding();

    private static readonly string[] CandidateFileNames =
    [
        "config.json",
        "config.txt",
        "lizzie.properties",
        "leelaz.command.txt",
        "settings.json"
    ];

    private static readonly string[] CommandKeys =
    [
        "engineCommand",
        "gtpCommand",
        "katagoCommand",
        "engine.command",
        "engine.commandLine",
        "leelaz.command"
    ];

    private static readonly string[] EstimateCommandKeys =
    [
        "estimate-command",
        "estimate.command",
        "ui.estimate-command"
    ];

    private static readonly string[] UseZenEstimateKeys =
    [
        "use-zen-estimate",
        "ui.use-zen-estimate"
    ];

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "jre",
        "jdk",
        "runtime",
        "jcef-bundle",
        "logs",
        "cache",
        "temp"
    };

    public async Task<LizzieConfigWriteResult> TryWriteEngineAsync(
        string lizziePath,
        string gtpCommand,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(gtpCommand))
        {
            return new LizzieConfigWriteResult(
                false,
                "Engine command is empty.",
                "Please generate a valid KataGo GTP command first.",
                null);
        }

        var workingDirectory = ResolveWorkingDirectory(lizziePath);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return new LizzieConfigWriteResult(
                false,
                "LizzieYzy directory does not exist.",
                "Cannot locate LizzieYzy working directory. Please configure the KataGo engine command manually in LizzieYzy.",
                gtpCommand);
        }

        var primaryConfigPath = Path.Combine(workingDirectory, "config.txt");
        var updatedFiles = new List<string>();

        var primaryResult = await EnsurePrimaryConfigAsync(primaryConfigPath, gtpCommand, cancellationToken);
        if (!primaryResult.Success)
        {
            var primaryFallbackPath = Path.Combine(workingDirectory, "easykatago-engine-command.txt");
            await File.WriteAllTextAsync(primaryFallbackPath, gtpCommand + Environment.NewLine, Utf8Strict, cancellationToken);
            var primaryGuide = $"Primary config write failed. Command file generated: {primaryFallbackPath}";
            return new LizzieConfigWriteResult(
                false,
                $"Primary config write failed: {primaryResult.Message}",
                primaryGuide,
                gtpCommand);
        }

        updatedFiles.Add(primaryConfigPath);
        var candidates = BuildCandidates(workingDirectory);
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file))
            {
                continue;
            }

            if (string.Equals(
                   Path.GetFullPath(file),
                   Path.GetFullPath(primaryConfigPath),
                   StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var success = await TryUpdateConfigAsync(file, gtpCommand, cancellationToken);
            if (!success)
            {
                continue;
            }

            updatedFiles.Add(file);
        }

        if (updatedFiles.Count > 0)
        {
            var names = string.Join(", ", updatedFiles.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Take(4));
            return new LizzieConfigWriteResult(true, $"Auto-config updated: {names}", null, null);
        }

        var fallbackCommandPath = Path.Combine(workingDirectory, "easykatago-engine-command.txt");
        await File.WriteAllTextAsync(fallbackCommandPath, gtpCommand + Environment.NewLine, Utf8Strict, cancellationToken);
        var fallbackGuide = $"No writable known config file was found. Command file generated: {fallbackCommandPath}";
        return new LizzieConfigWriteResult(false, "Automatic config write did not match a known format.", fallbackGuide, gtpCommand);
    }

    public async Task<LizziePreferenceWriteResult> TryWriteKataThreadPreferenceAsync(
        string lizziePath,
        int threads,
        bool autoLoad = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (threads < 1 || threads > 1024)
        {
            return new LizziePreferenceWriteResult(false, "Thread value must be in range 1-1024.");
        }

        var workingDirectory = ResolveWorkingDirectory(lizziePath);
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return new LizziePreferenceWriteResult(false, "LizzieYzy working directory does not exist.");
        }

        var candidates = new[]
        {
            Path.Combine(workingDirectory, "config.txt"),
            Path.Combine(workingDirectory, "config.json")
        };

        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(file))
            {
                continue;
            }

            if (await TryUpdateThreadPreferenceJsonAsync(file, threads, autoLoad, cancellationToken))
            {
                return new LizziePreferenceWriteResult(
                    true,
                    $"KataGo thread preference synced: numSearchThreads={threads}, file={Path.GetFileName(file)}");
            }

            if (await TryUpdateThreadPreferenceKeyValueAsync(file, threads, autoLoad, cancellationToken))
            {
                return new LizziePreferenceWriteResult(
                    true,
                    $"KataGo thread preference synced (key-value): numSearchThreads={threads}, file={Path.GetFileName(file)}");
            }
        }

        return new LizziePreferenceWriteResult(false, "No writable LizzieYzy JSON config file was found.");
    }

    private static async Task<(bool Success, string Message)> EnsurePrimaryConfigAsync(
        string primaryConfigPath,
        string gtpCommand,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(primaryConfigPath) ?? ".");

        if (File.Exists(primaryConfigPath))
        {
            var updated = await TryUpdateConfigAsync(primaryConfigPath, gtpCommand, cancellationToken);
            if (updated)
            {
                return (true, "Primary config updated.");
            }

            try
            {
                var backupPath = primaryConfigPath + ".bak-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss");
                File.Copy(primaryConfigPath, backupPath, overwrite: true);
            }
            catch
            {
                // best effort
            }

            var rebuilt = BuildMinimalLizzieConfigJson(gtpCommand);
            await File.WriteAllTextAsync(primaryConfigPath, rebuilt, Utf8Strict, cancellationToken);
            return (true, "Primary config rebuilt with default engine.");
        }

        var minimalJson = BuildMinimalLizzieConfigJson(gtpCommand);
        await File.WriteAllTextAsync(primaryConfigPath, minimalJson, Utf8Strict, cancellationToken);
        return (true, "Primary config created with default engine.");
    }

    private static string BuildMinimalLizzieConfigJson(string gtpCommand)
    {
        var root = new JsonObject
        {
            ["leelaz"] = new JsonObject
            {
                ["limit-max-suggestion"] = 10,
                ["analyze-update-interval-centisec"] = 10,
                ["enable-lizzie-cache"] = true,
                ["limit-branch-length"] = 0,
                ["max-analyze-time-seconds"] = 600,
                ["max-game-thinking-time-seconds"] = 2,
                ["engine-settings-list"] = new JsonArray(BuildDefaultEngineSettings(gtpCommand))
            },
            ["ui"] = new JsonObject
            {
                ["first-load-katago"] = false,
                ["autoload-default"] = true,
                ["autoload-last"] = false,
                ["autoload-empty"] = false,
                ["estimate-command"] = gtpCommand,
                ["use-zen-estimate"] = false,
                ["txt-kata-engine-threads"] = string.Empty,
                ["autoload-kata-engine-threads"] = false,
                ["board-size"] = 19,
                ["limit-time"] = true,
                ["limit-playouts"] = 100000,
                ["show-subboard"] = true,
                ["show-comment"] = true,
                ["show-status"] = true
            },
            ["engineCommand"] = gtpCommand
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static IReadOnlyList<string> BuildCandidates(string workingDirectory)
    {
        return new[]
            {
                Path.Combine(workingDirectory, "config.txt"),
                Path.Combine(workingDirectory, "config.json"),
                Path.Combine(workingDirectory, "leelaz.command.txt"),
                Path.Combine(workingDirectory, "settings.json"),
                Path.Combine(workingDirectory, "config_wrong.txt"),
                Path.Combine(workingDirectory, "test_commands.txt"),
                Path.Combine(workingDirectory, "config_readboard.txt"),
                Path.Combine(workingDirectory, "config_readboard_others.txt"),
                Path.Combine(workingDirectory, "readboard", "config_readboard.txt"),
                Path.Combine(workingDirectory, "readboard", "config_readboard_others.txt")
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> EnumerateCandidateFiles(string root, int maxDepth)
    {
        var queue = new Queue<(string Dir, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsPotentialConfigFile(file))
                {
                    yield return file;
                }
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (IgnoredDirectoryNames.Contains(name))
                {
                    continue;
                }

                queue.Enqueue((sub, depth + 1));
            }
        }
    }

    private static bool IsPotentialConfigFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        foreach (var known in CandidateFileNames)
        {
            if (string.Equals(fileName, known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var lower = fileName.ToLowerInvariant();
        var extension = Path.GetExtension(lower);
        var allowedExt = extension is ".json" or ".txt" or ".properties" or ".cfg" or ".yaml" or ".yml";
        if (!allowedExt)
        {
            return false;
        }

        return lower.StartsWith("config", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("lizzie", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("engine", StringComparison.OrdinalIgnoreCase)
               || lower.Contains("command", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveWorkingDirectory(string lizziePath)
    {
        if (string.IsNullOrWhiteSpace(lizziePath))
        {
            return string.Empty;
        }

        if (Directory.Exists(lizziePath))
        {
            return Path.GetFullPath(lizziePath);
        }

        var fullPath = Path.GetFullPath(lizziePath);
        return Path.GetDirectoryName(fullPath) ?? string.Empty;
    }

    private static async Task<bool> TryUpdateConfigAsync(string filePath, string gtpCommand, CancellationToken cancellationToken)
    {
        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return await TryUpdateJsonConfigAsync(filePath, gtpCommand, cancellationToken);
        }

        var isJsonLike = await IsLikelyJsonAsync(filePath, cancellationToken);
        return isJsonLike
            ? await TryUpdateJsonConfigAsync(filePath, gtpCommand, cancellationToken)
            : await TryUpdateKeyValueConfigAsync(filePath, gtpCommand, cancellationToken);
    }

    private static async Task<bool> IsLikelyJsonAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var (text, _) = await ReadTextWithFallbackAsync(filePath, cancellationToken);
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                return c is '{' or '[';
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static async Task<bool> TryUpdateJsonConfigAsync(string filePath, string gtpCommand, CancellationToken cancellationToken)
    {
        try
        {
            var (json, encoding) = await ReadTextWithFallbackAsync(filePath, cancellationToken);
            json = StripLeadingBom(json);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null)
            {
                return false;
            }

            var updated = false;
            updated |= TryUpdateLizzieEstimateSettings(root, gtpCommand);
            updated |= TryUpdateLizzieEngineSettings(root, gtpCommand);

            foreach (var key in CommandKeys)
            {
                updated |= TrySetPathIfExists(root, key.Split('.'), gtpCommand);
            }

            if (!updated)
            {
                root["engineCommand"] = gtpCommand;
            }

            var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            await File.WriteAllTextAsync(filePath, output, GetWriteEncoding(encoding), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryUpdateLizzieEngineSettings(JsonObject root, string gtpCommand)
    {
        var leelaz = root["leelaz"] as JsonObject;
        if (leelaz is null)
        {
            leelaz = new JsonObject();
            root["leelaz"] = leelaz;
        }

        var engineList = leelaz["engine-settings-list"] as JsonArray;
        if (engineList is null)
        {
            engineList = new JsonArray();
            leelaz["engine-settings-list"] = engineList;
        }

        JsonObject? selectedEngine = null;
        var selectedIndex = -1;
        for (var i = 0; i < engineList.Count; i++)
        {
            if (engineList[i] is JsonObject obj)
            {
                var isDefault = CoalesceBool(obj["isDefault"], false);
                if (selectedEngine is null)
                {
                    selectedEngine = obj;
                    selectedIndex = i;
                }

                if (isDefault)
                {
                    selectedEngine = obj;
                    selectedIndex = i;
                    break;
                }
            }
        }

        var engineName = selectedEngine is null
            ? "EasyKataGo"
            : CoalesceString(selectedEngine["name"], "EasyKataGo");
        var normalizedEngine = BuildDefaultEngineSettings(gtpCommand, engineName);

        if (selectedEngine is null)
        {
            engineList.Add(normalizedEngine);
            selectedIndex = engineList.Count - 1;
        }
        else
        {
            engineList[selectedIndex] = normalizedEngine;
        }

        for (var i = 0; i < engineList.Count; i++)
        {
            if (engineList[i] is not JsonObject obj || i == selectedIndex)
            {
                continue;
            }

            obj["isDefault"] = false;
        }

        if (root["ui"] is JsonObject ui)
        {
            ui["first-load-katago"] = false;
            ui["autoload-default"] = true;
            ui["autoload-last"] = false;
            ui["autoload-empty"] = false;
        }

        root["engineCommand"] = gtpCommand;
        return true;
    }

    private static bool TryUpdateLizzieEstimateSettings(JsonObject root, string gtpCommand)
    {
        var ui = root["ui"] as JsonObject;
        if (ui is null)
        {
            ui = new JsonObject();
            root["ui"] = ui;
        }

        ui["estimate-command"] = gtpCommand;
        ui["use-zen-estimate"] = false;
        return true;
    }

    private static async Task<bool> TryUpdateThreadPreferenceJsonAsync(
        string filePath,
        int threads,
        bool autoLoad,
        CancellationToken cancellationToken)
    {
        try
        {
            var (json, encoding) = await ReadTextWithFallbackAsync(filePath, cancellationToken);
            json = StripLeadingBom(json);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null)
            {
                return false;
            }

            var ui = root["ui"] as JsonObject;
            if (ui is null)
            {
                ui = new JsonObject();
                root["ui"] = ui;
            }

            ui["txt-kata-engine-threads"] = threads.ToString();
            ui["autoload-kata-engine-threads"] = autoLoad;
            ui["first-load-katago"] = false;

            var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            await File.WriteAllTextAsync(filePath, output, GetWriteEncoding(encoding), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryUpdateThreadPreferenceKeyValueAsync(
        string filePath,
        int threads,
        bool autoLoad,
        CancellationToken cancellationToken)
    {
        try
        {
            var (rawText, encoding) = await ReadTextWithFallbackAsync(filePath, cancellationToken);
            rawText = StripLeadingBom(rawText);
            var newline = rawText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = rawText.Replace("\r\n", "\n").Split('\n').ToList();

            var threadUpdated = false;
            var autoLoadUpdated = false;
            var firstLoadUpdated = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.Length == 0)
                {
                    continue;
                }

                var indentLength = line.Length - trimmed.Length;
                var indent = indentLength > 0 ? line[..indentLength] : string.Empty;

                if (StartsWithKey(trimmed, "txt-kata-engine-threads"))
                {
                    lines[i] = $"{indent}txt-kata-engine-threads = {threads}";
                    threadUpdated = true;
                    continue;
                }

                if (StartsWithKey(trimmed, "autoload-kata-engine-threads"))
                {
                    lines[i] = $"{indent}autoload-kata-engine-threads = {autoLoad.ToString().ToLowerInvariant()}";
                    autoLoadUpdated = true;
                    continue;
                }

                if (StartsWithKey(trimmed, "first-load-katago"))
                {
                    lines[i] = $"{indent}first-load-katago = false";
                    firstLoadUpdated = true;
                }
            }

            if (!threadUpdated)
            {
                lines.Add($"txt-kata-engine-threads = {threads}");
            }

            if (!autoLoadUpdated)
            {
                lines.Add($"autoload-kata-engine-threads = {autoLoad.ToString().ToLowerInvariant()}");
            }

            if (!firstLoadUpdated)
            {
                lines.Add("first-load-katago = false");
            }

            var output = string.Join(newline, lines) + newline;
            await File.WriteAllTextAsync(filePath, output, GetWriteEncoding(encoding), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static JsonObject BuildDefaultEngineSettings(string gtpCommand, string? engineName = null)
    {
        return new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(engineName) ? "EasyKataGo" : engineName,
            ["command"] = gtpCommand,
            ["isDefault"] = true,
            ["preload"] = false,
            ["width"] = 19,
            ["height"] = 19,
            ["komi"] = 7.5
        };
    }

    private static string CoalesceString(JsonNode? node, string fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            var value = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }

    private static bool CoalesceBool(JsonNode? node, bool fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return fallback;
        }
    }

    private static int CoalesceInt(JsonNode? node, int fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            return fallback;
        }
    }

    private static double CoalesceDouble(JsonNode? node, double fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            return node.GetValue<double>();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool TrySetPathIfExists(JsonObject root, IReadOnlyList<string> segments, string value)
    {
        if (segments.Count == 0)
        {
            return false;
        }

        JsonObject current = root;
        for (var i = 0; i < segments.Count - 1; i++)
        {
            var name = segments[i];
            if (current[name] is JsonObject child)
            {
                current = child;
                continue;
            }

            return false;
        }

        var leaf = segments[^1];
        if (!current.ContainsKey(leaf))
        {
            return false;
        }

        current[leaf] = value;
        return true;
    }

    private static async Task<bool> TryUpdateKeyValueConfigAsync(string filePath, string gtpCommand, CancellationToken cancellationToken)
    {
        try
        {
            var (rawText, encoding) = await ReadTextWithFallbackAsync(filePath, cancellationToken);
            rawText = StripLeadingBom(rawText);
            var newline = rawText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = rawText.Replace("\r\n", "\n").Split('\n').ToList();
            var commandUpdated = false;
            var estimateCommandUpdated = false;
            var useZenEstimateUpdated = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#", StringComparison.Ordinal) ||
                    trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.Length == 0)
                {
                    continue;
                }

                var indentLength = line.Length - trimmed.Length;
                var indent = indentLength > 0 ? line[..indentLength] : string.Empty;

                foreach (var key in CommandKeys)
                {
                    if (!StartsWithKey(trimmed, key))
                    {
                        continue;
                    }

                    lines[i] = $"{indent}{key} = {gtpCommand}";
                    commandUpdated = true;
                }

                foreach (var key in EstimateCommandKeys)
                {
                    if (!StartsWithKey(trimmed, key))
                    {
                        continue;
                    }

                    lines[i] = $"{indent}{key} = {gtpCommand}";
                    estimateCommandUpdated = true;
                }

                foreach (var key in UseZenEstimateKeys)
                {
                    if (!StartsWithKey(trimmed, key))
                    {
                        continue;
                    }

                    lines[i] = $"{indent}{key} = false";
                    useZenEstimateUpdated = true;
                }
            }

            if (!commandUpdated)
            {
                lines.Add($"engineCommand = {gtpCommand}");
            }

            if (!estimateCommandUpdated)
            {
                lines.Add($"estimate-command = {gtpCommand}");
            }

            if (!useZenEstimateUpdated)
            {
                lines.Add("use-zen-estimate = false");
            }

            var output = string.Join(newline, lines) + newline;
            await File.WriteAllTextAsync(filePath, output, GetWriteEncoding(encoding), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool StartsWithKey(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (line.Length == key.Length)
        {
            return true;
        }

        var c = line[key.Length];
        return c is '=' or ':' or ' ' or '\t';
    }

    private static async Task<(string Text, Encoding Encoding)> ReadTextWithFallbackAsync(string filePath, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        if (TryDecodeWithBomOrHeuristic(bytes, out var decoded))
        {
            return (StripLeadingBom(decoded.Text), decoded.Encoding);
        }

        try
        {
            return (StripLeadingBom(Utf8Strict.GetString(bytes)), Utf8Strict);
        }
        catch (DecoderFallbackException)
        {
            return (StripLeadingBom(GbkEncoding.GetString(bytes)), GbkEncoding);
        }
    }

    private static Encoding GetWriteEncoding(Encoding sourceEncoding)
    {
        if (ReferenceEquals(sourceEncoding, Encoding.Unicode) ||
            ReferenceEquals(sourceEncoding, Encoding.BigEndianUnicode))
        {
            return sourceEncoding;
        }

        return Utf8Strict;
    }

    private static string StripLeadingBom(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var start = 0;
        while (start < text.Length && text[start] == '\uFEFF')
        {
            start++;
        }

        return start == 0 ? text : text[start..];
    }

    private static bool TryDecodeWithBomOrHeuristic(byte[] bytes, out (string Text, Encoding Encoding) decoded)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF)
        {
            decoded = (Encoding.UTF8.GetString(bytes), Encoding.UTF8);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            decoded = (Encoding.Unicode.GetString(bytes), Encoding.Unicode);
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            decoded = (Encoding.BigEndianUnicode.GetString(bytes), Encoding.BigEndianUnicode);
            return true;
        }

        if (LooksLikeUtf16LeWithoutBom(bytes))
        {
            decoded = (Encoding.Unicode.GetString(bytes), Encoding.Unicode);
            return true;
        }

        if (LooksLikeUtf16BeWithoutBom(bytes))
        {
            decoded = (Encoding.BigEndianUnicode.GetString(bytes), Encoding.BigEndianUnicode);
            return true;
        }

        decoded = default;
        return false;
    }

    private static bool LooksLikeUtf16LeWithoutBom(byte[] bytes)
    {
        if (bytes.Length < 8 || (bytes.Length % 2) != 0)
        {
            return false;
        }

        var samplePairs = Math.Min(40, bytes.Length / 2);
        var zeroHighBytes = 0;
        for (var i = 0; i < samplePairs; i++)
        {
            if (bytes[i * 2 + 1] == 0x00)
            {
                zeroHighBytes++;
            }
        }

        return zeroHighBytes >= samplePairs * 0.6;
    }

    private static bool LooksLikeUtf16BeWithoutBom(byte[] bytes)
    {
        if (bytes.Length < 8 || (bytes.Length % 2) != 0)
        {
            return false;
        }

        var samplePairs = Math.Min(40, bytes.Length / 2);
        var zeroLowBytes = 0;
        for (var i = 0; i < samplePairs; i++)
        {
            if (bytes[i * 2] == 0x00)
            {
                zeroLowBytes++;
            }
        }

        return zeroLowBytes >= samplePairs * 0.6;
    }

    private static Encoding CreateGbkEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030");
    }
}
