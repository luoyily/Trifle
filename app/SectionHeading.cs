using Godot;

namespace Trifle.App;

public static class SectionHeading
{
    public static void Bind(Button button, Control fields, string title)
    {
        void Update(bool open)
        {
            ShowState(button, title, open);
            fields.Visible = open;
        }
        button.Toggled += Update;
        Update(button.ButtonPressed);
    }

    public static void ShowState(Button button, string title, bool open)
    {
        button.Text = title;
        button.Icon = button.GetThemeIcon(open ? "chevron_down" : "chevron_right", "Trifle");
        button.TooltipText = string.Format(AppLocale.T(open ? "折叠{0}" : "展开{0}"), AppLocale.T(title));
    }
}
