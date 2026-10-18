using System.Text.Json;
using System.Text.Json.Nodes;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Apps;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Read-only tools that let Claude explore the current scan and the installed apps on demand. Results are capped in
/// size, and every path must belong to the scan, so Claude only ever sees metadata the user's question needs.
/// </summary>
public sealed class StorageToolbox(
    IScanSession session,
    ICleanupRiskAnalyzer riskAnalyzer,
    ICleanupPlanner planner,
    IInstalledAppsService apps,
    TimeProvider clock) : IStorageToolbox
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private const string Definitions = """
        [
          {
            "name": "list_folder",
            "description": "List the contents of a scanned folder, largest first, with size, type, last-modified date and cleanup risk of each item.",
            "input_schema": {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Full path of a folder in the scan." },
                "limit": { "type": "integer", "description": "Maximum items to return (default 20, max 50)." }
              },
              "required": ["path"]
            }
          },
          {
            "name": "find_large_files",
            "description": "Find the largest files anywhere below a folder.",
            "input_schema": {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Folder to search. Defaults to the scan root." },
                "min_size_mb": { "type": "number", "description": "Only files at least this large, in MB (default 100)." },
                "limit": { "type": "integer", "description": "Maximum files to return (default 20, max 50)." }
              }
            }
          },
          {
            "name": "search_scan",
            "description": "Find files and folders whose name contains some text, largest first.",
            "input_schema": {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Text to look for in names, e.g. 'cache' or '.iso'." },
                "limit": { "type": "integer", "description": "Maximum results (default 20, max 50)." }
              },
              "required": ["query"]
            }
          },
          {
            "name": "get_item_details",
            "description": "Get the size, dates, cleanup risk with its reason and, for folders, a breakdown by file type.",
            "input_schema": {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Full path of a file or folder in the scan." }
              },
              "required": ["path"]
            }
          },
          {
            "name": "plan_cleanup",
            "description": "Compute SpaceLens' cleanup plan for a space goal: low-risk groups first, then items that need review. Never includes high-risk or protected items.",
            "input_schema": {
              "type": "object",
              "properties": {
                "goal_gb": { "type": "number", "description": "How much space to free, in GB." }
              },
              "required": ["goal_gb"]
            }
          },
          {
            "name": "list_installed_apps",
            "description": "List installed desktop programs, largest first, with size, install date, when each was last started from Start, the taskbar or File Explorer, and whether it can be uninstalled.",
            "input_schema": {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "description": "Maximum apps to return (default 20, max 50)." }
              }
            }
          },
          {
            "name": "compare_with_previous_scan",
            "description": "Show which folders grew or shrank and which large files appeared since the previous scan of the same location.",
            "input_schema": { "type": "object", "properties": {} }
          }
        ]
        """;

    public JsonArray CreateDefinitions() => JsonNode.Parse(Definitions)!.AsArray();

    public string Describe(string toolName, JsonObject input) => toolName switch
    {
        "list_folder" => $"Looking inside {NameOf(Text(input, "path"))}",
        "find_large_files" => "Finding large files",
        "search_scan" => $"Searching for \"{Text(input, "query")}\"",
        "get_item_details" => $"Checking {NameOf(Text(input, "path"))}",
        "plan_cleanup" => $"Planning how to free {Number(input, "goal_gb", 0):0.#} GB",
        "compare_with_previous_scan" => "Comparing with your previous scan",
        "list_installed_apps" => "Looking at your installed apps",
        _ => "Working"
    };

    public async Task<ToolResult> ExecuteAsync(string toolName, JsonObject input, CancellationToken cancellationToken)
    {
        if (toolName == "list_installed_apps")
        {
            return await ListAppsAsync(input, cancellationToken);
        }

        if (session.Current is null)
        {
            return Error("No scan is loaded yet.");
        }

        try
        {
            return toolName switch
            {
                "list_folder" => await session.ReadAsync(scan => ListFolder(scan, input), cancellationToken),
                "find_large_files" => await session.ReadAsync(scan => FindLargeFiles(scan, input), cancellationToken),
                "search_scan" => await session.ReadAsync(scan => Search(scan, input, cancellationToken), cancellationToken),
                "get_item_details" => await session.ReadAsync(scan => GetDetails(scan, input), cancellationToken),
                "plan_cleanup" => PlanCleanup(input),
                "compare_with_previous_scan" => CompareWithPrevious(),
                _ => Error($"Unknown tool '{toolName}'.")
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Error(exception.Message);
        }
    }

    private ToolResult ListFolder(ScanResult scan, JsonObject input)
    {
        if (Resolve(scan, Text(input, "path")) is not FolderNode folder)
        {
            return Error("That path isn't a folder in the current scan. Use search_scan to find it.");
        }

        var limit = Limit(input);
        var children = folder.Children.OrderByDescending(c => c.Size).ToList();
        return Json(new
        {
            Path = folder.FullPath,
            Size = AiContextBuilder.Size(folder.Size),
            Files = folder.FileCount,
            Folders = folder.FolderCount,
            Unreadable = folder.IsUnreadable ? true : (bool?)null,
            Items = children.Take(limit).Select(Summarize).ToList(),
            NotShown = Math.Max(0, children.Count - limit)
        });
    }

    private ToolResult FindLargeFiles(ScanResult scan, JsonObject input)
    {
        var path = Text(input, "path");
        var root = string.IsNullOrWhiteSpace(path) ? scan.Root : Resolve(scan, path) as FolderNode;
        if (root is null)
        {
            return Error("That path isn't a folder in the current scan.");
        }

        var minBytes = (long)(Number(input, "min_size_mb", 100) * ByteSize.MB);
        var files = StorageAnalyzer.LargestFiles(root, Limit(input), Math.Max(1, minBytes));
        return Json(new { Folder = root.FullPath, Files = files.Select(f => SummarizeWithPath(f)).ToList() });
    }

    private ToolResult Search(ScanResult scan, JsonObject input, CancellationToken cancellationToken)
    {
        var query = Text(input, "query");
        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("Provide some text to search for.");
        }

        var result = StorageAnalyzer.Search(scan.Root, query, Limit(input), cancellationToken);
        return Json(new { Query = query, result.TotalMatches, Items = result.Items.Select(SummarizeWithPath).ToList() });
    }

    private ToolResult GetDetails(ScanResult scan, JsonObject input)
    {
        var node = Resolve(scan, Text(input, "path"));
        if (node is null)
        {
            return Error("That path isn't part of the current scan.");
        }

        var risk = riskAnalyzer.Assess(node);
        var folder = node as FolderNode;
        return Json(new
        {
            Path = node.FullPath,
            Type = FileTypeClassifier.GetTypeName(node),
            Size = AiContextBuilder.Size(node.Size),
            Modified = node.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd"),
            Risk = risk.Label,
            RiskReason = risk.Reason,
            Files = folder?.FileCount,
            Folders = folder?.FolderCount,
            ByType = folder is null
                ? null
                : StorageAnalyzer.TypeBreakdown(folder)
                    .Take(6)
                    .Select(s => new { Category = s.Label, Size = AiContextBuilder.Size(s.Bytes) })
                    .ToList()
        });
    }

    private ToolResult PlanCleanup(JsonObject input)
    {
        var goalGb = Number(input, "goal_gb", 0);
        if (goalGb is <= 0 or > 100_000)
        {
            return Error("goal_gb must be a positive number of gigabytes.");
        }

        var plan = planner.CreatePlan(session.Cleanup, (long)(goalGb * ByteSize.GB));
        var content = JsonSerializer.Serialize(new
        {
            Goal = AiContextBuilder.Size(plan.GoalBytes),
            Planned = AiContextBuilder.Size(plan.PlannedBytes),
            plan.MeetsGoal,
            plan.Summary,
            Steps = plan.Steps.Select(s => new
            {
                s.Opportunity.Title,
                Risk = s.Opportunity.Risk.ToLabel(),
                Size = AiContextBuilder.Size(s.Bytes),
                Items = s.Items.Count,
                Why = s.Opportunity.Description,
                Examples = s.Items.Take(5).Select(i => i.FullPath).ToList()
            }).ToList()
        }, AiContextBuilder.JsonOptions);
        return new ToolResult(content, Plan: plan);
    }

    private ToolResult CompareWithPrevious()
    {
        var growth = session.Growth;
        if (growth is null)
        {
            return Error("There's no earlier completed scan of this location to compare with. Scan again later to see changes.");
        }

        return Json(new
        {
            PreviousScan = growth.Before.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            CurrentScan = growth.After.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            NetChange = ByteSize.FormatDelta(growth.NetChange),
            Grew = growth.Grew.Select(c => new { c.Path, Change = ByteSize.FormatDelta(c.Delta), New = c.IsNew }).ToList(),
            Shrank = growth.Shrank.Take(8).Select(c => new { c.Path, Change = ByteSize.FormatDelta(c.Delta) }).ToList(),
            NewLargeFiles = growth.NewLargeFiles.Select(f => new { f.Path, Size = AiContextBuilder.Size(f.AfterBytes), f.IsNew }).ToList(),
            Note = "Folders under 1 MB and files under 100 MB are not recorded in history."
        });
    }

    private async Task<ToolResult> ListAppsAsync(JsonObject input, CancellationToken cancellationToken)
    {
        var installed = await Task.Run(apps.GetInstalledApps, cancellationToken);
        var scan = session.Current;
        var sized = scan is null
            ? installed.Select(app => (App: app, Bytes: app.SizeBytes ?? 0)).ToList()
            : await session.ReadAsync(s => installed.Select(app => (App: app, Bytes: AppSize.Best(app, s))).ToList(), cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        return Json(new
        {
            Count = sized.Count,
            Apps = sized.OrderByDescending(a => a.Bytes).Take(Limit(input)).Select(a => new
            {
                a.App.Name,
                a.App.Publisher,
                Size = a.Bytes > 0 ? AiContextBuilder.Size(a.Bytes) : "unknown",
                Installed = a.App.InstalledOn?.ToString("yyyy-MM-dd"),
                LastUsed = a.App.LastUsedUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "not recorded",
                DaysSinceUsed = a.App.LastUsedUtc is { } used ? (int?)(now - used).TotalDays : null,
                a.App.CanUninstall
            }).ToList(),
            Note = "Last used is only recorded for programs started from Start, the taskbar or File Explorer; "
                + "an app started by a launcher or a game client may show as not recorded although it's used."
        });
    }

    private object Summarize(StorageNode node) => new
    {
        node.Name,
        Type = FileTypeClassifier.GetTypeName(node),
        Size = AiContextBuilder.Size(node.Size),
        Modified = node.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd"),
        Risk = riskAnalyzer.Assess(node).Label
    };

    private object SummarizeWithPath(StorageNode node) => new
    {
        Path = node.FullPath,
        Size = AiContextBuilder.Size(node.Size),
        Modified = node.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd"),
        Risk = riskAnalyzer.Assess(node).Label
    };

    private static StorageNode? Resolve(ScanResult scan, string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : scan.Root.Find(path);

    private static int Limit(JsonObject input) => (int)Math.Clamp(Number(input, "limit", DefaultLimit), 1, MaxLimit);

    private static string? Text(JsonObject input, string name) =>
        input[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static double Number(JsonObject input, string name, double fallback) =>
        input[name] is JsonValue value && value.TryGetValue<double>(out var number) ? number : fallback;

    private static string NameOf(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "a folder" : System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path;

    private static ToolResult Json(object value) => new(JsonSerializer.Serialize(value, AiContextBuilder.JsonOptions));

    private static ToolResult Error(string message) => new(message, IsError: true);
}
