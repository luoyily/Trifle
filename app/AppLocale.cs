using Godot;

namespace Trifle.App;

// Header language switcher: Auto / 简体中文 / English. UI strings are authored in Chinese, so
// Chinese needs no translation resource and missing entries fall back to the source text.
// The choice persists in user://ui.cfg and re-translates live through TranslationServer.
public static class AppLocale
{
    private static string _osLocale = "";

    public static void ApplySaved()
    {
        _osLocale = TranslationServer.GetLocale();
        Apply(Load());
    }

    public static void Attach(OptionButton menu)
    {
        menu.AddItem("Auto");
        menu.AddItem("简体中文");
        menu.AddItem("English");
        menu.Select(Load() switch { "zh" => 1, "en" => 2, _ => 0 });
        menu.ItemSelected += index =>
        {
            string value = index switch { 1 => "zh", 2 => "en", _ => "auto" };
            Apply(value);
            Save(value);
        };
    }

    private static void Apply(string value)
    {
        TranslationServer.SetLocale(value switch
        {
            "zh" => "zh",
            "en" => "en",
            _ => _osLocale.Length > 0 ? _osLocale : "zh",
        });
    }

    private static string Load()
    {
        var config = new ConfigFile();
        return config.Load("user://ui.cfg") == Error.Ok
            ? config.GetValue("ui", "language", "auto").AsString()
            : "auto";
    }

    private static void Save(string value)
    {
        var config = new ConfigFile();
        config.SetValue("ui", "language", value);
        config.Save("user://ui.cfg");
    }
}
