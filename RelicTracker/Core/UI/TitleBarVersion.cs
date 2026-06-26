using System.Numerics;
using System.Reflection;
namespace RelicTracker;

internal static class TitleBarVersion
{
    public static void DrawFromContext(int customTitleBarButtonCount, bool showAdditionalOptionsButton)
    {
        Vector2 windowPos = ImGui.GetWindowPos();
        Vector2 windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
        {
            return;
        }

        DrawAt(windowPos, windowSize, customTitleBarButtonCount, showAdditionalOptionsButton);
    }

    private static void DrawAt(
        Vector2 windowPos,
        Vector2 windowSize,
        int customTitleBarButtonCount,
        bool showAdditionalOptionsButton)
    {
        string text = GetVersionLabel();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Vector2 textSize = ImGui.CalcTextSize(text);
        ImGuiStylePtr style = ImGui.GetStyle();
        float buttonSize = ImGui.GetFontSize();
        float spacing = style.ItemInnerSpacing.X;

        int numNativeButtons = 1;
        if (style.WindowMenuButtonPosition == ImGuiDir.Right)
        {
            numNativeButtons++;
        }

        int numCustomButtons = customTitleBarButtonCount + (showAdditionalOptionsButton ? 1 : 0);
        float padRight = (numNativeButtons + numCustomButtons) * (buttonSize + spacing);

        float titleBarMaxX = windowPos.X + windowSize.X;
        Vector2 position = new(
            titleBarMaxX - padRight - textSize.X,
            windowPos.Y + style.FramePadding.Y);

        uint color = ImGui.ColorConvertFloat4ToU32(style.Colors[(int)ImGuiCol.TextDisabled]);

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 clipMax = windowPos + windowSize;
        drawList.PushClipRect(windowPos, clipMax, false);
        drawList.AddText(
            ImGui.GetFont(),
            ImGui.GetFontSize(),
            position,
            color,
            text);
        drawList.PopClipRect();
    }

    private static string GetVersionLabel()
    {
        Version? manifestVersion = Svc.PluginInterface.Manifest.AssemblyVersion;
        if (manifestVersion != null)
        {
            return "v" + FormatVersion(manifestVersion);
        }

        Version? assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion != null ? "v" + FormatVersion(assemblyVersion) : "v?.?.?.?";
    }

    private static string FormatVersion(Version version) =>
        version.Revision >= 0 ? version.ToString(4) : version.ToString(3);
}
