using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using Microsoft.Extensions.DependencyInjection;
using Stagehand.Definitions.Objects;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using ImGuiExtensions = Stagehand.Utils.ImGuiExtensions;

namespace Stagehand.Editor.DefinitionEditors.Objects;

internal class WeaponDefinitionEditor : IObjectDefinitionEditor<WeaponDefinition>
{
    public static readonly DefinitionTypeInfo StaticTypeInfo = new DefinitionTypeInfo("Weapon", "A weapon model.", FontAwesomeIcon.Gun);

    public override DefinitionTypeInfo TypeInfo => StaticTypeInfo;

    private readonly ITextureProvider _textureProvider;
    private readonly IDataManager _dataManager;

    private record class WeaponOption(string DisplayName, int IconId, int ModelSetId, int SecondaryId, int Variant, int PrimaryDye, int SecondaryDye);
    private static WeaponOption[] _allWeaponOptions;

    public int ModelSetId
    {
        get => Definition.ModelSetId;
        set => SetPropertyValue(value => Definition.ModelSetId = value, value);
    }

    public int SecondaryId
    {
        get => Definition.SecondaryId;
        set => SetPropertyValue(value => Definition.SecondaryId = value, value);
    }

    public int Variant
    {
        get => Definition.Variant;
        set => SetPropertyValue(value => Definition.Variant = value, value);
    }

    public int PrimaryDye
    {
        get => Definition.PrimaryDye;
        set => SetPropertyValue(value => Definition.PrimaryDye = value, value);
    }

    public int SecondaryDye
    {
        get => Definition.SecondaryDye;
        set => SetPropertyValue(value => Definition.SecondaryDye = value, value);
    }

    public int AnimationVariant
    {
        get => Definition.AnimationVariant;
        set => SetPropertyValue(value => Definition.AnimationVariant = value, value);
    }

    private string _weaponFilter = "";
    private string _primaryDyeFilter = "";
    private string _secondaryDyeFilter = "";

    public WeaponDefinitionEditor(IServiceProvider serviceProvider, WeaponDefinition definition, string key, StageDefinitionEditor stage) : base(serviceProvider, definition, key, stage)
    {
        _textureProvider = serviceProvider.GetRequiredService<ITextureProvider>();
        _dataManager = serviceProvider.GetRequiredService<IDataManager>();
        List<WeaponOption> weaponOptions = new List<WeaponOption>();

        if (_allWeaponOptions == null)
        {
            var sheet = serviceProvider.GetRequiredService<IDataManager>().GetExcelSheet<Item>();
            foreach (var item in sheet)
            {
                if ((item.FilterGroup == 1 || item.FilterGroup == 2) && !string.IsNullOrEmpty(item.Name.ToString()))
                {
                    if (item.ModelMain != 0)
                    {
                        WeaponModelId modelMainId = new() { Value = item.ModelMain };
                        weaponOptions.Add(new WeaponOption(item.Name.ToString(), item.Icon, modelMainId.Id, modelMainId.Type, modelMainId.Variant, modelMainId.Stain0, modelMainId.Stain1));
                    }

                    if (item.ModelSub != 0)
                    {
                        var modelSubId = new WeaponModelId() { Value = item.ModelSub };
                        weaponOptions.Add(new WeaponOption($"{item.Name} (Off Hand)", item.Icon, modelSubId.Id, modelSubId.Type, modelSubId.Variant, modelSubId.Stain0, modelSubId.Stain1));
                    }
                }
            }

            _allWeaponOptions = weaponOptions.OrderBy(weaponOption => weaponOption.DisplayName).ToArray();
        }
    }

    public override void DrawProperties()
    {
        base.DrawProperties();

        int selectedWeaponOption = -1;
        for (int i = 0; i < _allWeaponOptions.Length; i++)
        {
            if (_allWeaponOptions[i].ModelSetId == ModelSetId
                && _allWeaponOptions[i].SecondaryId == SecondaryId
                && _allWeaponOptions[i].Variant == Variant)
            {
                selectedWeaponOption = i;
                break;
            }
        }

        var iconSize = 48.0f * ImGuiHelpers.GlobalScale;

        ImGui.Image(_textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(selectedWeaponOption != -1 ? (uint)_allWeaponOptions[selectedWeaponOption].IconId : 0)).GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize));
        using (ImRaii.PushIndent(iconSize, false))
        {
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            using (ImRaii.Group())
            {
                using (var weaponCombo = ImRaii.Combo("Weapon", selectedWeaponOption != -1 ? _allWeaponOptions[selectedWeaponOption].DisplayName : "(Unspecified)"))
                {
                    if (weaponCombo.Success)
                    {
                        ImGuiExtensions.FilterBox("Filter", ref _weaponFilter);

                        for (int i = 0; i < _allWeaponOptions.Length; i++)
                        {
                            if (_allWeaponOptions[i].DisplayName.Contains(_weaponFilter, StringComparison.CurrentCultureIgnoreCase))
                            {
                                if (ImGui.Selectable($"###Weapon{i}", i == selectedWeaponOption, ImGuiSelectableFlags.AllowItemOverlap, new Vector2(0.0f, iconSize + ImGui.GetStyle().FramePadding.Y * 2.0f)))
                                {
                                    ModelSetId = _allWeaponOptions[i].ModelSetId;
                                    SecondaryId = _allWeaponOptions[i].SecondaryId;
                                    Variant = _allWeaponOptions[i].Variant;
                                    PrimaryDye = _allWeaponOptions[i].PrimaryDye;
                                    SecondaryDye = _allWeaponOptions[i].SecondaryDye;
                                }
                                ImGui.SameLine(0.0f);
                                ImGui.AlignTextToFramePadding();
                                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.Y);
                                ImGui.Image(_textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup((uint)_allWeaponOptions[i].IconId)).GetWrapOrEmpty().Handle, new Vector2(iconSize, iconSize));
                                ImGui.SameLine();
                                using (ImRaii.Group())
                                {
                                    ImGui.TextUnformatted(_allWeaponOptions[i].DisplayName);
                                    ImGui.TextDisabled($"({_allWeaponOptions[i].ModelSetId}-{_allWeaponOptions[i].SecondaryId}-{_allWeaponOptions[i].Variant})");
                                }
                            }
                        }
                    }
                }

                float propertiesColumnWidth = (ImGui.GetContentRegionMax().X - ImGui.GetWindowContentRegionMin().X) * 0.333f;
                float dyeBoxSize = (ImGui.GetContentRegionAvail().X - propertiesColumnWidth - ImGui.GetStyle().ItemInnerSpacing.X) / 2.0f;
                ImGui.SetNextItemWidth(dyeBoxSize);
                var primaryDye = PrimaryDye;
                if (ImGuiExtensions.DyeCombo("###DyePrimary", ref primaryDye, ref _primaryDyeFilter, _dataManager))
                {
                    PrimaryDye = primaryDye;
                }

                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                var secondaryDye = SecondaryDye;
                if (ImGuiExtensions.DyeCombo("###DyeSecondary", ref secondaryDye, ref _secondaryDyeFilter, _dataManager))
                {
                    SecondaryDye = secondaryDye;
                }

                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
                ImGui.Text("Primary & Secondary Dye");
            }
        }

#if false

        int modelSetId = ModelSetId;
        if (ImGui.InputInt("Model Set ID", ref modelSetId))
        {
            ModelSetId = modelSetId;
        }

        int secondaryId = SecondaryId;
        if (ImGui.InputInt("Secondary ID", ref secondaryId))
        {
            SecondaryId = secondaryId;
        }

        int variant = Variant;
        if (ImGui.InputInt("Variant", ref variant))
        {
            Variant = variant;
        }

        int primaryDye = PrimaryDye;
        if (ImGui.InputInt("Primary Dye", ref primaryDye))
        {
            PrimaryDye = primaryDye;
        }

        int secondaryDye = SecondaryDye;
        if (ImGui.InputInt("Secondary Dye", ref secondaryDye))
        {
            SecondaryDye = secondaryDye;
        }

        int animationVariant = AnimationVariant;
        if (ImGui.InputInt("Animation Variant", ref animationVariant))
        {
            AnimationVariant = animationVariant;
        }

#endif
    }
}
