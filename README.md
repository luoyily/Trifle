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

乐谱同步基于 MuseScore 4 导出数据，覆盖大多数常规乐谱；光标线与打字机按演奏 onset 工作并支持基本段落反复，打字机在反复回跳时会从回跳点重新书写；复杂反复建议先展开，特殊排版和自动行归属依赖启发式处理，实际效果请用谱面验证。

## 赞助此项目
爱发电：https://afdian.com/a/luo_yi

## 从源码构建

1. 安装 [Godot 4.7.2 .NET 版](https://godotengine.org/download)
2. 用 Godot 打开工程目录，或执行 `dotnet build Trifle.sln`

## 第三方声明

随包分发的第三方组件（Godot Engine、DryWetMIDI、.NET Runtime）许可见 [licenses/THIRD-PARTY-LICENSES.txt](licenses/THIRD-PARTY-LICENSES.txt)。

## 许可证

本项目以 [GNU AGPL-3.0](LICENSE) 协议开源。
