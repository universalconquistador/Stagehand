using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using static Dalamud.Interface.Utility.Raii.ImRaii;

namespace Stagehand.Utils;

internal static class ImGuiExtensions
{
    public static void FilterBox(ImU8String hint, ref string currentFilter)
    {
        var icon = FontAwesomeIcon.Times;
        var clearButtonWidth = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - clearButtonWidth - ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.InputTextWithHint("##SearchFilter", hint, ref currentFilter, 2048);
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
        if (ImGuiComponents.IconButton(icon, new Vector2(clearButtonWidth, clearButtonWidth)))
        {
            currentFilter = string.Empty;
        }
    }

    public static bool FilteredCombo<TValues>(ImU8String label, ref int currentItem, ref string currentFilter, IReadOnlyList<TValues> items, Func<TValues, string> toString, string filterHint, Func<TValues, bool>? filterPredicate = null)
    {
        bool selected = false;
        using (var combo = ImRaii.Combo(label, (currentItem >= 0 && currentItem < items.Count) ? toString(items[currentItem]) : string.Empty))
        {
            if (combo)
            {
                if (ImGui.IsItemActivated())
                {
                    ImGui.SetKeyboardFocusHere();
                }
                FilterBox(filterHint, ref currentFilter);
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

    private record class DyeOption(string DisplayName, Vector4 Color);
    private static DyeOption[]? _allDyeOptions;
    public static bool DyeCombo(ImU8String label, ref int dyeIndex, ref string currentFilter, IDataManager dataManager)
    {
        // Lazy initialize the available dye options
        if (_allDyeOptions == null)
        {
            var sheet = dataManager.GetExcelSheet<Stain>();
            var dyeOptions = new List<DyeOption>();
            foreach (var item in sheet)
            {
                if (!string.IsNullOrEmpty(item.Name.ToString()))
                {
                    var vectorColor = ImGui.ColorConvertU32ToFloat4(item.Color);
                    dyeOptions.Add(new DyeOption(item.Name.ToString(), new Vector4(vectorColor.Z, vectorColor.Y, vectorColor.X, item.RowId > 0 ? 1.0f : 0.0f)));
                }
            }
            _allDyeOptions = dyeOptions.ToArray();
        }

        var result = false;
        var selectedSecondaryDye = (dyeIndex >= 0 && dyeIndex < _allDyeOptions.Length) ? _allDyeOptions[dyeIndex] : null;
        IEndObject? secondaryDyeCombo;
        using (ImRaii.PushColor(ImGuiCol.FrameBg, ImGui.ColorConvertFloat4ToU32(selectedSecondaryDye?.Color ?? Vector4.Zero), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(selectedSecondaryDye?.Color ?? Vector4.Zero), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.FrameBgActive, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(selectedSecondaryDye?.Color ?? Vector4.Zero, Vector4.UnitW, 0.2f)), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.ButtonActive, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(selectedSecondaryDye?.Color ?? Vector4.Zero, Vector4.UnitW, 0.2f)), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(selectedSecondaryDye?.Color ?? Vector4.Zero, Vector4.One, 0.2f)), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(selectedSecondaryDye?.Color ?? Vector4.Zero, Vector4.One, 0.2f)), selectedSecondaryDye != null))
        using (ImRaii.PushColor(ImGuiCol.Text, Vector4.One - (ImGui.GetStyle().Colors[(int)ImGuiCol.Text] with { W = 0.0f }), (selectedSecondaryDye?.Color.X + selectedSecondaryDye?.Color.Y + selectedSecondaryDye?.Color.Z) / 3.0f > 0.5f))
        {
            secondaryDyeCombo = ImRaii.Combo(label, selectedSecondaryDye?.DisplayName ?? "(Unspecified)", ImGuiComboFlags.NoArrowButton);
        }

        using (secondaryDyeCombo)
        {
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            {
                dyeIndex = 0;
                result = true;
            }

            if (secondaryDyeCombo.Success)
            {
                ImGuiExtensions.FilterBox("Filter", ref currentFilter);

                for (int i = 0; i < _allDyeOptions.Length; i++)
                {
                    if (_allDyeOptions[i].DisplayName.Contains(currentFilter, StringComparison.CurrentCultureIgnoreCase))
                    {
                        var color = _allDyeOptions[i].Color;
                        using (ImRaii.PushColor(ImGuiCol.Button, ImGui.ColorConvertFloat4ToU32(color)))
                        using (ImRaii.PushColor(ImGuiCol.ButtonActive, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(color, Vector4.UnitW, 0.2f))))
                        using (ImRaii.PushColor(ImGuiCol.ButtonHovered, ImGui.ColorConvertFloat4ToU32(Vector4.Lerp(color, Vector4.One, 0.2f))))
                        using (ImRaii.PushColor(ImGuiCol.Text, (color.X + color.Y + color.Z) / 3.0f > 0.5f ? Vector4.UnitW : Vector4.One))
                        {
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            if (ImGui.Button(_allDyeOptions[i].DisplayName, new Vector2(-1.0f, 0.0f)))
                            {
                                dyeIndex = i;
                                result = true;
                                ImGui.CloseCurrentPopup();
                            }

                            if (i == dyeIndex)
                            {
                                ImGui.SameLine(ImGui.GetStyle().WindowPadding.X - ImGuiHelpers.GlobalScale);
                                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGuiHelpers.GlobalScale);
                                using (ImRaii.PushFont(UiBuilder.IconFont))
                                {
                                    ImGui.TextUnformatted(FontAwesomeIcon.CheckCircle.ToIconString());
                                }
                            }
                        }
                    }
                }
            }
        }

        return result;
    }
}
