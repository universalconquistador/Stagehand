using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Stagehand.Api;
using System.IO;

namespace Stagehand.AssetThumbnailer;

public class Plugin : IDalamudPlugin
{
    public const string ThumbnailerCommand = "/stagehandthumbnailer";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    private readonly IStagehandApiConsumer _stagehandApi;
    private readonly WindowSystem _windowSystem = new("Stagehand.Thumbnailer");
    private readonly ThumbnailerWindow _thumbnailerWindow;

    public Configuration Configuration { get; }

    public Plugin()
    {
#if DEBUG
        // Use local build of FFXIVClientStructs
        InteropGenerator.Runtime.Resolver.GetInstance.Setup(
            SigScanner.SearchBase,
            DataManager.GameData.Repositories["ffxiv"].Version,
            new FileInfo(Path.Join(PluginInterface.ConfigDirectory.FullName, "SigCache.json")));
        FFXIVClientStructs.Interop.Generated.Addresses.Register();
        InteropGenerator.Runtime.Resolver.GetInstance.Resolve();
#endif

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _stagehandApi = StagehandApi.CreateIpcClient(PluginInterface);

        _thumbnailerWindow = new ThumbnailerWindow(Framework, DataManager, GameInteropProvider, _stagehandApi, Configuration);
        _windowSystem.AddWindow(_thumbnailerWindow);

        CommandManager.AddHandler(ThumbnailerCommand, new(OnThumbnailerCommandInvoked)
        {
            HelpMessage = "Opens the Stagehand Asset Thumbnailer window.",
        });

        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ShowThumbnailerWindow;
    }

    private void OnThumbnailerCommandInvoked(string command, string args)
    {
        ShowThumbnailerWindow();
    }

    private void ShowThumbnailerWindow()
    {
        _thumbnailerWindow.IsOpen = true;
        _thumbnailerWindow.RequestFocus = true;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenMainUi -= ShowThumbnailerWindow;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        CommandManager.RemoveHandler(ThumbnailerCommand);
        _windowSystem.RemoveAllWindows();
        _thumbnailerWindow.Dispose();
        _stagehandApi.Dispose();
    }
}
