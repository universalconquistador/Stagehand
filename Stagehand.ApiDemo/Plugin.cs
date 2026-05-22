using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Stagehand.Api;

namespace Stagehand.ApiDemo;

public class Plugin : IDalamudPlugin
{
    public const string ConfigCommand = "/stagehanddemo";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    public Configuration Configuration { get; }

    private readonly IStagehandApiConsumer _stagehandApi;
    private readonly WindowSystem _windowSystem = new("Stagehand.ApiDemo");
    private readonly ConfigWindow _configWindow;
    private readonly Trail _trail;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _stagehandApi = StagehandApi.CreateIpcClient(PluginInterface);

        _trail = new Trail(Configuration, Framework, ObjectTable, _stagehandApi);

        _configWindow = new ConfigWindow(Configuration, DataManager, _stagehandApi);
        _windowSystem.AddWindow(_configWindow);

        CommandManager.AddHandler(ConfigCommand, new(OnConfigCommandInvoked)
        {
            HelpMessage = "Opens the configuration window for the Stagehand API Demo."
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ShowConfigWindow;
    }

    private void OnConfigCommandInvoked(string command, string args)
    {
        ShowConfigWindow();
    }

    private void ShowConfigWindow()
    {
        _configWindow.IsOpen = true;
        _configWindow.RequestFocus = true;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenMainUi -= ShowConfigWindow;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        CommandManager.RemoveHandler(ConfigCommand);
        _windowSystem.RemoveAllWindows();
        _configWindow.Dispose();
        _trail.Dispose();
        _stagehandApi.Dispose();
    }
}
