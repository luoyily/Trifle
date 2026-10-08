using System;
using System.Text.Json.Serialization;
using Trifle.Export;
using Trifle.Visuals;
using Trifle.Audio;

namespace Trifle.Persistence;

public sealed record ProjectData
{
    [JsonRequired] public string Kind { get; init; } = "trifle-project";
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public string MidiPath { get; init; } = "";
    public bool MidiFromScore { get; init; }
    public string ScorePath { get; init; } = "";
    public VisualSettings Visual { get; init; } = new();
    public ExportPreferences Export { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public double TimeSeconds { get; init; }
    public bool SettingsVisible { get; init; } = true;
    public System.Collections.Generic.Dictionary<string, bool> ExpandedSections { get; init; } = new();
    public Trifle.App.PreviewSettings Preview { get; init; } = new();

    public void Validate()
    {
        if (Kind != "trifle-project" || Version != 1) throw new ArgumentException("不支持的项目类型或格式版本。");
        if (string.IsNullOrWhiteSpace(MidiPath)) throw new ArgumentException("项目缺少 MIDI 引用。");
        if (ScorePath == null) throw new ArgumentException("乐谱引用不能为空值。");
        if (ScorePath.Length > 0 && !MidiFromScore) throw new ArgumentException("当前乐谱同步需使用数据包内的 MIDI。");
        if (Visual == null || Export == null || Audio == null) throw new ArgumentException("项目参数不能为空值。");
        if (ExpandedSections == null) throw new ArgumentException("侧栏折叠状态不能为空值。");
        Visual.Validate();
        Export.Validate();
        Audio.Validate();
        if (Preview == null) throw new ArgumentException("预览参数不能为空值。");
        Preview.Validate();
        if (!double.IsFinite(TimeSeconds) || TimeSeconds < 0) throw new ArgumentException("播放位置无效。");
    }
}

public sealed record VisualPreset
{
    [JsonRequired] public string Kind { get; init; } = "trifle-preset";
    [JsonRequired] public int Version { get; init; } = 1;
    public VisualSettings Visual { get; init; } = new();

    public void Validate()
    {
        if (Kind != "trifle-preset" || Version != 1) throw new ArgumentException("不支持的视觉预设类型或格式版本。");
        if (Visual == null) throw new ArgumentException("预设参数不能为空值。");
        Visual.Validate();
    }
}
