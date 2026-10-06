using System;
using System.IO;

namespace Trifle.Persistence;

public sealed record RecoveryData
{
    public string Kind { get; init; } = "trifle-recovery";
    public int Version { get; init; } = 1;
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ProjectPath { get; init; } = "";
    public ProjectData Project { get; init; } = new();

    public void Validate()
    {
        if (Kind != "trifle-recovery" || Version != 1 || Project == null || ProjectPath == null ||
            (ProjectPath.Length > 0 && !Path.IsPathFullyQualified(ProjectPath)))
            throw new ArgumentException("自动保存记录无效。");
        Project.Validate();
    }
}
