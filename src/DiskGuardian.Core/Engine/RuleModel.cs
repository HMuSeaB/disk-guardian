using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DiskGuardian.Core.Engine;

public sealed class RuleConfig
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("updated")]
    public string Updated { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("vars")]
    public Dictionary<string, string> Vars { get; set; } = new();

    [JsonPropertyName("levels")]
    public Dictionary<string, string> Levels { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<RuleItem> Rules { get; set; } = new();
}

public sealed class RuleItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("paths")]
    public List<string>? Paths { get; set; }

    [JsonPropertyName("discover")]
    public DiscoverConfig? Discover { get; set; }

    [JsonPropertyName("size_hint")]
    public string SizeHint { get; set; } = string.Empty;

    [JsonPropertyName("rebuild")]
    public string Rebuild { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("why")]
    public string Why { get; set; } = string.Empty;

    [JsonPropertyName("allow_inside_deny")]
    public bool AllowInsideDeny { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "tree"; // "tree" | "old-files"

    [JsonPropertyName("keep_days")]
    public int KeepDays { get; set; } = 0;
}

public sealed class DiscoverConfig
{
    [JsonPropertyName("roots")]
    public List<string> Roots { get; set; } = new();

    [JsonPropertyName("dirname")]
    public string Dirname { get; set; } = string.Empty;

    [JsonPropertyName("require_sibling")]
    public string? RequireSibling { get; set; }

    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; set; } = 5;

    [JsonPropertyName("exclude_contains")]
    public List<string>? ExcludeContains { get; set; }
}

public sealed class PlanRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; }
    public List<string> ResolvedPaths { get; set; } = new();
    public List<string> ExistingPaths { get; set; } = new();
    public long SizeBytes { get; set; }
    public string SizeHuman { get; set; } = "0 B";
    public int FilesCount { get; set; }
    public int ErrorsCount { get; set; }
    public string Status { get; set; } = "ready"; // "ready" | "blocked" | "need-admin" | "clean"
    public List<string> BlockReasons { get; set; } = new();
    public string Rebuild { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string Mode { get; set; } = "tree";
    public int KeepDays { get; set; }

    public bool IsReady => Status == "ready";
    public bool IsSelected { get; set; } = true;
    public List<SubPathItem> SubItems { get; set; } = new();
}

public sealed class SubPathItem
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeHuman => CleanerEngine.HumanSize(SizeBytes);
    public int FilesCount { get; set; }
    public bool IsSelected { get; set; } = true;
}
