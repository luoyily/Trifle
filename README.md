<div align="center">

  简体中文 | [English](README.en.md)

  <img src="readme_assets/icon.png" width="128" alt="Trifle 图标">

  # Trifle

  **Nothing special. Until it is.**

  开源 MIDI 钢琴可视化工具 —— 乐谱同步 · 特效渲染 · 视频导出

  <img src="readme_assets/screenshot.jpg" width="720" alt="Trifle 截图">

  **[▶ 示例视频（哔哩哔哩）](https://www.bilibili.com/video/BV1Aqps6eEjr/)**

</div>

## 功能特点

- **钢琴可视化**：音符与键盘联动命中，接触线、键盘灯光、粒子、辉光等效果均可独立调节
- **乐谱同步**：配合 MuseScore 4 显示乐谱，支持光标线同步与逐音符打字机动画，并兼容与记谱不完全一致的 MIDI
- **通用 MIDI**：直接打开 `.mid` 即可播放与可视化
- **多种背景**：支持图片、纯色、渐变、视频
- **音频挂载**：外接音频可设偏移，导出时编码为 AAC 音轨
- **视频导出**：1080p / 1440p / 4K · 24 / 30 / 60 FPS · H.264 / H.265，逐帧渲染
- **工程管理**：可视化设置保存为工程文件，支持自动恢复
- **Midi输出**: 配合Win11 Midi环回，将当前播放的Midi发送给DAW或软音源以实时播放声音

## 环境要求

- Windows 10 / 11（64 位）

## 可选依赖

- **FFmpeg**（视频导出、背景视频）：从[官网](https://ffmpeg.org/download.html#build-windows)下载**完整版**（需包含 `ffprobe.exe`），添加至 PATH 环境变量，或在导出窗口中填写 `ffmpeg.exe` 完整路径；背景视频要求 `ffprobe.exe` 与 ffmpeg 位于同一目录
- **MuseScore 4**（乐谱同步）：程序会自动探测安装位置，也可在乐谱设置面板手动指定
- **Windows Midi**（Win11 Midi环回）：从[官网](https://github.com/microsoft/MIDI)下载Windows.MIDI.Services.Tools 与 Windows.MIDI.Services.Transports并安装，打开MIDI Loop Back Setup，“+ New Loopback”选项默认即可，软件内启用Midi输出并选择“Default App Loopback (A)”，DAW或独立软音源监听“Default App Loopback (B)”，此时软件播放Midi，DAW将收到对应Midi事件。注：此功能暂时仅适用于预览或配合屏幕录制，内置视频导出不会包含此功能产生的声音

## 功能覆盖与已知边界

- **乐谱覆盖**：乐谱同步基于 MuseScore 4 的乐谱导出数据，理论上支持大部分常规乐谱；最终效果取决于 MuseScore 能否正确打开并导出该谱面。复杂声部、特殊排版或非标准记谱建议先用实际谱面验证。
- **光标线与打字机**：两者共用同一份播放时间轴。光标线支持跨行、跨页移动和反复回跳（包括跨行反复）；打字机按演奏 onset 逐音符显示，已写出的内容不会因为反复回跳而被擦除。
- **反复记号**：支持由开始反复记号与结束反复记号构成的基本段落反复。乐谱内 MIDI 会按 MuseScore 展开后的播放顺序播放；使用外部 MIDI 时，需要提供同样展开反复的 MIDI，软件会核对展开后的小节数，数量不足会给出提示。其他复杂反复（多结局、D.S./Coda 等）建议先展开后再使用。
- **打字机边界**：优先停在音符之间的空隙；如果最优边界仍会切到音符，会连带显示该音符，避免显示切了一半的音符。跨多个显示单元的宽记号（连线、延音线等）仍可能在边界处被裁切。
- **行裁剪与过滤**：系统行裁剪、相邻行归属和装饰记号过滤依赖几何与语义启发式判断；常见谱面可以正常使用，但不保证 100% 精确，复杂排版或特殊记号建议用实际谱面检查。

## 从源码构建

1. 安装 [Godot 4.7.2 .NET 版](https://godotengine.org/download)
2. 用 Godot 打开工程目录，或执行 `dotnet build Trifle.sln`

## 第三方声明

随包分发的第三方组件（Godot Engine、DryWetMIDI、.NET Runtime）许可见 [licenses/THIRD-PARTY-LICENSES.txt](licenses/THIRD-PARTY-LICENSES.txt)。

## 许可证

本项目以 [GNU AGPL-3.0](LICENSE) 协议开源。
