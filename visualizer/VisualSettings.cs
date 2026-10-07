using System;
using System.Collections.Generic;
using System.Globalization;

namespace Trifle.Visuals;

// Plain values shared by project files and visual presets; no scene nodes are serialized.
public sealed record VisualSettings
{
    public int EffectControlsVersion { get; init; } = 2;
    public int FirstPitch { get; init; } = 21;
    public int LastPitch { get; init; } = 108;
    public double LookAheadSeconds { get; init; } = 6;
    public NoteColorMode ColorMode { get; init; } = NoteColorMode.Channel;
    public Dictionary<int, string> ChannelColors { get; init; } = new();
    public Dictionary<int, string> TrackColors { get; init; } = new();
    public KeyboardAppearance Keyboard { get; init; } = new();
    public BackgroundSettings Background { get; init; } = new();
    public NoteAppearance Note { get; init; } = new();
    public GlowSettings Glow { get; init; } = new();
    public KeyboardLightSettings KeyboardLights { get; init; } = new();
    public ContactLineSettings ContactLine { get; init; } = new();
    public ParticleSettings Particles { get; init; } = new();

    public void Validate(int? trackCount = null)
    {
        if (EffectControlsVersion != 2) throw new ArgumentException("不支持的特效设置版本。");
        if (FirstPitch < 0 || LastPitch > 127 || FirstPitch > LastPitch)
            throw new ArgumentException("琴键范围需要满足 0 ≤ 最低音 ≤ 最高音 ≤ 127。");
        if (!double.IsFinite(LookAheadSeconds) || LookAheadSeconds < 1 || LookAheadSeconds > 12)
            throw new ArgumentException("同屏音符时长需要在 1–12 秒之间。");
        if (ColorMode is not NoteColorMode.Channel and not NoteColorMode.Track)
            throw new ArgumentException("未知配色模式。");
        if (ChannelColors == null || TrackColors == null) throw new ArgumentException("颜色表不能为空值。");
        ValidateColors(ChannelColors, 16, "Channel");
        ValidateColors(TrackColors, trackCount, "Track");
        if (Keyboard == null || Background == null) throw new ArgumentException("键盘和背景参数不能为空值。");
        Keyboard.Validate();
        Background.Validate();
        if (Note == null || Glow == null) throw new ArgumentException("音符和 Glow 参数不能为空值。");
        Note.Validate();
        Glow.Validate();
        if (KeyboardLights == null || ContactLine == null) throw new ArgumentException("键盘灯光和接触线参数不能为空值。");
        KeyboardLights.Validate();
        ContactLine.Validate();
        if (Particles == null) throw new ArgumentException("粒子参数不能为空值。");
        Particles.Validate();
    }

    private static void ValidateColors(Dictionary<int, string> colors, int? count, string label)
    {
        foreach (var (index, color) in colors)
        {
            if (index < 0 || (count.HasValue && index >= count.Value))
                throw new ArgumentException($"{label} 颜色索引 {index} 超出范围。");
            ValidateColor(color, $"{label} {index}");
        }
    }

    public static void ValidateColor(string color, string label)
    {
        if (color == null || color.Length != 8 ||
            !uint.TryParse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new ArgumentException($"{label} 的颜色需要为 8 位 RGBA 十六进制值。");
    }
}
