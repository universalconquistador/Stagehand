using Dalamud.Plugin;
using Dalamud.Plugin.Ipc.Exceptions;
using Penumbra.Api;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Services;

public interface IPenumbraInteropService
{
    bool IsAvailable { get; }
    string ModDirectory { get; }

    (Guid Id, string Name)? GetCollection(ApiCollectionType type);

    /// <summary>
    /// Gets the map of directory name to mod name.
    /// </summary>
    Dictionary<string, string> GetModList();

    IReadOnlyDictionary<string, (string[], GroupType)>? GetAvailableModSettings(string modDirectory);

    (PenumbraApiEc, (bool, int, Dictionary<string, List<string>>, bool, bool)?) GetCurrentModSettingsWithTemp(Guid collectionId,
        string modDirectory, bool ignoreInheritance, bool ignoreTemporary, int key);

    (PenumbraApiEc, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?) GetAllModSettings(Guid collectionId,
        bool ignoreInheritance = false, bool ignoreTemporary = false, int key = 0);
}

internal class PenumbraInteropService : IPenumbraInteropService
{
    private readonly GetEnabledState _getEnabledState;
    private readonly GetModDirectory _getModDirectory;
    private readonly GetCollection _getCollection;
    private readonly GetModList _getModList;
    private readonly GetAvailableModSettings _getAvailableModSettings;
    private readonly GetCurrentModSettingsWithTemp _getCurrentModSettingsWithTemp;
    private readonly GetAllModSettings _getAllModSettings;

    public bool IsAvailable
    {
        get
        {
            try
            {
                return _getEnabledState.Invoke();
            }
            catch (IpcNotReadyError)
            {
                return false;
            }
        }
    }
    public string ModDirectory => _getModDirectory.Invoke();

    public PenumbraInteropService(IDalamudPluginInterface dalamudPluginInterface)
    {
        _getEnabledState = new(dalamudPluginInterface);
        _getModDirectory = new(dalamudPluginInterface);
        _getCollection = new(dalamudPluginInterface);
        _getModList = new(dalamudPluginInterface);
        _getAvailableModSettings = new(dalamudPluginInterface);
        _getCurrentModSettingsWithTemp = new(dalamudPluginInterface);
        _getAllModSettings = new(dalamudPluginInterface);
    }

    public (Guid Id, string Name)? GetCollection(ApiCollectionType type)
    {
        return _getCollection.Invoke(type);
    }

    public Dictionary<string, string> GetModList()
    {
        return _getModList.Invoke();
    }

    public IReadOnlyDictionary<string, (string[], GroupType)>? GetAvailableModSettings(string modDirectory)
    {
        return _getAvailableModSettings.Invoke(modDirectory);
    }

    public (PenumbraApiEc, (bool, int, Dictionary<string, List<string>>, bool, bool)?) GetCurrentModSettingsWithTemp(Guid collectionId,
        string modDirectory, bool ignoreInheritance, bool ignoreTemporary, int key)
    {
        return _getCurrentModSettingsWithTemp.Invoke(collectionId, modDirectory, modName: "", ignoreInheritance, ignoreTemporary, key);
    }

    public (PenumbraApiEc, Dictionary<string, (bool, int, Dictionary<string, List<string>>, bool, bool)>?) GetAllModSettings(Guid collectionId,
        bool ignoreInheritance, bool ignoreTemporary, int key)
    {
        return _getAllModSettings.Invoke(collectionId, ignoreInheritance, ignoreTemporary, key);
    }
}
