using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Stagehand.Editor;
using Stagehand.Editor.Services;
using Stagehand.Editor.Tools;
using Stagehand.Live;
using Stagehand.Services;
using Stagehand.Utils;
using Stagehand.Windows;
using System;
using System.IO;
using System.Numerics;

namespace Stagehand;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;

    private readonly IHost _host;

    private readonly OverlayService _overlayService;

    public StagehandConfiguration Configuration { get; init; }
    
    public readonly WindowSystem WindowSystem = new("Stagehand");

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

        Configuration = PluginInterface.GetPluginConfig() as StagehandConfiguration ?? new StagehandConfiguration();
        if (string.IsNullOrEmpty(Configuration.DefinitionLibraryPath))
        {
            Configuration.DefinitionLibraryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Stages");
            Configuration.Save();
        }

        Directory.CreateDirectory(Configuration.DefinitionLibraryPath);

        _overlayService = new OverlayService(GameGui);

        // Fluent style syntax is an abberant abomination, so we use sane C# instead
        var hostBuilder = new HostBuilder();
        hostBuilder.UseContentRoot(PluginInterface.ConfigDirectory.FullName);
        hostBuilder.ConfigureLogging(logBuilder =>
        {
            logBuilder.ClearProviders();
            logBuilder.AddDalamudLogging(Log);
            logBuilder.SetMinimumLevel(LogLevel.Trace);
        });
        hostBuilder.ConfigureServices(services =>
        {
            services.AddSingleton(PluginInterface);
            services.AddSingleton(TextureProvider);
            services.AddSingleton(CommandManager);
            services.AddSingleton(ClientState);
            services.AddSingleton(Configuration);
            services.AddSingleton(PlayerState);
            services.AddSingleton(DataManager);
            services.AddSingleton(Framework);
            services.AddSingleton(GameInteropProvider);
            services.AddSingleton(ObjectTable);
            services.AddSingleton(SigScanner);
            services.AddSingleton(GameGui);
            services.AddSingleton(WindowSystem);

            services.AddSingleton<IModelBvhCacheService, ModelBvhCacheService>();
            services.AddSingleton<IOverlayService>(_overlayService);
            services.AddSingleton<ILocalDefinitionService, LocalDefinitionService>();
            services.AddSingleton<ILiveObjectService, LiveObjectService>();
            services.AddSingleton<ILiveStageService, LiveStageService>();
            services.AddSingleton<IEditorService, EditorService>();

            services.AddHostedService<ConfigWindow>();
            services.AddHostedService<DebugWindow>();
            services.AddHostedService<LibraryWindow>();
            services.AddSingleton<LocalStageService>();
            services.AddHostedService(c => c.GetRequiredService<LocalStageService>());

            // Editor services are scoped to the editor session
            services.AddScoped<IEditorTool, SelectTool>();
            services.AddScoped<IEditorTool, MoveTool>();
            services.AddScoped<IEditorTool, RotateTool>();
            services.AddScoped<IEditorTool, ScaleTool>();
            services.AddScoped<IToolManager, ToolManager>();
            services.AddScoped<IOutliner, Outliner>();
            services.AddScoped<ISelectionManager, SelectionManager>();
        });

        _host = hostBuilder.Build();
        _ = _host.StartAsync();

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += Draw;
    }

    public void Draw()
    {
        WindowSystem.Draw();
        _overlayService.Draw();
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();

        WindowSystem.RemoveAllWindows();
    }
}
