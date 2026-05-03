using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
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

    private readonly FileDialogManager _fileDialogManager;

    public ConfigWindow(IDalamudPluginInterface dalamudPluginInterface, WindowSystem windowSystem, StagehandConfiguration configuration) : base("Stagehand Configuration")
    {
        SizeCondition = ImGuiCond.Always;

        _dalamudPluginInterface = dalamudPluginInterface;
        _windowSystem = windowSystem;
        _configuration = configuration;

        _fileDialogManager = new FileDialogManager();
    }

    void IConfigWindow.Show()
    {
        IsOpen = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        using (ImRaii.ItemWidth(ImGui.GetContentRegionAvail().X * 2.0f / 3.0f))
        {
            var definitionLibraryPath = _configuration.DefinitionLibraryPath;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 2.0f / 3.0f - ImGui.GetStyle().ItemInnerSpacing.X - ImGui.GetFrameHeight());
            if (ImGui.InputText("###LibraryFolder", ref definitionLibraryPath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                // TODO: Might want to prompt whether to move the player's local definitions
                // TODO: Migrate auto load conditions
                _configuration.DefinitionLibraryPath = definitionLibraryPath;
                _configuration.Save();
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton("###BrowseLibraryFolder", FontAwesomeIcon.Folder, new Vector2(ImGui.GetFrameHeight())))
            {
                _fileDialogManager.OpenFolderDialog("Definition Library Folder", (confirmed, path) =>
                {
                    if (confirmed)
                    {
                        _configuration.DefinitionLibraryPath = path;
                        _configuration.Save();
                    }
                }, definitionLibraryPath);
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Definition Library Folder");
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
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 2.0f / 3.0f - ImGui.GetStyle().ItemInnerSpacing.X - ImGui.GetFrameHeight());
            if (ImGui.InputTextWithHint("###AutosaveFolder", "(leave blank for default)", ref autosavePath, 512, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll))
            {
                _configuration.AutosavePath = autosavePath;
                _configuration.Save();
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            if (ImGuiComponents.IconButton("###BrowseAutosaveFolder", FontAwesomeIcon.Folder, new Vector2(ImGui.GetFrameHeight())))
            {
                _fileDialogManager.OpenFolderDialog("Autosave Folder", (confirmed, path) =>
                {
                    if (confirmed)
                    {
                        _configuration.AutosavePath = path;
                        _configuration.Save();
                    }
                }, autosavePath);
            }
            ImGui.SameLine(0.0f, ImGui.GetStyle().ItemInnerSpacing.X);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Autosave Folder");
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

            ImGui.Spacing();
            ImGui.TextUnformatted("Debug Logging");
            var logMemoryResourceUntouched = _configuration.LogMemoryResourceUntouched;
            if (ImGui.Checkbox("Log Memory Resource Untouched", ref logMemoryResourceUntouched))
            {
                _configuration.LogMemoryResourceUntouched = logMemoryResourceUntouched;
                _configuration.Save();
            }
            var logMemoryResourceHandled = _configuration.LogMemoryResourceHandled;
            if (ImGui.Checkbox("Log Memory Resource Handled", ref logMemoryResourceHandled))
            {
                _configuration.LogMemoryResourceHandled = logMemoryResourceHandled;
                _configuration.Save();
            }
            var logModpackResourceUntouched = _configuration.LogModpackResourceUntouched;
            if (ImGui.Checkbox("Log Modpack Resource Untouched", ref logModpackResourceUntouched))
            {
                _configuration.LogModpackResourceUntouched = logModpackResourceUntouched;
                _configuration.Save();
            }
            var logModpackResourceHandled = _configuration.LogModpackResourceHandled;
            if (ImGui.Checkbox("Log Modpack Resource Handled", ref logModpackResourceHandled))
            {
                _configuration.LogModpackResourceHandled = logModpackResourceHandled;
                _configuration.Save();
            }
        }

        _fileDialogManager.Draw();
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
