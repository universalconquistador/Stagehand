using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Stagehand.Api;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Stagehand.ApiDemo;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration _configuration;
    private readonly IStagehandApi _stagehandApi;

    private bool _wasAvailable = false;
    private LocalStageDefinition[] _localStages = Array.Empty<LocalStageDefinition>();

    public ConfigWindow(Configuration configuration, IStagehandApi stagehandApi)
        : base("Stagehand IPC Demo")
    {
        _configuration = configuration;
        _stagehandApi = stagehandApi;

        _stagehandApi.LocalStageDefinitionsChanged += OnLocalStageDefinitionsChanged;

        Size = new(600, 300);

        Flags |= ImGuiWindowFlags.NoResize;
    }

    private void OnLocalStageDefinitionsChanged()
    {
        _localStages = _stagehandApi.GetLocalStageDefinitions();
        _localStages.Sort((a, b) => a.Name.CompareTo(b.Name));
    }

    public override void Draw()
    {
        var stagehandAvailability = _stagehandApi.CheckApiAvailability();
        var available = stagehandAvailability == StagehandApiAvailability.Available;

        if (!available)
        {
            _localStages = Array.Empty<LocalStageDefinition>();
        }
        else if (!_wasAvailable)
        {
            _localStages = _stagehandApi.GetLocalStageDefinitions();
            _localStages.Sort((a, b) => a.Name.CompareTo(b.Name));
        }

        using (ImRaii.PushColor(ImGuiCol.Text, available ? ImGuiColors.HealerGreen : ImGuiColors.DPSRed))
        using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
        {
            ImGui.TextUnformatted(available ? FontAwesomeIcon.CheckCircle.ToIconString() : FontAwesomeIcon.TimesCircle.ToIconString());
        }
        ImGui.SameLine();
        if (stagehandAvailability == StagehandApiAvailability.Available)
        {
            ImGui.TextUnformatted($"Stagehand available. (API revision: {_stagehandApi.GetPluginApiRevision()})");
        }
        else if (stagehandAvailability == StagehandApiAvailability.StagehandMissing)
        {
            ImGui.TextUnformatted("Stagehand disabled or not installed.");
        }
        else if (stagehandAvailability == StagehandApiAvailability.StagehandTooOld)
        {
            ImGui.TextUnformatted($"Stagehand too old. (API revision: {_stagehandApi.GetPluginApiRevision()}, expected: {StagehandApi.LibraryApiRevision})");
        }
        else if (stagehandAvailability == StagehandApiAvailability.StagehandTooNew)
        {
            ImGui.TextUnformatted($"Stagehand too new. (API revision: {_stagehandApi.GetPluginApiRevision()}, expected: {StagehandApi.LibraryApiRevision})");
        }
        else
        {
            // Has a new enum member been added? Fail in debug builds!
            Debug.Assert(false);
        }

        ImGui.Spacing();
        using (var tabBar = ImRaii.TabBar("###DemoConfigTabs"))
        {
            if (tabBar.Success)
            {
                using (var trailTab = ImRaii.TabItem("Trail"))
                {
                    if (trailTab.Success)
                    {
                        using (ImRaii.Disabled(!available))
                        {
                            DrawTrailTab();
                        }
                    }
                }

                using (var localStagesTab = ImRaii.TabItem("Local Stages"))
                {
                    if (localStagesTab.Success)
                    {
                        using (ImRaii.Disabled(!available))
                        {
                            DrawLocalStagesTab();
                        }
                    }
                }
            }
        }

        _wasAvailable = available;
    }

    private void DrawTrailTab()
    {
        ImGui.Spacing();

        bool enableTrail = _configuration.EnableTrail;
        if (ImGui.Checkbox("Enable Trail", ref enableTrail))
        {
            _configuration.EnableTrail = enableTrail;
            _configuration.Save();
        }
        ImGui.TextDisabled("Leaves a fading trail of VFX placements as the player moves.");
        ImGui.Spacing();

        using (ImRaii.Disabled(!enableTrail))
        {
            float intervalSeconds = _configuration.PlacementIntervalSeconds;
            if (ImGui.DragFloat("Placement Interval Seconds", ref intervalSeconds, vSpeed: 0.01f, vMin: 0.1f, vMax: 10.0f))
            {
                _configuration.PlacementIntervalSeconds = intervalSeconds;
                _configuration.Save();
            }

            float lifespanSeconds = _configuration.PlacementLifespanSeconds;
            if (ImGui.DragFloat("Placement Lifespan (Seconds)", ref lifespanSeconds, vSpeed: 0.01f, vMin: 0.1f, vMax: 10.0f))
            {
                _configuration.PlacementLifespanSeconds = lifespanSeconds;
                _configuration.Save();
            }

            string vfxGamePath = _configuration.PlacementVFXGamePath;
            if (ImGui.InputText("Placement VFX Path", ref vfxGamePath, flags: ImGuiInputTextFlags.AutoSelectAll))
            {
                _configuration.PlacementVFXGamePath = vfxGamePath;
                _configuration.Save();
            }

            float scale = _configuration.PlacementScale;
            if (ImGui.DragFloat("Placement Scale", ref scale, vSpeed: 0.01f, vMin: 0.1f, vMax: 10.0f))
            {
                _configuration.PlacementScale = scale;
                _configuration.Save();
            }
        }
    }

    private void DrawLocalStagesTab()
    {
        ImGui.Spacing();

        using (ImRaii.Table("###LocalStagesTable", 2, ImGuiTableFlags.BordersV | ImGuiTableFlags.BordersOuterH | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, ImGui.GetContentRegionAvail()))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("###Visible", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 1.0f);

            ImGui.TableHeadersRow();

            foreach (var localDefinition in _localStages)
            {
                // Visible icon
                ImGui.TableNextColumn();
                if (localDefinition.IsVisible)
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetStyle().FramePadding.Y);
                    using (ImRaii.PushFont(UiBuilder.IconFontFixedWidth))
                    using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen))
                    {
                        ImGui.TextUnformatted(FontAwesomeIcon.Eye.ToIconString());
                    }
                    if (ImGui.IsItemHovered())
                    {
                        using (ImRaii.Tooltip())
                        {
                            ImGui.TextUnformatted("Currently visible");
                        }
                    }
                }

                // Name & version
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(localDefinition.Name);
                bool nameHovered = ImGui.IsItemHovered();
                ImGui.SameLine();
                ImGui.TextDisabled($"v{localDefinition.VersionString}");
                if (nameHovered)
                {
                    using (ImRaii.Tooltip())
                    {
                        ImGui.TextUnformatted(localDefinition.Filename);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        _stagehandApi.LocalStageDefinitionsChanged -= OnLocalStageDefinitionsChanged;
    }
}
