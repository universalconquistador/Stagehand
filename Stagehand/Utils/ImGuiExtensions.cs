using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Stagehand.Utils;

internal static class ImGuiExtensions
{
    public static bool FilteredCombo<TValues>(ImU8String label, ref int currentItem, ref string currentFilter, IReadOnlyList<TValues> items, Func<TValues, string> toString, string filterHint, Func<TValues, bool>? filterPredicate = null)
    {
        bool selected = false;
        using (var combo = ImRaii.Combo(label, (currentItem >= 0 && currentItem < items.Count) ? toString(items[currentItem]) : string.Empty))
        {
            if (combo)
            {
                var comboWidth = ImGui.GetItemRectSize().X;
                var icon = FontAwesomeIcon.Times;
                var clearButtonWidth = ImGuiComponents.GetIconButtonWithTextWidth(icon, string.Empty);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - clearButtonWidth - ImGui.GetStyle().ItemSpacing.X);
                ImGui.InputTextWithHint("##SearchFilter", filterHint, ref currentFilter, 2048);
                ImGui.SetItemDefaultFocus();
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(icon))
                {
                    currentFilter = string.Empty;
                }
                ImGui.Separator();

                using (var child = ImRaii.Child("##ComboItems", new Vector2(ImGui.GetContentRegionAvail().X, 110.0f), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
                {
                    var hitCount = 0;
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];

                        if (filterPredicate != null ? filterPredicate(item) : (!string.IsNullOrEmpty(currentFilter) ? toString(item).Contains(currentFilter, StringComparison.CurrentCultureIgnoreCase) : true))
                        {
                            hitCount += 1;

                            if (ImGui.Selectable(toString(item), i == currentItem))
                            {
                                currentItem = i;
                                selected = true;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }

                    if (hitCount == 0)
                    {
                        ImGui.TextDisabled(items.Count > 0 ? "(No Results)" : "(None)");
                    }

                }
            }
        }

        return selected;
    }
}
