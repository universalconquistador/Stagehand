using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using InteropGenerator.Runtime.Attributes;
using Stagehand.Services;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Object = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Object;

namespace Stagehand.Utils;

public static class TerrainHitTesting
{

    // Client::Graphics::Render::TerrainGroundPlate
    //   Client::Graphics::Render::RenderObject
    //     Client::Graphics::ReferencedClassBase
    [Inherits<ReferencedClassBase>]
    [StructLayout(LayoutKind.Explicit, Size = 0x50)]
    private unsafe partial struct TerrainGroundPlate
    {
        [FieldOffset(0x10)] internal byte Unk10;
        [FieldOffset(0x18)] public TerrainGridCoordinates GridCoordinates;
        [FieldOffset(0x20)] public FFXIVClientStructs.FFXIV.Common.Math.Vector3 BoundsCenter;
        [FieldOffset(0x30)] public ModelResourceHandle* ModelResourceHandle;
        [FieldOffset(0x38)] public ushort GridSize;
        [FieldOffset(0x3A)] public ushort LinearGridIndex; // A single index based on GridCoordinates flattened into the terrain renderer's 1D arrays
        [FieldOffset(0x40)] internal void* ModelThingBuffer;
        [FieldOffset(0x48)] internal uint ModelThingBufferByteSize;

        public readonly Vector3 Translation => new(GridSize * (GridCoordinates.TileX + 0.5f), 0.0f, GridSize * (GridCoordinates.TileZ + 0.5f));
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    private struct TerrainWaterPlateConstants;

    // Client::Graphics::Render::TerrainWaterPlate
    //   Client::Graphics::Render::RenderObject
    //     Client::Graphics::ReferencedClassBase
    [Inherits<ReferencedClassBase>]
    [StructLayout(LayoutKind.Explicit, Size = 0x40)]
    private unsafe partial struct TerrainWaterPlate
    {
        [FieldOffset(0x10)] internal byte Unk10;
        [FieldOffset(0x18)] public ushort GridSize;
        [FieldOffset(0x1A)] public TerrainGridCoordinates GridCoordinates;
        [FieldOffset(0x20)] internal ulong Unk20;
        [FieldOffset(0x28)] public ModelResourceHandle* ModelResourceHandle;
        [FieldOffset(0x30)] public ConstantBufferPointer<TerrainWaterPlateConstants> ConstantBuffer;
        [FieldOffset(0x38)] internal byte Unk38;

        public readonly Vector3 Translation => new(GridSize * (GridCoordinates.TileX + 0.5f), 0.0f, GridSize * (GridCoordinates.TileZ + 0.5f));
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x10)]
    private struct TerrainVerticalFogPlateConstants
    {
        [FieldOffset(0x0)] public FFXIVClientStructs.FFXIV.Common.Math.Vector3 Translation;
    }

    // Client::Graphics::Render::TerrainVerticalFogPlate
    //   Client::Graphics::Render::RenderObject
    //     Client::Graphics::ReferencedClassBase
    [Inherits<ReferencedClassBase>]
    [StructLayout(LayoutKind.Explicit, Size = 0x30)]
    private unsafe partial struct TerrainVerticalFogPlate
    {
        [FieldOffset(0x10)] internal byte Unk10;
        [FieldOffset(0x18)] public ushort GridSize;
        [FieldOffset(0x1A)] public TerrainGridCoordinates GridCoordinates;
        [FieldOffset(0x20)] public ModelResourceHandle* ModelResourceHandle;
        [FieldOffset(0x28)] public ConstantBufferPointer<TerrainVerticalFogPlateConstants> ConstantBuffer;

        public readonly Vector3 Translation => new(GridSize * (GridCoordinates.TileX + 0.5f), 0.0f, GridSize * (GridCoordinates.TileZ + 0.5f));
    }

    /// <summary>
    /// Contains many individual terrain tile models
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 0x1F0)] // size probably outdated
    private unsafe struct Terrain__Internal
    {
        [FieldOffset(0x0)] public Terrain Terrain;

        [FieldOffset(0x90)] public TerrainResourceHandle* TerrainResourceHandle;
        [FieldOffset(0x98)] internal ModelResourceHandle** TileModelResourceHandlesPtr;
        [FieldOffset(0xA0)] internal uint TileCount;
        [FieldOffset(0xA4)] internal uint GroundPlateCount;
        [FieldOffset(0xA8)] internal uint WaterPlateCount;
        [FieldOffset(0xAC)] internal uint VerticalFogPlateCount;
        [FieldOffset(0xB0)] internal TerrainGroundPlate** GroundPlatesPtr;
        [FieldOffset(0xB8)] internal TerrainWaterPlate** WaterPlatesPtr;
        [FieldOffset(0xC0)] internal TerrainVerticalFogPlate** VerticalFogPlatesPtr;
        [FieldOffset(0xC8)] internal void** GroundPlateCullingHandlesPtr;
        [FieldOffset(0xD0)] internal void** WaterPlateCullingHandlesPtr;
        [FieldOffset(0xD8)] internal void** VerticalFogPlateCullingHandlesPtr;

        [FieldOffset(0xE0)] internal byte ModelLoadPhase; // 1 -> Load the models with GetResourceAsync next UpdateRender, 2 -> Waiting for plate models to load, and each frame checks the model resource handles until they are loaded.
        //[FieldOffset(0xE1)][FixedSizeArray(isString: true)] internal FixedSizeArray256<byte> _terrainGameFolder; // The folder to read the plate models from
        [FieldOffset(0x1E2)] internal byte EnableGrass;

        public Span<Pointer<ModelResourceHandle>> TileModelResourceHandles => new(TileModelResourceHandlesPtr, (int)TileCount);

        public Span<Pointer<TerrainGroundPlate>> GroundPlates => new(GroundPlatesPtr, (int)GroundPlateCount);
        public Span<Pointer<TerrainWaterPlate>> WaterPlates => new(WaterPlatesPtr, (int)WaterPlateCount);
        public Span<Pointer<TerrainVerticalFogPlate>> VerticalFogPlates => new(VerticalFogPlatesPtr, (int)VerticalFogPlateCount);

        public Span<IntPtr> GroundPlateCullingHandles => new(GroundPlateCullingHandlesPtr, (int)GroundPlateCount);
        public Span<IntPtr> WaterPlateCullingHandles => new(WaterPlateCullingHandlesPtr, (int)WaterPlateCount);
        public Span<IntPtr> VerticalFogPlateCullingHandles => new(VerticalFogPlateCullingHandlesPtr, (int)VerticalFogPlateCount);
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x2A0)]
    private unsafe struct ModelResourceHandle__Internal
    {
        [FieldOffset(0x00)] public ModelResourceHandle Base;

        [FieldOffset(0x160)] public FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds* AxisAlignedBounds;
        [FieldOffset(0x168)] public FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds* TerrainBounds; // If null, AxisAlignedBounds is used instead
        [FieldOffset(0x170)] public FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds* WaterBounds; // If null, AxisAlignedBounds is used instead
        [FieldOffset(0x178)] public FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds* VerticalFogBounds;
    }

    public static unsafe void HitTestTerrain(IModelBvhCacheService modelBvhCacheService, Terrain* obj, Ray ray, ref float nearestDistanceSq, ref Object* nearestObject, ref Vector3 nearestPosition, ref Vector3 nearestHitNormal, bool forceLoad = false)
    {
        var terrain = (Terrain__Internal*)obj;
        for (int i = 0; i < terrain->GroundPlates.Length; i++)
        {
            var chunk = terrain->GroundPlates[i].Value;
            var modelHandle = chunk->ModelResourceHandle;
            if (modelHandle != null && modelHandle->LoadState <= 7 && modelHandle->ReadState == 2)
            {
                var localSpaceStart = (Vector3)ray.Origin - chunk->Translation;
                var localSpaceDirection = ray.Direction;

                FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds modelBounds;
                var modelHandleInternal = (ModelResourceHandle__Internal*)modelHandle;
                if (modelHandleInternal->TerrainBounds != null)
                {
                    modelBounds = *modelHandleInternal->TerrainBounds;
                }
                else
                {
                    modelBounds = *modelHandleInternal->AxisAlignedBounds;
                }
                FFXIVClientStructs.FFXIV.Common.Math.SphereBounds sphereBound = new() { CenterPoint = modelBounds.Center, Radius = modelBounds.HalfExtents.Magnitude };
                if (sphereBound.IntersectsRay(new Ray(localSpaceStart, localSpaceDirection), out _))
                {
                    if (modelBvhCacheService.TryIntersectModel(modelHandle->FileName.ToString(), modpack: null, localSpaceStart, localSpaceDirection, out var intersectionPoint, out var intersectionNormal, forceLoad))
                    {
                        var worldSpaceIntersection = intersectionPoint + chunk->Translation;
                        var worldSpaceNormal = intersectionNormal;

                        var distanceSquared = (worldSpaceIntersection - (Vector3)ray.Origin).LengthSquared();
                        if (distanceSquared < nearestDistanceSq)
                        {
                            nearestDistanceSq = distanceSquared;
                            nearestObject = (Object*)obj;
                            nearestPosition = worldSpaceIntersection;
                            nearestHitNormal = worldSpaceNormal;
                        }
                    }
                }
            }
        }
    }
}
