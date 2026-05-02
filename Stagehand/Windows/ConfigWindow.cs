using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Stagehand.Windows;

public interface IConfigWindow : IHostedService
{
    void Show();
}

public class ConfigWindow : Window, IConfigWindow, IDisposable
{
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly WindowSystem _windowSystem;
    private readonly StagehandConfiguration _configuration;

    public ConfigWindow(IDalamudPluginInterface dalamudPluginInterface, WindowSystem windowSystem, StagehandConfiguration configuration) : base("Stagehand Configuration")
    {
        SizeCondition = ImGuiCond.Always;

        _dalamudPluginInterface = dalamudPluginInterface;
        _windowSystem = windowSystem;
        _configuration = configuration;
    }

    void IConfigWindow.Show()
    {
        IsOpen = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var definitionLibraryPath = _configuration.DefinitionLibraryPath;
        if (ImGui.InputText("Definition Library Folder", ref definitionLibraryPath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
        {
            // TODO: Might want to prompt whether to move the player's local definitions
            // TODO: Migrate auto load conditions
            _configuration.DefinitionLibraryPath = definitionLibraryPath;
            _configuration.Save();
        }
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (ImGuiComponents.IconButton("###OpenLibraryFolder", FontAwesomeIcon.ExternalLinkAlt, new Vector2(ImGui.GetFrameHeight())))
        {
            Process.Start("explorer", $"/root, {_configuration.DefinitionLibraryPath}");
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(_configuration.DefinitionLibraryPath);
                ImGui.Separator();
                ImGui.TextDisabled("Click to open");
            }
        }

        var autosavePath = _configuration.AutosavePath;
        if (ImGui.InputTextWithHint("Autosave Folder", "(leave blank for default)", ref autosavePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
        {
            _configuration.AutosavePath = autosavePath;
            _configuration.Save();
        }
        ImGui.SameLine(0.0f, 0.0f);
        ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.GetFrameHeight());
        if (ImGuiComponents.IconButton("###OpenAutosaveFolder", FontAwesomeIcon.ExternalLinkAlt, new Vector2(ImGui.GetFrameHeight())))
        {
            Process.Start("explorer", $"/root, {_configuration.FinalAutosavePath}");
        }
        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(_configuration.FinalAutosavePath);
                ImGui.Separator();
                ImGui.TextDisabled("Click to open");
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _windowSystem.AddWindow(this);

        _dalamudPluginInterface.UiBuilder.OpenConfigUi += Toggle;

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _dalamudPluginInterface.UiBuilder.OpenConfigUi -= Toggle;

        _windowSystem.RemoveWindow(this);

        return Task.CompletedTask;
    }
}
