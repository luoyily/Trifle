using System;
using System.Text.Json.Serialization;

namespace Trifle.Export;

public sealed record EncodingSettings
{
    public string Encoder { get; init; } = "h264";
    public int Crf { get; init; } = 18;
    public string Preset { get; init; } = "fast";
    public int AudioBitrateKbps { get; init; } = 192;

    [JsonIgnore] public string CodecName => Encoder == "h265" ? "libx265" : "libx264";
    [JsonIgnore] public string DisplayName => Encoder == "h265" ? "H.265" : "H.264";

    public void Validate()
    {
        if (Encoder is not ("h264" or "h265"))
            throw new ArgumentException("视频编码器需要为 H.264 或 H.265。");
        if (Crf is < 0 or > 51) throw new ArgumentException("CRF 需要为 0–51 的整数。");
        if (Preset is not ("ultrafast" or "superfast" or "veryfast" or "faster" or "fast" or
            "medium" or "slow" or "slower" or "veryslow"))
            throw new ArgumentException("编码预设无效。");
        if (AudioBitrateKbps is not (128 or 192 or 256 or 320))
            throw new ArgumentException("AAC 音频码率需要为 128 / 192 / 256 / 320 kbps。");
    }
}
