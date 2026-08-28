using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace Stagehand.AssetLibrary.Assets;

/// <summary>
/// Asset info for a resource in the game's files.
/// </summary>
public record class ResourceAssetInfo(string DisplayName, AssetType Type, string GamePath) : AssetInfo(DisplayName, Type, GamePath)
{
    public override void DrawProperties()
    {
        base.DrawProperties();

        ImGui.LabelText("Game Path", GamePath);
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(GamePath);
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(GamePath);
                ImGui.Separator();
                ImGui.TextDisabled("Click to copy");
            }
        }
    }
}
