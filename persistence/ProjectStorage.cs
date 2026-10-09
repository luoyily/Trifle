using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace Trifle.Persistence;

public static class ProjectStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ProjectData LoadProject(string path)
    {
        var data = Read<ProjectData>(path);
        data.Validate();
        return data;
    }

    public static VisualPreset LoadPreset(string path)
    {
        var data = Read<VisualPreset>(path);
        data.Validate();
        return data;
    }

    public static RecoveryData LoadRecovery(string path)
    {
        var data = Read<RecoveryData>(path); data.Validate(); return data;
    }

    public static void SaveRecovery(string path, RecoveryData data)
    {
        data.Validate();
        // Recovery contains absolute resource paths and never replaces the user's project.
        WriteAtomic(path, data);
    }

    public static void SaveProject(string path, ProjectData data)
    {
        data.Validate();
        WriteAtomic(path, data with
        {
            MidiPath = MakeReference(data.MidiPath, path),
            ScorePath = MakeReference(data.ScorePath, path),
            ExternalMidiPath = MakeReference(data.ExternalMidiPath, path),
            Audio = data.Audio with { Path = MakeReference(data.Audio.Path, path) },
            Visual = MakeVisualReferences(data.Visual, path),
            Export = data.Export with { OutputPath = MakeReference(data.Export.OutputPath, path) }
        });
    }

    public static void SavePreset(string path, VisualPreset data)
    {
        data.Validate();
        WriteAtomic(path, data with { Visual = MakeVisualReferences(data.Visual, path) });
    }

    private static Trifle.Visuals.VisualSettings MakeVisualReferences(Trifle.Visuals.VisualSettings visual, string path) =>
        visual with { Background = visual.Background with { ImagePath = MakeReference(visual.Background.ImagePath, path),
            Video = visual.Background.Video with { Path = MakeReference(visual.Background.Video.Path, path) } } };

    public static Trifle.Visuals.VisualSettings ResolveVisualReferences(Trifle.Visuals.VisualSettings visual, string path) =>
        visual with { Background = visual.Background with { ImagePath = ResolveReference(visual.Background.ImagePath, path),
            Video = visual.Background.Video with { Path = ResolveReference(visual.Background.Video.Path, path) } } };

    public static string MakeReference(string resourcePath, string documentPath)
    {
        if (resourcePath.Length == 0) return "";
        string absolute = Path.GetFullPath(resourcePath);
        string relative = Path.GetRelativePath(DocumentDirectory(documentPath), absolute).Replace('\\', '/');
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative)
            ? absolute.Replace('\\', '/') : relative;
    }

    public static string ResolveReference(string reference, string documentPath) => reference.Length == 0
        ? "" : Path.GetFullPath(reference, DocumentDirectory(documentPath));

    private static string DocumentDirectory(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("项目 / 预设文件需要使用绝对路径。");
        return Path.GetDirectoryName(Path.GetFullPath(path))!;
    }

    private static T Read<T>(string path)
    {
        _ = DocumentDirectory(path);
        var document = JsonNode.Parse(File.ReadAllText(path));
        if (document is JsonObject root)
        {
            UpgradeEffectControls(root["visual"] as JsonObject);
            if (root["project"] is JsonObject project) UpgradeEffectControls(project["visual"] as JsonObject);
        }
        return document.Deserialize<T>(JsonOptions)
            ?? throw new JsonException("文件没有有效的 JSON 对象。");
    }

    // Old projects used one particle master switch and one line/halo brightness.
    // Migrate once at the file boundary; saved projects, presets and recovery
    // then carry explicit independent states without overwriting their values.
    private static void UpgradeEffectControls(JsonObject visual)
    {
        if (visual == null) return;
        if (visual["background"] is JsonObject background && background["video"] is JsonObject video)
        {
            if (!background.ContainsKey("blur") && video["blur"] != null)
                background["blur"] = video["blur"].DeepClone();
            if (video["darkness"] != null && background["type"]?.GetValue<string>() == "Video")
                background["brightness"] = (background["brightness"]?.GetValue<double>() ?? 1) *
                    (1 - video["darkness"].GetValue<double>());
            video.Remove("blur");
            video.Remove("darkness");
        }
        UpgradeSidebarSettings(visual);
        if (visual.ContainsKey("effectControlsVersion")) return;
        if (visual["particles"] is JsonObject particles)
        {
            particles["curves"] = (particles["enabled"]?.GetValue<bool>() ?? false)
                && (particles["curves"]?.GetValue<bool>() ?? true);
            particles["curveEmission"] = particles["emission"]?.DeepClone() ?? JsonValue.Create(1.0);
            particles["curveGlow"] = particles["glow"]?.DeepClone() ?? JsonValue.Create(0.85);
        }
        if (visual["lights"] is JsonObject lights)
        {
            lights["haloEmission"] = lights["lineEmission"]?.DeepClone() ?? JsonValue.Create(0.4);
            lights["lineContactBoost"] = 0.8 + (lights["hitEmission"]?.GetValue<double>() ?? 0) * 0.5;
            // This effect previously depended on keyboard emission as well.
            lights["nearEnabled"] = (lights["keyboardEmission"]?.GetValue<double>() ?? 0) > 0;
        }
        visual["effectControlsVersion"] = 1;
        UpgradeSidebarSettings(visual);
    }
    private static void UpgradeSidebarSettings(JsonObject visual)
    {
        if (visual["background"] is JsonObject background && !background.ContainsKey("type"))
        {
            bool image = !string.IsNullOrEmpty(background["imagePath"]?.GetValue<string>());
            string gradient = background["gradient"]?.GetValue<string>();
            background["type"] = image ? "Image" : gradient is "Vertical" or "Horizontal" ? "Gradient" : "Solid";
        }
        // Let the older brightness/switch migration run before splitting its source record.
        if (visual["effectControlsVersion"]?.GetValue<int>() != 1) return;
        if (visual["lights"] is JsonObject lights)
        {
            var keyboard = new JsonObject();
            var line = new JsonObject();
            foreach (var field in lights)
            {
                if (KeyboardLightFields.Contains(field.Key)) keyboard[field.Key] = field.Value?.DeepClone();
                else if (ContactLineFields.Contains(field.Key)) line[field.Key] = field.Value?.DeepClone();
            }
            if (!visual.ContainsKey("keyboardLights")) visual["keyboardLights"] = keyboard;
            if (!visual.ContainsKey("contactLine")) visual["contactLine"] = line;
            visual.Remove("lights");
        }
        visual["effectControlsVersion"] = 2;
    }

    private static readonly System.Collections.Generic.HashSet<string> KeyboardLightFields = new()
    {
        "keyboardEnabled", "hitEnabled", "nearEnabled", "keyLightEnabled", "keyboardEmission", "hitEmission", "hitDecay", "nearStrength", "nearDistance", "keyLightStrength", "keyLightRadius"
    };
    private static readonly System.Collections.Generic.HashSet<string> ContactLineFields = new()
    {
        "lineEnabled", "haloEnabled", "haloEmission", "lineColor", "haloColor", "haloFollowsLine", "tintWithNotes", "lineContactBoost", "lineEmission", "lineWidth", "lineWave", "lineCoreWidth"
    };

    private static void WriteAtomic<T>(string path, T data)
    {
        string directory = DocumentDirectory(path);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("保存目录不存在。");
        string temporary = Path.Combine(directory, $".trifle_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
