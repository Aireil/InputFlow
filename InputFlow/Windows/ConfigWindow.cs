using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace InputFlow.Windows;

public class ConfigWindow : Window
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("InputFlow Config")
    {
        Flags = ImGuiWindowFlags.AlwaysAutoResize;

        configuration = plugin.Configuration;
    }

    public override void Draw()
    {
        var isNextBoundaryAtWordEnd = configuration.IsNextBoundaryAtWordEnd;
        if (ImGui.Checkbox("Navigate to the end of words when going right", ref isNextBoundaryAtWordEnd))
        {
            configuration.IsNextBoundaryAtWordEnd = isNextBoundaryAtWordEnd;
            configuration.Save();
        }

        Util.DrawHelp("off = ImGui/Notepad/Windows/..., on = Discord/VS Code/...");
    }
}
