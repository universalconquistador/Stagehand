using Dalamud.Hooking;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.System.File;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina;
using System;
using System.Collections.Generic;
using System.Text;
using static FFXIVClientStructs.FFXIV.Common.Component.BGCollision.MeshPCB;
using static Stagehand.Live.ResourceRedirectionService;

namespace Stagehand.Services;

//
// The Read functions (virtual function 32) of certain resource handle types seem to not be able to handle reading from unpacked data like
// one would assume considering they explicitly check for the SqPack FileMode and have alternate paths.
// However, there are alternate versions of the Read functions that are not referenced in code or in data that *do* read from unpacked data.
// It's not clear why these are in the binary without actually being used, but we can use them to successfully read our raw files in memory.
//
// Thanks for spotting this Penumbra!
//
internal unsafe partial class MemoryResourceService
{
    private void EnableResourceHandleHooks()
    {
        _textureResourceHandleReadHook.Enable();
        _modelResourceHandleReadExternalHook.Enable();
        _modelResourceHandleReadHook.Enable();
        _soundResourceHandleReadHook.Enable();
    }

    private void DisposeResourceHandleHooks()
    {
        _soundResourceHandleReadHook.Dispose();
        _modelResourceHandleReadHook.Dispose();
        _modelResourceHandleReadExternalHook.Dispose();
        _textureResourceHandleReadHook.Dispose();
    }

    [Signature("E8 ?? ?? ?? ?? 44 8B 8D ?? ?? ?? ?? 33 FF")]
    private readonly delegate* unmanaged<byte*, ulong> _computeSplitHash = null!;

    #region TextureResourceHandle

    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC ?? 49 8B E8 44 88 4C 24")]
    private readonly delegate* unmanaged<TextureResourceHandle*, int, FileDescriptor*, bool, byte> _textureResourceHandleReadUnpacked = null!;

    [Signature("40 53 55 41 54 41 55 41 56 41 57 48 81 EC ?? ?? ?? ?? 48 8B D9", DetourName = nameof(TextureResourceHandleReadDetour))]
    private readonly Hook<TextureResourceHandle.Delegates.Read> _textureResourceHandleReadHook = null!;

    [Signature("48 8B 05 ?? ?? ?? ?? B3")]
    private readonly nint _lodConfig = nint.Zero;

    public byte GetLodOffsetIndex(TextureResourceHandle* handle)
    {
        if (handle->ChangeLod)
        {
            var config = *(byte*)_lodConfig + 0xE;
            if (config == byte.MaxValue)
                return 2;
        }

        return 0;
    }

    private byte TextureResourceHandleReadDetour(TextureResourceHandle* textureResourceHandle, FileDescriptor* descriptor, bool failedToOpen)
    {
        // If this is a memory resource, use the unpacked load function
        if (descriptor->FileMode == (FileMode)LoadMemoryResourceFileMode)
        {
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{fileName}] TextureResourceHandleReadDetour handled!", textureResourceHandle->ResourceHandle.FileName.ToString());
            }

            // The tex file header has 3 LOD offsets corresponding to the 3 selectable texture quality levels.
            int lodOffsetIndex = GetLodOffsetIndex(textureResourceHandle);
            return _textureResourceHandleReadUnpacked(textureResourceHandle, lodOffsetIndex, descriptor, failedToOpen);
        }
        else
        {
            if (_config.LogMemoryResourceUntouched)
            {
                _logger.LogDebug("[{fileName}] TextureResourceHandleReadDetour untouched!", textureResourceHandle->ResourceHandle.FileName.ToString());
            }

            return _textureResourceHandleReadHook.Original.Invoke(textureResourceHandle, descriptor, failedToOpen);
        }
    }

    #endregion

    #region ModelResourceHandle

    [Signature("48 89 5C 24 ?? 55 56 57 41 54 41 55 41 56 41 57 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 48 8B 72 ?? 4C 8B EA")]
    private readonly delegate* unmanaged<ModelResourceHandle*, FileDescriptor*, bool, byte> _modelResourceHandleReadUnpacked = null!;

    private delegate byte ModelResourceHandleReadExternal(ModelResourceHandle* modelResourceHandle, FileDescriptor* descriptor, bool failedToOpen, void* rsfEntry);

    //[Signature("E8 ?? ?? ?? ?? EB 02 B0 F1", DetourName = nameof(ModelResourceHandleReadDetour))]
    private readonly Hook<ModelResourceHandleReadExternal> _modelResourceHandleReadExternalHook = null!;

    private byte ModelResourceHandleReadExternalDetour(ModelResourceHandle* modelResourceHandle, FileDescriptor* descriptor, bool failedToOpen, void* rsfEntry)
    {
        // If this is a memory resource, use the unpacked load function
        if (descriptor->FileMode == (FileMode)LoadMemoryResourceFileMode)
        {
            failedToOpen = false;
            var result = _modelResourceHandleReadUnpacked(modelResourceHandle, descriptor, failedToOpen);
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{fileName}] ModelResourceHandleReadExternalDetour handled! ({result}, {failed})", modelResourceHandle->ResourceHandle.FileName.ToString(), result, failedToOpen ? "Failed" : "Not Failed");
            }

            return result;
        }
        else
        {
            if (_config.LogMemoryResourceUntouched)
            {
                _logger.LogDebug("[{fileName}] ModelResourceHandleReadExternalDetour untouched!", modelResourceHandle->ResourceHandle.FileName.ToString());
            }

            return _modelResourceHandleReadExternalHook.Original.Invoke(modelResourceHandle, descriptor, failedToOpen, rsfEntry);
        }
    }

    // Hook the vfunc so that we can test things
    private delegate byte ModelResourceHandleRead(ModelResourceHandle* handle, FileDescriptor* descriptor, bool failedToOpen);
    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 57 48 83 EC 20 80 3A 0B", DetourName = nameof(ModelResourceHandleReadDetour))]
    private readonly Hook<ModelResourceHandleRead> _modelResourceHandleReadHook = null!;
    private byte ModelResourceHandleReadDetour(ModelResourceHandle* handle, FileDescriptor* descriptor, bool failedToOpen)
    {
        if (descriptor->FileMode == (FileMode)LoadMemoryResourceFileMode)
        {
            failedToOpen = false;
            var result = _modelResourceHandleReadUnpacked(handle, descriptor, failedToOpen);
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{fileName}] ModelResourceHandleReadDetour handled! ({result}, {failed})", handle->ResourceHandle.FileName.ToString(), result, failedToOpen ? "Failed" : "Not Failed");
            }
            return result;
        }
        else
        {
            return _modelResourceHandleReadHook.Original.Invoke(handle, descriptor, failedToOpen);
        }
    }

    #endregion

    #region SoundResourceHandle

    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 8B 79 ?? 48 8B DA 8B D7")]
    private readonly delegate* unmanaged<SoundResourceHandle*, FileDescriptor*, bool, byte> _soundResourceHandleReadUnpacked = null!;

    [Signature("40 56 57 41 54 48 81 EC ?? ?? ?? ?? 80 3A ?? 45 0F B6 E0 48 8B F2 48 8B F9 75 ?? 83 BA ?? ?? ?? ?? ?? 72 ?? 48 8B 01 FF 90 ?? ?? ?? ?? 3C", DetourName = nameof(SoundResourceHandleReadDetour))]
    private readonly Hook<SoundResourceHandle.Delegates.Read> _soundResourceHandleReadHook = null!;

    private byte SoundResourceHandleReadDetour(SoundResourceHandle* soundResourceHandle, FileDescriptor* descriptor, bool failedToOpen)
    {
        // If this is a memory resource, use the unpacked read function
        if (descriptor->FileMode == (FileMode)LoadMemoryResourceFileMode)
        {
            if (_config.LogMemoryResourceHandled)
            {
                _logger.LogDebug("[{fileName}] SoundResourceHandleReadDetour handled!", soundResourceHandle->ResourceHandle.FileName.ToString());
            }

            return _soundResourceHandleReadUnpacked(soundResourceHandle, descriptor, failedToOpen);
        }
        else
        {
            if (_config.LogMemoryResourceUntouched)
            {
                _logger.LogDebug("[{fileName}] SoundResourceHandleReadDetour untouched!", soundResourceHandle->ResourceHandle.FileName.ToString());
            }

            return _soundResourceHandleReadHook.Original.Invoke(soundResourceHandle, descriptor, failedToOpen);
        }
    }

    #endregion
}
