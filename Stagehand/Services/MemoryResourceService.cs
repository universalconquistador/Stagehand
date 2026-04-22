using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.File;
using FFXIVClientStructs.FFXIV.Client.System.Resource;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using static FFXIVClientStructs.FFXIV.Common.Component.BGCollision.MeshPCB;

namespace Stagehand.Services;

/// <summary>
/// Lets you load resources backed by memory rather than a file.
/// </summary>
/// <remarks>
/// You would think this would be easier than it is...
/// </remarks>
public interface IMemoryResourceService
{
    string RegisterMemoryResource(byte[] data, string gamePath);
    bool TryUnregisterMemoryResource(string memoryResourcePath);
}

/// <summary>
/// 
/// </summary>
/// <remarks>
/// This works as follows:
/// <br/>
/// - By hooking <c>GetResource[Async]</c> the resource load is redirected to <c>"mem://&lt;key&gt;/gamePath"</c> where <c>&lt;key&gt;</c> is a unique ID provided by this service
/// <br />
/// - By hooking <c>ReadResource</c>, we identify that special prefix and detour to a custom implementation that mostly mirrors the load-from-file path but uses the unique ID to read from the given memory.
///   This also sets the FileMode on the descriptor so that downstream operations can recognize it, and uses the PlatformFile to hold the resource's byte array pointer and length instead of the HANDLE.
/// <br />
/// - By hooking the <c>Load</c> function of <c>TextureResourceHandle</c>, <c>ModelResourceHandle</c>, and <c>SoundResourceHandle</c>, we can detour to the versions of the functions that read directly from data without going through the game's sqpack filesystem.
/// <br />
/// - By hooking the <c>ReadFile</c> function, we can check for the custom file descriptor mode and if it is present, use the reference in the custom file descriptor to get the data for the memory file and copy it to the destination.
/// </remarks>
internal unsafe partial class MemoryResourceService : IMemoryResourceService, IDisposable
{
    public const byte LoadMemoryResourceFileMode = 0xF;

    private const string MemoryResourcePrefix = "mem://";

    //private delegate byte ReadFileProtype(ResourceManager* resourceManager, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, int priority, bool isSync);
    //[Signature("41 56 48 83 EC 28 0F BE 02"/*, DetourName = nameof(ReadResourceDetour)*/)]
    //private readonly Hook<ReadFileProtype> _readResourceHook = null!;

    private delegate byte ReadSqPackPrototype(ResourceManager* resourceManager, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, int priority, bool isSync);
    [Signature("40 56 41 56 48 83 EC ?? 0F BE 02", DetourName = nameof(ReadSqPackDetour))]
    private readonly Hook<ReadSqPackPrototype> _readSqPackHook = null!;

    private delegate byte PostLoadResourcePrototype(ResourceHandle* resourceHandle, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, byte lastIoResult, byte flag);
    [Signature("E8 ?? ?? ?? ?? 80 3F 0B 75 28")]
    private readonly PostLoadResourcePrototype _postLoadResource = null!;

    private delegate byte FileDescriptorRead(Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, byte* outputBuffer, ulong length, ulong start, bool failedToOpen);
    [Signature("E8 ?? ?? ?? ?? 8B 45 C7 89 83 ?? ?? ?? ??", DetourName = nameof(FileDescriptorReadDetour))]
    private readonly Hook<FileDescriptorRead> _fileDescriptorReadHook = null!;

    private readonly ILogger _logger;
    private readonly IGameInteropProvider _gameInteropProvider;
    private readonly StagehandConfiguration _config;

    private readonly ConcurrentDictionary<string, byte[]> _memoryResources = new();
    private ulong _lastId = 1;

    public MemoryResourceService(ILogger<MemoryResourceService> logger, IGameInteropProvider gameInteropProvider, StagehandConfiguration config)
    {
        _logger = logger;
        _gameInteropProvider = gameInteropProvider;
        _config = config;

        _modelResourceHandleReadHook = gameInteropProvider.HookFromSignature<ModelResourceHandleReadExternal>("E8 ?? ?? ?? ?? EB 02 B0 F1", ModelResourceHandleReadDetour, IGameInteropProvider.HookBackend.Reloaded);

        _gameInteropProvider.InitializeFromAttributes(this);
        _fileDescriptorReadHook.Enable();
        EnableResourceHandleHooks();
        //_readResourceHook.Enable();
        _readSqPackHook.Enable();
    }

    public string RegisterMemoryResource(byte[] data, string gamePath)
    {
        var id = Interlocked.Increment(ref _lastId).ToString();

        _memoryResources[id] = data;
        return $"{MemoryResourcePrefix}{id}/{gamePath}";
    }

    public bool TryUnregisterMemoryResource(string memoryResourcePath)
    {
        if (TryParseMemoryResourcePath(memoryResourcePath, out var resourceId, out _))
        {
            return _memoryResources.TryRemove(resourceId, out _);
        }
        else
        {
            return false;
        }
    }

    private static bool TryParseMemoryResourcePath(string resourcePath, [NotNullWhen(true)] out string? resourceId, [NotNullWhen(true)] out string? gamePath)
    {
        if (resourcePath.StartsWith(MemoryResourcePrefix))
        {
            // Parse the info from the path
            var firstSlash = resourcePath.IndexOf('/', MemoryResourcePrefix.Length);

            if (firstSlash > -1)
            {
                resourceId = resourcePath.Substring(MemoryResourcePrefix.Length, firstSlash - MemoryResourcePrefix.Length);
                gamePath = resourcePath.Substring(firstSlash + 1);
                return true;
            }
        }

        resourceId = null;
        gamePath = null;
        return false;
    }

    //private byte ReadResourceDetour(ResourceManager* resourceManager, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, int priority, bool isSync)
    private byte ReadSqPackDetour(ResourceManager* resourceManager, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, int priority, bool isSync)
    {
        // Look for packed resource requests with our special prefix
        string fileName = "";
        if (fileDescriptor->ResourceHandle != null)
        {
            fileName = fileDescriptor->ResourceHandle->FileName.ToString() ?? "";
        }
        if (fileDescriptor->ResourceHandle != null
            && fileDescriptor->FileMode == FileMode.LoadSqPackResource
            && TryParseMemoryResourcePath(fileName, out var memoryResourceId, out var gamePath))
        {

            // Switch the mode to our custom mode
            var originalResourceFilename = fileDescriptor->ResourceHandle->FileName;
            var filenameBytes = Encoding.UTF8.GetBytes(gamePath + "\0");
            var resourceBytes = _memoryResources.GetValueOrDefault(memoryResourceId);
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{fileName}] ReadResourceDetour handled! Id = {id}, gamePath = {gamePath}, found = {found}", fileName, memoryResourceId, gamePath, resourceBytes != null ? "True" : "False");
            }
            fixed (byte* filenameBytesPointer = filenameBytes)
            fixed (byte* resourceBytesPointer = resourceBytes)
            {
                fileDescriptor->ResourceHandle->FileName.BufferPtr = filenameBytesPointer;
                fileDescriptor->ResourceHandle->FileName.Length = (ulong)(filenameBytes.Length - 1);

                fileDescriptor->FileMode = (FileMode)LoadMemoryResourceFileMode;

                var oldFileDescriptor = fileDescriptor->FileDescriptor;

                PlatformFile tempDescriptor = new()
                {

                };
                fileDescriptor->FileDescriptor = &tempDescriptor;
                var result = ReadFileImpostor(resourceManager, fileDescriptor, priority, isSync, resourceBytesPointer, (ulong)(resourceBytes?.LongLength ?? 0));

                // Restore everything we changed
                fileDescriptor->ResourceHandle->FileName = originalResourceFilename;
                fileDescriptor->FileMode = FileMode.LoadSqPackResource;

                fileDescriptor->FileDescriptor = oldFileDescriptor;

                return result;
            }
        }
        else
        {
            if (_config.LogMemoryResourceUntouched)
            {
                _logger.LogDebug("[{fileName}] ReadResourceDetour untouched!", fileName);
            }

            //throw new NotImplementedException();
            //return _readResourceHook.OriginalDisposeSafe.Invoke(resourceManager, fileDescriptor, priority, isSync);
            return _readSqPackHook.OriginalDisposeSafe.Invoke(resourceManager, fileDescriptor, priority, isSync);
        }
    }

    private byte ReadFileImpostor(ResourceManager* resourceManager, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, int priority, bool isSync, byte* resourceBytes, ulong resourceLength)
    {
        var fileHandleManager = FileHandleManager.Instance();
        var fileHandle = fileHandleManager->GetFileHandle(fileDescriptor->FileHandleHandle);
        byte state2;
        using (var managerLock = fileHandleManager->Lock.Acquire())
        {
            state2 = fileHandle->State2;
        }
        byte platformIOResult = 1;
        if (state2 == 0)
        {
            if (!fileDescriptor->FileDescriptor->IsOpen)
            {
                // "Try opening" the file (analogue of func_14045C6E0_open_os_file)
                if (resourceBytes != null)
                {
                    fileDescriptor->FileDescriptor->PlatformHandle = (nint)resourceBytes;
                    fileDescriptor->FileDescriptor->IsOpen = true;

                    platformIOResult = 1; // Fake that we just opened the file
                }
                else
                {
                    unchecked
                    {
                        platformIOResult = (byte)(sbyte)-1;
                        // 1: Success
                        // -1: File/path not found
                    }
                }
                fileDescriptor->FileDescriptor->FileSize = resourceLength;
            }

            // Check the file handle's state2 again
            using (var managerLock = fileHandleManager->Lock.Acquire())
            {
                state2 = fileHandle->State2;
            }
            if (state2 == 0 && resourceBytes != null)
            {
                platformIOResult = ReadFileResourceImpostor(fileDescriptor->ResourceHandle, fileDescriptor, platformIOResult != 1);
            }
            else
            {
                platformIOResult = 2;
            }

            if (resourceBytes != null)
            {
                fileDescriptor->FileDescriptor->IsOpen = false;
            }

            // Check the file handle's state2 again again
            using (var managerLock = fileHandleManager->Lock.Acquire())
            {
                state2 = fileHandle->State2;
            }
            if (state2 != 0)
            {
                platformIOResult = 2;
            }
        }
        else
        {
            platformIOResult = 2;
        }
        using (var managerLock = fileHandleManager->Lock.Acquire())
        {
            fileHandle->AllocatedBuffer = null; // Only FileMode 1 allocates a buffer
            fileHandle->ResultLength = fileDescriptor->AmountToRead; // FileMode 1 makes sure the file handle size doesn't exceed the actual file size, but not FileMode 0, which instead puts the capped size in the resource handle.
        }
        using (var managerLock = fileHandleManager->Lock.Acquire())
        {
            fileHandle->Reset(platformIOResult);
        }
        
        // This calls vf40 or vf35 or vf33 (load)
        return _postLoadResource.Invoke(fileDescriptor->ResourceHandle, fileDescriptor, platformIOResult, (byte)0);
    }

    // func_1402EEDC0_read_unpacked_resource
    private byte ReadFileResourceImpostor(ResourceHandle* resourceHandle, Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, bool fileLoadFailed)
    {
        // Manipulate magic numbers and interlocked bytes to advance the load state of the resource
        if (InterlockedRead(ref resourceHandle->ReadState) == 3)
        {
            return 2;
        }
        resourceHandle->LoadState = 4;
        if (InterlockedRead(ref resourceHandle->OtherState) == 2)
        {
            if (resourceHandle->FileSize != 0)
            {
                return 1;
            }
            else
            {
                return 2;
            }
        }

        // Store the file sizes in the resource handle
        resourceHandle->FileSize2 = (uint)fileDescriptor->FileDescriptor->FileSize;

        // Cap the amount to read by the actual end of the memory resource
        var startOffset = fileDescriptor->StartOffset;
        var amountToRead = (uint)fileDescriptor->FileDescriptor->FileSize - startOffset;
        if (fileDescriptor->AmountToRead > 0)
        {
            amountToRead = Math.Min((uint)amountToRead, fileDescriptor->AmountToRead);
        }
        resourceHandle->FileSize = (uint)amountToRead;
        if (amountToRead == 0)
        {
            return 2;
        }

        // If it's already loaded, reload, otherwise just load
        if (InterlockedRead(ref resourceHandle->OtherState) == 1)
        {
            return resourceHandle->Reread(fileDescriptor, fileLoadFailed);
        }
        else
        {
            return resourceHandle->Read(fileDescriptor, fileLoadFailed);
        }
    }

    private byte FileDescriptorReadDetour(Live.ResourceRedirectionService.SeFileDescriptor* fileDescriptor, byte* outputBuffer, ulong length, ulong start, bool failedToOpen)
    {
        if (fileDescriptor->FileMode == (FileMode)LoadMemoryResourceFileMode)
        {
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{path}] FileDescriptorRead handled!", fileDescriptor->ResourceHandle->FileName.ToString() ?? "");
            }

            var totalSize = fileDescriptor->FileDescriptor->FileSize;
            if (length == 0)
            {
                length = totalSize - start;
            }

            if (fileDescriptor->FileDescriptor->PlatformHandle == 0 || start + length > totalSize)
            {
                unchecked
                {
                    return (byte)(sbyte)-10;
                }
            }
            else
            {
                NativeMemory.Copy((void*)fileDescriptor->FileDescriptor->PlatformHandle, outputBuffer, (nuint)length);
                return 1;
            }
            // TODO: Technically, we are supposed to call fileDescriptor->ResourceHandle->vf44, but those are all nullsubs. Perhaps compiled out of release builds?
        }
        else
        {
            if (_config.LogMemoryResourceUntouched)
            {
                _logger.LogDebug("FileDescriptorRead untouched!");
            }

            return _fileDescriptorReadHook.Original.Invoke(fileDescriptor, outputBuffer, length, start, failedToOpen);
        }
    }

    // The game does _InterlockedExchangeAdd8(ref value, 0) to check the value of fields it modifies with Interlocked functions.
    // .NET doesn't have an Interlocked.Add overload for bytes yet, so we use a no-op CompareExchange instead.
    private static byte InterlockedRead(ref byte location)
    {
        return Interlocked.CompareExchange(ref location, (byte)0, (byte)0);
    }

    public void Dispose()
    {
        _readSqPackHook.Dispose();
        //_readResourceHook.Dispose();
        DisposeResourceHandleHooks();
        _fileDescriptorReadHook.Dispose();
    }
}
