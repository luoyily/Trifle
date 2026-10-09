<div align="center">

  [简体中文](README.md) | English

  <img src="readme_assets/icon.png" width="128" alt="Trifle icon">

  # Trifle

  **Nothing special. Until it is.**

  An open-source MIDI piano visualizer — score sync · effects rendering · video export

  <img src="readme_assets/screenshot.jpg" width="720" alt="Trifle screenshot">

</div>

## Features

- **Piano visualization**: notes strike the keyboard in sync; the contact line, keyboard lights, particles, glow and more are all independently adjustable
- **Score sync**: display sheet music with a play-along cursor via MuseScore 4 — the MIDI doesn't have to match the notation exactly
- **Universal MIDI**: open any `.mid` file directly for playback and visualization
- **Multiple backgrounds**: image, solid color, gradient and video
- **Audio support**: attach external audio with an offset, exported as an AAC track
- **Video export**: 1080p / 1440p / 4K · 24 / 30 / 60 FPS · H.264 / H.265, frame-by-frame rendering
- **Project management**: save all visual settings as project files, with automatic recovery

## Requirements

- Windows 10 / 11 (64-bit)

## Optional dependencies

- **FFmpeg** (video export, background videos): download it from the [official site](https://ffmpeg.org/download.html#build-windows), add it to `PATH`, or place `ffmpeg.exe` next to the program
- **MuseScore 4** (score sync): detected automatically, or set manually in the score settings panel

## Build from source

1. Install [Godot 4.7.2 (.NET)](https://godotengine.org/download)
2. Open the project folder in Godot, or run `dotnet build Trifle.sln`

## Third-party notices

Licenses for the bundled third-party components (Godot Engine, DryWetMIDI, .NET Runtime) can be found in [licenses/THIRD-PARTY-LICENSES.txt](licenses/THIRD-PARTY-LICENSES.txt).
