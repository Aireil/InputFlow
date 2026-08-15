using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;

namespace InputFlow;

public class Util
{
    public static void DrawHelp(string helpMessage)
    {
        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.DalamudGrey, "(?)");

        SetHoverTooltip(helpMessage);
    }

    public static void SetHoverTooltip(string tooltip, bool allowWhenDisabled = false)
    {
        if (ImGui.IsItemHovered(allowWhenDisabled ? ImGuiHoveredFlags.AllowWhenDisabled : ImGuiHoveredFlags.None))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(tooltip);
            ImGui.EndTooltip();
        }
    }
}
