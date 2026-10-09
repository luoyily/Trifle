using Godot;
using System;

namespace Trifle.App;

// Autoload applied before the main scene builds: Auto / 简体中文 / English. UI strings are authored
// in Chinese, so Chinese needs no translation resource and missing entries fall back to the source
// text. Plain Text/TooltipText assignments stay raw and re-translate live; composed strings use
// T() at build time and pick up a language change when their owner regenerates them.
public partial class AppLocale : Node
{
    private static AppLocale _instance;
    private string _osLocale = "";

    // Fired after every locale change. The engine's NOTIFICATION_TRANSLATION_CHANGED proves
    // unreliable for runtime switches outside Control auto-translation, so composed strings
    // and OptionButton items rebuild through this event instead.
    public static event Action LanguageChanged;

    // Binds a Window title (Window titles are not auto-translated) and keeps it current.
    public static void BindTitle(Window window, string msgid)
    {
        window.Title = T(msgid);
        LanguageChanged += () => window.Title = T(msgid);
    }

    // Scene nodes can be constructed before autoload _Ready runs (e.g. field initializers calling
    // T()), so the instance and the OS locale must be captured in the constructor.
    public AppLocale()
    {
        _instance = this;
        _osLocale = TranslationServer.GetLocale();
    }

    public override void _Ready() => Apply(Load());

    // Translation helper for compositions, OptionButton items and Window titles. Raw Control
    // property assignments do not need it — the engine translates those by itself.
    public static string T(string message) => _instance.Tr(message);

    public static void Attach(OptionButton menu)
    {
        menu.AddItem("Auto");
        menu.AddItem("简体中文");
        menu.AddItem("English");
        menu.Select(Load() switch { "zh" => 1, "en" => 2, _ => 0 });
        menu.ItemSelected += index =>
        {
            string value = index switch { 1 => "zh", 2 => "en", _ => "auto" };
            _instance.Apply(value);
            Save(value);
        };
    }

    private void Apply(string value)
    {
        TranslationServer.SetLocale(value switch
        {
            "zh" => "zh",
            "en" => "en",
            _ => _osLocale.Length > 0 ? _osLocale : "zh",
        });
        LanguageChanged?.Invoke();
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
