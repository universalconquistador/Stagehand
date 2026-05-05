using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Lumina.Data;
using Stagehand.Definitions.ModResources;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Stagehand.Live;

public record struct Redirection(string NewPath, ResourceCategory Category);

public interface ILiveModpack : IDisposable
{
    string ID { get; }
    string DebugName { get; }
    uint EffectsHash { get; }
    IReadOnlyDictionary<string, Redirection> AllRedirections { get; }
    bool ModdedResourceExists(string gamePath);
}

public readonly struct ModpackReadScope : IDisposable
{
    private readonly IResourceRedirectionService _resourceRedirectionService;

    public readonly ILiveModpack? PriorModpack;

    public ModpackReadScope(IResourceRedirectionService resourceRedirectionService, ILiveModpack? priorModpack)
    {
        _resourceRedirectionService = resourceRedirectionService;
        PriorModpack = priorModpack;
    }

    public void Dispose()
    {
        _resourceRedirectionService.SetCurrentModpack(PriorModpack);
    }
}

public interface IResourceRedirectionService
{
    ILiveModpack CreateModpack(string debugName, IReadOnlyDictionary<string, ModResourceDefinition> moddedResources);
    bool TryParseModpackPath(string modpackPath, [NotNullWhen(true)] out ILiveModpack? modpack, [NotNullWhen(true)] out string? gamePath);
    T? GetFile<T>(string gamePath, ILiveModpack? liveModpack)
        where T : FileResource;

    ILiveModpack? SetCurrentModpack(ILiveModpack? modpack);
}

public static class ResourceRedirectionHelpers
{
    public static uint HashModpackEffects(IReadOnlyDictionary<string, ModResourceDefinition> moddedResources)
    {
        var hashParams = new ModdedResourceHasherParams();

        Debug.WriteLine("Hashing (start: " + hashParams.Hasher.Value + ")");

        hashParams.Hasher.Advance(moddedResources.Count);
        foreach (var redirection in moddedResources.OrderBy(pair => pair.Key))
        {
            Debug.WriteLine($"Hashing {redirection.Key} ({redirection.Value}) (starting value: {hashParams.Hasher.Value})");
            hashParams.Hasher.Advance(redirection.Key.Length);
            hashParams.Hasher.AdvanceASCII(redirection.Key);

            redirection.Value.Visit<ModdedResourceHasher, ModdedResourceHasherParams, object?>(ref hashParams);
        }

        Debug.WriteLine($"Hashing done (result: {hashParams.Hasher.Value})");

        return hashParams.Hasher.Value;
    }

    public static ModpackReadScope OpenModpackScope(this IResourceRedirectionService resourceRedirectionService, ILiveModpack? modpack)
    {
        return new ModpackReadScope(resourceRedirectionService, resourceRedirectionService.SetCurrentModpack(modpack));
    }

    public static string MakeModpackPath(string gamePath, ILiveModpack modpack)
    {
        return $"{ResourceRedirectionService.StagehandPathIdentifier}{modpack.ID}/{gamePath}";
    }

    private struct ModdedResourceHasherParams
    {
        public Crc32Hasher Hasher;

        public ModdedResourceHasherParams()
        {
            Hasher = new();
        }
    }

    private class ModdedResourceHasher : IModResourceDefinitionVisitor<ModdedResourceHasherParams, object?>
    {
        public static object? VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref ModdedResourceHasherParams param)
        {
            // Hash the disk path
            param.Hasher.Advance(definition.SourceDiskPath.Length);
            param.Hasher.AdvanceASCII(definition.SourceDiskPath);

            // Hash the last modified date
            // TODO: Is this perfy? Do we need to add quick, coarse hashing and then more fine-grained comparison?
            DateTime lastModified = default;
            try
            {
                lastModified = File.GetLastWriteTimeUtc(definition.SourceDiskPath);
            }
            catch (Exception)
            { }
            param.Hasher.Advance(lastModified);

            return null;
        }

        public static object? VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref ModdedResourceHasherParams param)
        {
            param.Hasher.Advance(definition.CompressionScheme);
            param.Hasher.Advance(definition.CompressedDataBytes.Length);
            param.Hasher.Advance(definition.CompressedDataBytes);

            return null;
        }

        public static object? VisitGameModResourceDefinition(GameModResourceDefinition definition, ref ModdedResourceHasherParams param)
        {
            // Hash the game path
            param.Hasher.Advance(definition.SourceGamePath.Length);
            param.Hasher.AdvanceASCII(definition.SourceGamePath);

            return null;
        }
    }
}

internal unsafe class ResourceRedirectionService : IResourceRedirectionService, IDisposable
{
    private record struct LiveMemoryResource(string MemoryResourcePath, byte[] Data);

    private record class LiveModpack(string ID, string DebugName, uint EffectsHash, IReadOnlyDictionary<string, Redirection> AllRedirections, IReadOnlyDictionary<string, LiveMemoryResource> MemoryResources, IReadOnlyDictionary<string, string> GameResources, IReadOnlyDictionary<string, string> DiskResources, ResourceRedirectionService ResourceRedirectionService) : ILiveModpack
    {
        public void Dispose()
        {
            foreach (var path in MemoryResources.Values)
            {
                ResourceRedirectionService._memoryResourceService.TryUnregisterMemoryResource(path.MemoryResourcePath);
            }
            ResourceRedirectionService._liveModpacks.TryRemove(ID, out _);
        }

        public bool ModdedResourceExists(string gamePath)
        {
            return AllRedirections.ContainsKey(gamePath);
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct GetResourceParameters
    {
        [FieldOffset(16)]
        public uint SegmentOffset;

        [FieldOffset(20)]
        public uint SegmentLength;

        public bool IsPartialRead
            => SegmentLength != 0;
    }

    private enum ResourceType : uint
    {
        Unknown = 0,
        Aet = 0x00616574,
        Amb = 0x00616D62,
        Atch = 0x61746368,
        Atex = 0x61746578,
        Avfx = 0x61766678,
        Awt = 0x00617774,
        Bklb = 0x626B6C62,
        Cmp = 0x00636D70,
        Cutb = 0x63757462,
        Dic = 0x00646963,
        Eanb = 0x65616E62,
        Eid = 0x00656964,
        Envb = 0x656E7662,
        Eqdp = 0x65716470,
        Eqp = 0x00657170,
        Eslb = 0x65736C63,
        Essb = 0x65737362,
        Est = 0x00657374,
        Evp = 0x00657670,
        Exd = 0x00657864,
        Exh = 0x00657868,
        Exl = 0x0065786C,
        Fdt = 0x00666474,
        Fpeb = 0x66706562,
        Gfd = 0x00676664,
        Ggd = 0x00676764,
        Gmp = 0x00676D70,
        Gzd = 0x00677A64,
        Imc = 0x00696D63,
        Kdb = 0x006B6462,
        Kdlb = 0x6B646C62,
        Lcb = 0x006C6362,
        Lgb = 0x006C6762,
        Luab = 0x6C756162,
        Lvb = 0x006C7662,
        Mdl = 0x006D646C,
        Mlt = 0x006D6C74,
        Mtrl = 0x6D74726C,
        Obsb = 0x6F627362,
        Pap = 0x00706170,
        Pbd = 0x00706264,
        Pcb = 0x00706362,
        Phyb = 0x70687962,
        Plt = 0x00706C74,
        Scd = 0x00736364,
        Sgb = 0x00736762,
        Shcd = 0x73686364,
        Shpk = 0x7368706B,
        Sklb = 0x736B6C62,
        Skp = 0x00736B70,
        Stm = 0x0073746D,
        Svb = 0x00737662,
        Tera = 0x74657261,
        Tex = 0x00746578,
        Tmb = 0x00746D62,
        Ugd = 0x00756764,
        Uld = 0x00756C64,
        Waoe = 0x77616F65,
        Wtd = 0x00777464,
    }


    // This is chosen so that it parses into one of the existing resource categories (shader, by starting with 'sh')
    internal const string StagehandPathIdentifier = "shnd://";

    private readonly ILogger _logger;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IDataManager _dataManager;
    private readonly IMemoryResourceService _memoryResourceService;
    private readonly StagehandConfiguration _config;

    // We need to load a unique copy of these per modpack, in case their dependencies differ even though they themselves might not.
    // In the future it might be good to try to do a more advanced computation for how to share a single copy of a resource based on whether
    // its dependencies are the same between given modpacks, like a dependencies hash or something.
    private static readonly ResourceType[] ResourcesWithDependencies =
    [
        ResourceType.Mdl, // Depends on mtrls
        ResourceType.Mtrl, // Depends on shpks and texs
        ResourceType.Avfx, // Depends on atexs and scds
    ];

    // Lovingly yoinked from Penumbra
    private delegate ResourceHandle* GetResourceSyncPrototype(ResourceManager* resourceManager, ResourceCategory* pCategoryId,
    ResourceType* pResourceType, int* pResourceHash, byte* pPath, GetResourceParameters* pGetResParams, byte* file, uint line);

    private delegate ResourceHandle* GetResourceAsyncPrototype(ResourceManager* resourceManager, ResourceCategory* pCategoryId,
        ResourceType* pResourceType, int* pResourceHash, byte* pPath, GetResourceParameters* pGetResParams, byte hasHandleLock, byte* file,
        uint line);
    
    [Signature("E8 ?? ?? ?? ?? 48 8B C8 8B C3 F0 0F C0 81", DetourName = nameof(GetResourceSyncDetour))]
    private readonly Hook<GetResourceSyncPrototype> _getResourceSyncHook = null!;

    [Signature("E8 ?? ?? ?? 00 48 8B D8 EB ?? F0 FF 83 ?? ?? 00 00", DetourName = nameof(GetResourceAsyncDetour))]
    private readonly Hook<GetResourceAsyncPrototype> _getResourceAsyncHook = null!;

    [Signature("E8 ?? ?? ?? ?? 4D 8B 04 3E")]
    private readonly delegate* unmanaged<ResourceCategory*, byte*, ResourceCategory*> _getResourceCategory = null!;

    [Signature("E8 ?? ?? ?? ?? 84 C0 75 12 B0 F6", DetourName = nameof(ModelResourceHandleLoadMaterialsDetour))]
    private readonly Hook<ModelResourceHandle.Delegates.LoadMaterials> _modelResourceHandleLoadMaterialsHook = null!;

    private readonly ConcurrentDictionary<string, LiveModpack> _liveModpacks = new();

    private readonly ThreadLocal<ILiveModpack?> _currentThreadModpack = new();

    public ResourceRedirectionService(ILogger<ResourceRedirectionService> logger, IGameInteropProvider gameInteropProvider, IDataManager dataManager, IMemoryResourceService memoryResourceService, StagehandConfiguration config)
    {
        _logger = logger;
        _gameInteropProvider = gameInteropProvider;
        _dataManager = dataManager;
        _memoryResourceService = memoryResourceService;
        _config = config;

        memoryResourceService.ResourceRedirectionService = this; // MASSIVE HACK PENDING REFACTOR

        _gameInteropProvider.InitializeFromAttributes(this);
        _modelResourceHandleLoadMaterialsHook.Enable();
        _getResourceSyncHook?.Enable();
        _getResourceAsyncHook?.Enable();
    }

    private bool ModelResourceHandleLoadMaterialsDetour(ModelResourceHandle* modelResourceHandle)
    {
        var originalModpack = _currentThreadModpack.Value;

        // We strip the shnd and mem prefixes when reading the resource. However, sometimes this is called outside that.

        // If this path is already a modpack path, use the modpack to resolve the final path
        if (Utf8StringStartsWith(modelResourceHandle->FileName.BasicString.First, StagehandPathIdentifier) && TryParseModpackPath(modelResourceHandle->FileName.ToString(), out var modpack, out var gamePath))
        {
            _currentThreadModpack.Value = modpack;
            _logger.LogDebug("Using modpack {pack} for materials of {path}", modpack.DebugName, modelResourceHandle->FileName.ToString());
        }
        else
        {
            _logger.LogDebug("Using already-set modpack {name} for materials of {path}", originalModpack?.DebugName ?? "(null)", modelResourceHandle->FileName.ToString());
        }

        var result = _modelResourceHandleLoadMaterialsHook.Original.Invoke(modelResourceHandle);
        _currentThreadModpack.Value = originalModpack;
        return result;
    }

    private ulong nextModpackId = 1;
    public ILiveModpack CreateModpack(string debugName, IReadOnlyDictionary<string, ModResourceDefinition> moddedResources)
    {
        var extractContext = new ExtractModResourcesParams()
        {
            DiskReplacements = new(),
            Redirections = new(),
            MemoryReplacements = new(),
        };
        foreach (var moddedResource in moddedResources)
        {
            extractContext.GamePath = moddedResource.Key;
            moddedResource.Value.Visit<ModResourceExtractor, ExtractModResourcesParams, object?>(ref extractContext);
        }

        var redirections = extractContext.Redirections;
        var memoryReplacements = extractContext.MemoryReplacements;
        var fileReplacements = extractContext.DiskReplacements;

        var newId = Interlocked.Increment(ref nextModpackId).ToString();
        Dictionary<string, Redirection> allRedirections = new(redirections.Count + memoryReplacements.Count + fileReplacements.Count);
        Span<byte> filename = stackalloc byte[1024];
        foreach (var redirection in redirections)
        {
            // The category of a redirection comes from the destination game path
            ResourceCategory category = ResourceCategory.BgCommon;
            Encoding.UTF8.GetBytes(redirection.Value + "\0", filename);

            fixed (byte* filenamePointer = filename)
            {
                _getResourceCategory(&category, filenamePointer);
            }
            allRedirections[redirection.Key] = new Redirection(redirection.Value, category);
        }
        Dictionary<string, LiveMemoryResource> memoryResources = new(memoryReplacements.Count);
        foreach (var replacement in memoryReplacements)
        {
            var path = _memoryResourceService.RegisterMemoryResource(replacement.Value, replacement.Key);
            memoryResources.Add(replacement.Key, new(path, replacement.Value));
            // The category of a replacement comes from the source game path (and doesn't really matter as no data is fetched from the category itself)
            ResourceCategory category = ResourceCategory.BgCommon;
            Encoding.UTF8.GetBytes(replacement.Key + "\0", filename);

            fixed (byte* filenamePointer = filename)
            {
                _getResourceCategory(&category, filenamePointer);
            }
            allRedirections[replacement.Key] = new(path, category);
        }
        foreach (var replacement in fileReplacements)
        {
            // The category of a replacement comes from the source game path (and doesn't really matter as no data is fetched from the category itself)
            ResourceCategory category = ResourceCategory.BgCommon;
            Encoding.UTF8.GetBytes(replacement.Key + "\0", filename);

            fixed (byte* filenamePointer = filename)
            {
                _getResourceCategory(&category, filenamePointer);
            }
            allRedirections[replacement.Key] = new(replacement.Value, category);
        }

        var newModpack = new LiveModpack(newId, debugName, ResourceRedirectionHelpers.HashModpackEffects(moddedResources), allRedirections, memoryResources, extractContext.Redirections, extractContext.DiskReplacements, this);
        Debug.Assert(_liveModpacks.TryAdd(newId, newModpack));
        return newModpack;
    }
    private record struct ExtractModResourcesParams(string GamePath, Dictionary<string, string> Redirections, Dictionary<string, byte[]> MemoryReplacements, Dictionary<string, string> DiskReplacements);
    private class ModResourceExtractor : IModResourceDefinitionVisitor<ExtractModResourcesParams, object?>
    {
        public static object? VisitDiskModResourceDefinition(DiskModResourceDefinition definition, ref ExtractModResourcesParams param)
        {
            param.DiskReplacements.Add(param.GamePath, definition.SourceDiskPath);
            return null;
        }

        public static object? VisitEmbeddedModResourceDefinition(EmbeddedModResourceDefinition definition, ref ExtractModResourcesParams param)
        {
            param.MemoryReplacements.Add(param.GamePath, EmbeddedModResourceDefinition.DecompressDataBytes(definition.CompressedDataBytes, definition.CompressionScheme));
            return null;
        }

        public static object? VisitGameModResourceDefinition(GameModResourceDefinition definition, ref ExtractModResourcesParams param)
        {
            param.Redirections.Add(param.GamePath, definition.SourceGamePath);
            return null;
        }
    }

    public bool TryParseModpackPath(string modpackPath, [NotNullWhen(true)] out ILiveModpack? modpack, [NotNullWhen(true)] out string? gamePath)
    {
        if (!modpackPath.StartsWith(StagehandPathIdentifier))
        {
            modpack = null;
            gamePath = null;
            return false;
        }

        var slashIndex = modpackPath.IndexOf('/', StagehandPathIdentifier.Length);
        if (slashIndex == -1)
        {
            modpack = null;
            gamePath = null;
            return false;
        }

        var modpackId = modpackPath.Substring(StagehandPathIdentifier.Length, slashIndex - StagehandPathIdentifier.Length);
        gamePath = modpackPath.Substring(slashIndex + 1);

        var result = _liveModpacks.TryGetValue(modpackId, out var foundModpack);
        modpack = foundModpack;
        return result;
    }

    public T? GetFile<T>(string gamePath, ILiveModpack? liveModpack)
        where T : FileResource
    {
        if (liveModpack != null && liveModpack is LiveModpack internalLiveModpack)
        {
            if (internalLiveModpack.MemoryResources.TryGetValue(gamePath, out var memoryResource))
            {
                return GetMemoryResource<T>(memoryResource.Data, gamePath);
            }
            else if (internalLiveModpack.GameResources.TryGetValue(gamePath, out var newGamePath))
            {
                return _dataManager.GetFile<T>(newGamePath);
            }
            else if (internalLiveModpack.DiskResources.TryGetValue(gamePath, out var diskPath))
            {
                return _dataManager.GameData.GetFileFromDisk<T>(diskPath, gamePath);
            }
        }

        return _dataManager.GetFile<T>(gamePath);
    }

    private T GetMemoryResource<T>(byte[] data, string gamePath)
        where T : FileResource
    {
        // TODO: A better way?
        var file = Activator.CreateInstance<T>();

        // file.Data = data;
        var dataProperty = typeof(T).GetProperty(nameof(FileResource.Data), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        dataProperty!.SetValue(file, data);

        // file.FilePath = ParseFilePath(gamePath);
        var filePathProperty = typeof(T).GetProperty(nameof(FileResource.FilePath), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        filePathProperty!.SetValue(file, Lumina.GameData.ParseFilePath(gamePath));

        // file.Reader = new LuminaBinaryReader(data, Options.CurrentPlatform);
        var fileReaderProperty = typeof(T).GetProperty(nameof(FileResource.Reader), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        fileReaderProperty!.SetValue(file, new LuminaBinaryReader(data));

        file.LoadFile();

        return file;
    }

    private ResourceHandle* GetResourceSyncDetour(ResourceManager* resourceManager, ResourceCategory* categoryId, ResourceType* resourceType,
        int* resourceHash, byte* path, GetResourceParameters* pGetResParams, byte* file, uint line)
        => GetResourceHandler(true, resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, 0, file, line);

    private ResourceHandle* GetResourceAsyncDetour(ResourceManager* resourceManager, ResourceCategory* categoryId, ResourceType* resourceType,
        int* resourceHash, byte* path, GetResourceParameters* pGetResParams, byte hasHandleLock, byte* file, uint line)
        => GetResourceHandler(false, resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, hasHandleLock, file, line);

    private ResourceHandle* GetResourceHandler(bool isSync, ResourceManager* resourceManager, ResourceCategory* categoryId,
        ResourceType* resourceType, int* resourceHash, byte* path, GetResourceParameters* pGetResParams, byte hasHandleLock, byte* file, uint line)
    {
        string pathString = ReadUtf8String(path);
        
        // If this path is already a modpack path, use the modpack to resolve the final path
        if (Utf8StringStartsWith(path, StagehandPathIdentifier) && TryParseModpackPath(ReadUtf8String(path), out var modpack, out var gamePath))
        {
            if (_config.LogModpackResourceHandled)
            {
                _logger.LogDebug("Resource requested with specified modpack '{packName}' ({packId})! {async} {category} {type} {path}", modpack.DebugName, modpack.ID, isSync ? "Sync" : "Async", *categoryId, *resourceType, pathString);
            }

            // Turn the modpack path into a game path if it's redirected or unmodded
            Span<byte> newPathBuffer = stackalloc byte[1024];
            if (modpack.AllRedirections.TryGetValue(gamePath, out var redirectedPath))
            {
                // If this resource type can have dependencies, we need to pipe the modpack ID through (which means we load a unique copy per modpack) so dependency paths can be resolved
                if (ResourcesWithDependencies.Contains(*resourceType))
                {
                    redirectedPath.NewPath = ResourceRedirectionHelpers.MakeModpackPath(redirectedPath.NewPath, modpack);
                }

                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  and was redirected to {newPath}.", redirectedPath);
                }
                *categoryId = redirectedPath.Category;

                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, redirectedPath.NewPath);
            }
            else
            {
                Span<byte> gamePathBytes = stackalloc byte[1024];

                // Fix up the category
                Encoding.UTF8.GetBytes(gamePath, gamePathBytes);
                fixed (byte* gamePathBytesPtr = gamePathBytes)
                {
                    *categoryId = *_getResourceCategory(categoryId, gamePathBytesPtr);
                }

                // If this resource type can have dependencies, we need to pipe the modpack ID through (which means we load a unique copy per modpack) so dependency paths can be resolved
                if (ResourcesWithDependencies.Contains(*resourceType))
                {
                    gamePath = ResourceRedirectionHelpers.MakeModpackPath(gamePath, modpack);
                }

                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  but isn't redirected, just using {path}.", gamePath);
                }
                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, gamePath);
            }

            var priorModpack = _currentThreadModpack.Value;
            _currentThreadModpack.Value = modpack;
            _logger.LogDebug("Setting current modpack to {pack}", modpack.DebugName);
            ResourceHandle* result;
            fixed (byte* newPathBufferPointer = newPathBuffer)
            {
                result = GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, newPathBufferPointer, pGetResParams, hasHandleLock, file, line);
            }
            _currentThreadModpack.Value = priorModpack;
            _logger.LogDebug("Restored modpack to {pack}", priorModpack?.DebugName ?? "null");
            return result;
        }
        // If this path isn't a modpack one but we're recursed inside another modpack's sync resource query, use that modpack
        else if (_currentThreadModpack.Value is ILiveModpack outerModpack)
        {
            if (_config.LogModpackResourceHandled)
            {
                _logger.LogDebug("Resource requested within an outer modpack resource '{packName}' ({packId})! {async} {category} {type} {path}", outerModpack.DebugName, outerModpack.ID, isSync ? "Sync" : "Async", *categoryId, *resourceType, pathString);
            }

            Span<byte> newPathBuffer = stackalloc byte[1024];
            if (outerModpack.AllRedirections.TryGetValue(pathString, out var redirectedPath))
            {
                // If this resource type can have dependencies, we need to pipe the modpack ID through (which means we load a unique copy per modpack) so dependency paths can be resolved
                if (ResourcesWithDependencies.Contains(*resourceType))
                {
                    redirectedPath.NewPath = ResourceRedirectionHelpers.MakeModpackPath(redirectedPath.NewPath, outerModpack);
                }

                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  and was redirected to {newPath}.", redirectedPath);
                }

                *categoryId = redirectedPath.Category;
                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, redirectedPath.NewPath);
                fixed (byte* newPathBufferPointer = newPathBuffer)
                {
                    return GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, newPathBufferPointer, pGetResParams, hasHandleLock, file, line);
                }
            }
            else
            {
                var newPath = pathString;

                // If this resource type can have dependencies, we need to pipe the modpack ID through (which means we load a unique copy per modpack) so dependency paths can be resolved
                if (ResourcesWithDependencies.Contains(*resourceType))
                {
                    newPath = ResourceRedirectionHelpers.MakeModpackPath(newPath, outerModpack);
                }

                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  but isn't redirected, sending to {path}.", newPath);
                }
                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, newPath);
                fixed (byte* newPathBufferPointer = newPathBuffer)
                {
                    return GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, newPathBufferPointer, pGetResParams, hasHandleLock, file, line);
                }
            }
        }
        else
        {
            if (_config.LogModpackResourceUntouched)
            {
                _logger.LogDebug("Resource requested with no modpack! {async} {category} {type} {path}", isSync ? "Sync" : "Async", *categoryId, *resourceType, pathString);
            }

            return GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, hasHandleLock, file, line);
        }
    }

    private void RedirectToPath(ref ResourceCategory category, ref ResourceType type, ref int hash, byte* pathBuffer, GetResourceParameters* getResourceParameters, Span<byte> newPathBuffer, string newPath)
    {
        var pathByteCount = Encoding.ASCII.GetBytes(newPath, newPathBuffer);
        newPathBuffer[pathByteCount] = 0;
        pathBuffer = newPathBuffer.GetPointer(0);
        hash = ComputeHash(pathBuffer, getResourceParameters);
    }

    /// <summary> Compute the CRC32 hash for a given path together with potential resource parameters. </summary>
    private static int ComputeHash(byte* path, GetResourceParameters* pGetResParams)
    {
        var hasher = new Crc32Hasher();

        hasher.Advance(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(path));

        if (pGetResParams != null && pGetResParams->IsPartialRead)
        {
            // When the game requests file only partially, crc32 includes that information, in format of:
            // path/to/file.ext.hex_offset.hex_size
            // ex) music/ex4/BGM_EX4_System_Title.scd.381adc.30000
            hasher.Advance((byte)'.');
            hasher.AdvanceASCII(pGetResParams->SegmentOffset.ToString("x"));
            hasher.Advance((byte)'.');
            hasher.AdvanceASCII(pGetResParams->SegmentLength.ToString("x"));
        }

        return (int)hasher.Value;
    }

    // Gets vanilla game resource
    private ResourceHandle* GetGameResource(bool isSync, ResourceManager* resourceManager, ResourceCategory* categoryId,
        ResourceType* resourceType, int* resourceHash, byte* path, GetResourceParameters* pGetResParams, byte hasHandleLock, byte* file, uint line)
    {
        return isSync ? _getResourceSyncHook.OriginalDisposeSafe.Invoke(resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, file, line) : _getResourceAsyncHook.OriginalDisposeSafe.Invoke(resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, hasHandleLock, file, line);
    }

    private static bool Utf8StringStartsWith(byte* str, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (str[i] != (byte)value[i] || str[i] == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadUtf8String(byte* str)
    {
        var i = 0;
        while (str[i] != 0)
        {
            i += 1;
        }
        return Encoding.UTF8.GetString(str, i);
    }

    public ILiveModpack? SetCurrentModpack(ILiveModpack? modpack)
    {
        // We don't need to do Interlocked or anything as this is a thread local, which by definition will not be accessed concurrently
        var current = _currentThreadModpack.Value;
        _currentThreadModpack.Value = modpack;

        _logger.LogDebug("Modpack for thread {thread} set to {new} from {old}", Thread.CurrentThread.Name ?? Thread.CurrentThread.ManagedThreadId.ToString(), modpack?.DebugName ?? "(null)", current?.DebugName ?? "(null)"); ;
        return current;
    }

    public void Dispose()
    {
        _getResourceSyncHook?.Dispose();
        _getResourceAsyncHook?.Dispose();
        _modelResourceHandleLoadMaterialsHook.Dispose();
    }
}
