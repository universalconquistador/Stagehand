using System;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Microsoft.Extensions.Hosting;

namespace Soundstage.Windows;

public class ConfigWindow : Window, IHostedService, IDisposable
{
    private readonly IDalamudPluginInterface _dalamudPluginInterface;
    private readonly WindowSystem _windowSystem;
    private readonly SoundstageConfiguration _configuration;

    private readonly byte[] _definitionLibraryPathBuffer = new byte[260];

    public ConfigWindow(IDalamudPluginInterface dalamudPluginInterface, WindowSystem windowSystem, SoundstageConfiguration configuration) : base("Soundstage Configuration")
    {
        SizeCondition = ImGuiCond.Always;

        _dalamudPluginInterface = dalamudPluginInterface;
        _windowSystem = windowSystem;
        _configuration = configuration;
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
