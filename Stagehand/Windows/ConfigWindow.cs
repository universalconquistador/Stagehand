using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;

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

    private readonly byte[] _definitionLibraryPathBuffer = new byte[260];

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

    public override void OnOpen()
    {
        base.OnOpen();

        Encoding.UTF8.GetBytes(_configuration.DefinitionLibraryPath, _definitionLibraryPathBuffer);
    }

    public override void Draw()
    {
        if (ImGui.InputText("Definition Library Folder", _definitionLibraryPathBuffer, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            // TODO: Might want to prompt whether to move the player's local definitions
            _configuration.DefinitionLibraryPath = Encoding.UTF8.GetString(_definitionLibraryPathBuffer.AsSpan().Slice(0, _definitionLibraryPathBuffer.IndexOf((byte)0)));
            _configuration.Save();
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
