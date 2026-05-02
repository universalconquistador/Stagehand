using HQIPC;

namespace Stagehand.Api;

/// <summary>
/// The Dalamud IPC interface for working with the Stagehand plugin.
/// </summary>
/// <remarks>
/// Use <see cref="StagehandApi.CreateIpcClient(Dalamud.Plugin.IDalamudPluginInterface)"/> to connect to Stagehand.
/// </remarks>
[IpcInterface("Stagehand")]
public partial interface IStagehandApi
{


}
