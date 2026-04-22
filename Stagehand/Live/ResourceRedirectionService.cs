using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.System.File;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.FFXIV.Common.Lua;
using FFXIVClientStructs.Interop;
using InteropGenerator.Runtime.Attributes;
using Lumina.Data;
using Stagehand.Services;
using Stagehand.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Stagehand.Live;

public interface ILiveModpack : IDisposable
{
    string ID { get; }
    string DebugName { get; }
    uint EffectsHash { get; }
    IReadOnlyDictionary<string, string> AllRedirections { get; }
}

public interface IResourceRedirectionService
{

    ILiveModpack CreateModpack(string debugName, Dictionary<string, string> fileRedirections, Dictionary<string, byte[]> fileReplacements);
    string MakeModpackPath(string gamePath, ILiveModpack modpack);
}

public static class ResourceRedirectionHelpers
{
    public static uint HashModpackEffects(IReadOnlyDictionary<string, string> fileRedirections, IReadOnlyDictionary<string, byte[]> fileReplacements)
    {
        var hasher = new Crc32Hasher();

        hasher.Advance(fileRedirections.Count);
        foreach (var redirection in fileRedirections.OrderBy(pair => pair.Key))
        {
            hasher.Advance(redirection.Key.Length);
            hasher.AdvanceASCII(redirection.Key);

            hasher.Advance(redirection.Value.Length);
            hasher.AdvanceASCII(redirection.Value);
        }

        hasher.Advance(fileRedirections.Count);
        foreach (var replacement in fileReplacements.OrderBy(pair => pair.Key))
        {
            hasher.Advance(replacement.Key.Length);
            hasher.AdvanceASCII(replacement.Key);

            hasher.Advance(replacement.Value.Length);
            hasher.Advance(replacement.Value);
        }

        return hasher.Value;
    }
}

internal unsafe class ResourceRedirectionService : IResourceRedirectionService, IDisposable
{
    private record class LiveModpack(string ID, string DebugName, uint EffectsHash, IReadOnlyDictionary<string, string> AllRedirections, IReadOnlyList<string> MemoryResourceKeys, ResourceRedirectionService ResourceRedirectionService) : ILiveModpack
    {
        public void Dispose()
        {
            foreach (var path in MemoryResourceKeys)
            {
                ResourceRedirectionService._memoryResourceService.TryUnregisterMemoryResource(path);
            }
            ResourceRedirectionService._liveModpacks.TryRemove(ID, out _);
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
    // This contains all the info for an I/O request. It seems to be a union at some point, because certain FileModes
    // use the FileName bytes off the end very strangely.
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct SeFileDescriptor
    {
        [FieldOffset(0x00)]
        public FileMode FileMode;
        [FieldOffset(0x08)]
        public byte* DataBuffer;
        [FieldOffset(0x10)]
        public ulong AmountToRead;
        [FieldOffset(0x18)]
        public ulong StartOffset;

        [FieldOffset(0x28)]
        public FileHandleHandle FileHandleHandle;

        [FieldOffset(0x30)]
        public PlatformFile* FileDescriptor;
        [FieldOffset(0x38)]
        public ulong AllocationAlignment; // The alignment for the allocation to make if DataBuffer is null.

        [FieldOffset(0x48)]
        public IMemorySpace* AllocationMemorySpace; // If DataBuffer is null, this space will be used to allocate the result buffer, or the default space if null.

        [FieldOffset(0x50)]
        public ResourceHandle* ResourceHandle;

        [FieldOffset(0x70)]
        public char Utf16FileName;

        public string FileName
        {
            get
            {
                fixed (char* ptr = &Utf16FileName)
                {
                    return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(ptr).ToString();
                }
            }
        }
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
    private const string StagehandPathIdentifier = "shnd://";

    private readonly ILogger _logger;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly IMemoryResourceService _memoryResourceService;
    private readonly StagehandConfiguration _config;

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

    private readonly ConcurrentDictionary<string, LiveModpack> _liveModpacks = new();

    private readonly ThreadLocal<ILiveModpack?> _currentThreadModpack = new();

    public ResourceRedirectionService(ILogger<ResourceRedirectionService> logger, IGameInteropProvider gameInteropProvider, IMemoryResourceService memoryResourceService, StagehandConfiguration config)
    {
        _logger = logger;
        _gameInteropProvider = gameInteropProvider;
        _memoryResourceService = memoryResourceService;
        _config = config;

        _gameInteropProvider.InitializeFromAttributes(this);
        _getResourceSyncHook?.Enable();
        _getResourceAsyncHook?.Enable();
    }

    public ILiveModpack CreateModpack(string debugName, Dictionary<string, string> fileRedirections, Dictionary<string, byte[]> fileReplacements)
    {
        var newId = Guid.NewGuid().ToString();
        Dictionary<string, string> allRedirections = new(fileRedirections);
        List<string> memoryResourceKeys = new(fileReplacements.Count);
        foreach (var replacement in fileReplacements)
        {
            var path = _memoryResourceService.RegisterMemoryResource(replacement.Value, replacement.Key);
            memoryResourceKeys.Add(path);
            allRedirections[replacement.Key] = path;
        }

        var newModpack = new LiveModpack(newId, debugName, ResourceRedirectionHelpers.HashModpackEffects(fileRedirections, fileReplacements), allRedirections, memoryResourceKeys, this);
        Debug.Assert(_liveModpacks.TryAdd(newId, newModpack));
        return newModpack;
    }

    public string MakeModpackPath(string gamePath, ILiveModpack modpack)
    {
        return $"{StagehandPathIdentifier}{modpack.ID}/{gamePath}";
    }

    private bool TryParseModpackPath(string modpackPath, [NotNullWhen(true)] out ILiveModpack? modpack, [NotNullWhen(true)] out string? gamePath)
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
                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  and was redirected to {newPath}.", redirectedPath);
                }

                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, redirectedPath);
            }
            else
            {
                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  but isn't redirected, just using {path}.", gamePath);
                }

                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, gamePath);
            }

            Span<byte> gamePathBytes = stackalloc byte[1024];
            Encoding.UTF8.GetBytes(gamePath, gamePathBytes);
            fixed (byte* gamePathBytesPtr = gamePathBytes)
            {
                *categoryId = *_getResourceCategory(categoryId, gamePathBytesPtr);
            }

            var priorModpack = _currentThreadModpack.Value;
            _currentThreadModpack.Value = modpack;
            ResourceHandle* result;
            fixed (byte* newPathBufferPointer = newPathBuffer)
            {
                result = GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, newPathBufferPointer, pGetResParams, hasHandleLock, file, line);
            }
            _currentThreadModpack.Value = priorModpack;
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
                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  and was redirected to {newPath}.", redirectedPath);
                }

                RedirectToPath(ref *categoryId, ref *resourceType, ref *resourceHash, path, pGetResParams, newPathBuffer, redirectedPath);
                fixed (byte* newPathBufferPointer = newPathBuffer)
                {
                    return GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, newPathBufferPointer, pGetResParams, hasHandleLock, file, line);
                }
            }
            else
            {
                if (_config.LogModpackResourceHandled)
                {
                    _logger.LogDebug("  but isn't redirected, not touching {path}.", pathString);
                }
                return GetGameResource(isSync, resourceManager, categoryId, resourceType, resourceHash, path, pGetResParams, hasHandleLock, file, line);
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
        pathBuffer[pathByteCount] = 0;
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

    public void Dispose()
    {
        _getResourceSyncHook?.Dispose();
        _getResourceAsyncHook?.Dispose();
    }
}
