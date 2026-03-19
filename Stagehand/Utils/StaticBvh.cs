using Lumina.Data.Files;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Stagehand.Utils;

/// <summary>
/// An immutable Bounding Volume Hierarchy which provides very fast ray-mesh intersection tests in exchange for
/// precomputation time and memory.
/// </summary>
/// <remarks>
/// These are used to provide fast, precise ray hit tests for BgObjects, whose native hit-testing is too
/// inaccurate to support a good UX.
/// </remarks>
public class StaticBvh : IDisposable
{
    // Yoinked from Penumbra.GameData
    public enum VertexType
    {
        Single1 = 0,
        Single2 = 1,
        Single3 = 2,
        Single4 = 3,

        // Unk4  = 4,
        UByte4 = 5,
        Short2 = 6,
        Short4 = 7,
        NByte4 = 8,
        NShort2 = 9,
        NShort4 = 10,

        // Unk11 = 11,
        // Unk12 = 12,
        Half2 = 13,
        Half4 = 14,

        // Unk15 = 15,
        UShort2 = 16,
        UShort4 = 17,
    }

    public enum VertexUsage
    {
        Position = 0,
        BlendWeights = 1,
        BlendIndices = 2,
        Normal = 3,
        UV = 4,
        Tangent2 = 5,
        Tangent1 = 6,
        Color = 7,
    }

    // Implementation based on https://jacco.ompf2.com/2022/04/13/how-to-build-a-bvh-part-1-basics/

    private struct BvhNode
    {
        public Vector3 BoundsMin;
        private uint LeftFirst;
        public Vector3 BoundsMax;
        public uint TriangleCount { get; private set; }


        //public bool IsLeaf => Child0Index == uint.MaxValue && Child1Index == uint.MaxValue;
        public readonly bool IsLeaf => TriangleCount > 0;
        public readonly uint LeftIndex
        {
            get
            {
                Debug.Assert(!IsLeaf);
                return LeftFirst;
            }
        }
        public readonly uint RightIndex => LeftIndex + 1;
        public readonly uint TriangleStart
        {
            get
            {
                Debug.Assert(IsLeaf);
                return LeftFirst;
            }
        }

        public void SetLeaf(uint triangleStart, uint triangleCount)
        {
            LeftFirst = triangleStart;
            TriangleCount = triangleCount;
        }

        public void SetParent(uint leftChildIndex)
        {
            LeftFirst = leftChildIndex;
            TriangleCount = 0;
        }
    }

    private struct BvhVertex
    {
        public Vector3 Position;
    }

    private struct BvhTriangle
    {
        public Vector3 Centroid;

        public uint VertexIndex0;
        public uint VertexIndex1;
        public uint VertexIndex2;
    }

    private readonly struct SizedMemoryOwner<TValue>
    {
        public readonly IMemoryOwner<TValue> MemoryOwner;
        public readonly int Count;

        public SizedMemoryOwner(IMemoryOwner<TValue> memoryOwner, int count)
        {
            MemoryOwner = memoryOwner;
            Count = count;
        }

        public readonly Span<TValue> Span => MemoryOwner.Memory.Span.Slice(0, Count);
    }

    // The max number of nodes ever needed in a recursion stack.
    // This is useful so we can stackalloc recursion stacks and avoid heap allocations.
    private const int RecursionStackSize = 32;

    private SizedMemoryOwner<BvhVertex> _vertexMemory; // Each vertex of the mesh.
    private SizedMemoryOwner<BvhTriangle> _triangleMemory; // Each triangle of the mesh, indexing into vertex memory.
    private SizedMemoryOwner<uint> _triangleRunMemory; // Each run of node triangles, indexing into triangle memory.
    private SizedMemoryOwner<BvhNode> _nodeMemory; // Each BVH node, indexing into itself and triangle run memory.

    public StaticBvh(ReadOnlySpan<byte> vertexBuffer, uint vertexStride, uint positionOffset, ReadOnlySpan<byte> indexBuffer, uint indexSize)
        : this(LoadVertices(vertexBuffer, vertexStride, positionOffset), LoadTriangles(indexBuffer, indexSize))
    { }

    public StaticBvh(MdlFile model)
        : this(LoadModelVertices(model), LoadModelTriangles(model))
    { }

    private static SizedMemoryOwner<BvhVertex> LoadModelVertices(MdlFile model)
    {
        var mainLod = model.Lods[0];

        // Count total vertices
        // See comment below in the triangle function about why we don't just query the meshes for the top lod
        int vertexCount = 0;
        for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];

            vertexCount += mesh.VertexCount;
        }

        var vertexMemory = MemoryPool<BvhVertex>.Shared.Rent(vertexCount);

        var verticesWritten = 0;
        for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];

            var vertexDeclaration = model.VertexDeclarations[meshIndex];

            // Find the position vertex element
            for (int i = 0; i < vertexDeclaration.VertexElements.Length; i++)
            {
                if (vertexDeclaration.VertexElements[i].Usage == (byte)VertexUsage.Position)
                {
                    var streamIndex = vertexDeclaration.VertexElements[i].Stream;

                    ReadOnlySpan<byte> vertexData = model.DataSpan.Slice((int)model.FileHeader.VertexOffset[streamIndex], (int)model.FileHeader.VertexBufferSize[streamIndex]);

                    Func<ReadOnlySpan<byte>, Vector3> positionReader;

                    var type = (VertexType)vertexDeclaration.VertexElements[i].Type;
                    if (type == VertexType.Single3)
                    {
                        positionReader = static span => MemoryMarshal.Read<Vector3>(span);
                    }
                    else if (type == VertexType.Half4)
                    {
                        positionReader = static span =>
                        {
                            var halfs = MemoryMarshal.Cast<byte, Half>(span);
                            return new Vector3((float)halfs[0], (float)halfs[1], (float)halfs[2]);
                        };
                    }
                    else
                    {
                        throw new Exception("Reading only implemented for Single3 and Half4 position attributes!");
                    }

                    var vertexOffset = mesh.VertexBufferOffset[streamIndex];
                    var vertexSize = mesh.VertexBufferStride[streamIndex];

                    for (int vertex = 0; vertex < mesh.VertexCount; vertex++)
                    {
                        vertexMemory.Memory.Span[verticesWritten + vertex].Position = positionReader(vertexData.Slice((int)vertexOffset + vertex * vertexSize + vertexDeclaration.VertexElements[i].Offset));
                    }

                    verticesWritten += mesh.VertexCount;
                }
            }
        }

        return new SizedMemoryOwner<BvhVertex>(vertexMemory, vertexCount);
    }

    private static SizedMemoryOwner<BvhTriangle> LoadModelTriangles(MdlFile model)
    {
        var mainLod = model.Lods[0];

        ReadOnlySpan<byte> indexData = model.DataSpan.Slice((int)mainLod.IndexDataOffset, (int)mainLod.IndexBufferSize);
        ReadOnlySpan<ushort> indices = MemoryMarshal.Cast<byte, ushort>(indexData);

        var triangleMemory = MemoryPool<BvhTriangle>.Shared.Rent(indices.Length / 3);

        int baseVertex = 0;
        int baseTriangle = 0;
        for (int meshIndex = 0; meshIndex < model.Meshes.Length; meshIndex++)
        {
            var mesh = model.Meshes[meshIndex];

            // HACK:
            // This whole thing is very wonky, based on two conflicting observations:
            //  - Some meshes don't seem to have any main submeshes but still have vertex/index data, and we want that
            //  - Some meshes have multiple LODs, and we don't want those extra LODs
            // So, we go through each mesh's index range without looking at submeshes or LODs, but once we've read
            // enough indices to fill the first LOD's index count we stop.
            if (baseTriangle >= indices.Length / 3)
            {
                break;
            }
            
            for (int triangleIndex = 0; triangleIndex < mesh.IndexCount / 3; triangleIndex++)
            {
                triangleMemory.Memory.Span[baseTriangle + triangleIndex] = new()
                {
                    // Indices are relative to the mesh's StartIndex
                    VertexIndex0 = (uint)(baseVertex + indices[(int)mesh.StartIndex + triangleIndex * 3]),
                    VertexIndex1 = (uint)(baseVertex + indices[(int)mesh.StartIndex + triangleIndex * 3 + 1]),
                    VertexIndex2 = (uint)(baseVertex + indices[(int)mesh.StartIndex + triangleIndex * 3 + 2]),
                };
            }

            baseVertex += mesh.VertexCount;
            baseTriangle += (int)mesh.IndexCount / 3;
        }

        return new SizedMemoryOwner<BvhTriangle>(triangleMemory, indices.Length / 3);
    }

    private static unsafe SizedMemoryOwner<BvhVertex> LoadVertices(ReadOnlySpan<byte> vertexBuffer, uint vertexStride, uint positionOffset)
    {
        // Load vertex data
        int vertexCount = (int)(vertexBuffer.Length / vertexStride);
        var vertexMemory = MemoryPool<BvhVertex>.Shared.Rent(vertexCount);
        for (int i = 0; i < vertexCount; i++)
        {
            vertexMemory.Memory.Span[i].Position = MemoryMarshal.Read<Vector3>(vertexBuffer.Slice((int)(i * vertexStride + positionOffset), sizeof(Vector3)));
        }
        return new SizedMemoryOwner<BvhVertex>(vertexMemory, vertexCount);
    }

    private static SizedMemoryOwner<BvhTriangle> LoadTriangles(ReadOnlySpan<byte> indexBuffer, uint indexSize)
    {
        // Load triangle data
        int indexCount = (int)(indexBuffer.Length / indexSize);
        int triangleCount = indexCount / 3;
        var triangleMemory = MemoryPool<BvhTriangle>.Shared.Rent(triangleCount);
        Func<ReadOnlySpan<byte>, uint> indexReader;
        if (indexSize == 1)
        {
            indexReader = static span => span[0];
        }
        else if (indexSize == 2)
        {
            indexReader = static span => MemoryMarshal.Read<ushort>(span);
        }
        else if (indexSize == 4)
        {
            indexReader = static span => MemoryMarshal.Read<uint>(span);
        }
        else
        {
            throw new ArgumentException("Unsupported index size!", nameof(indexSize));
        }
        for (int i = 0; i < triangleCount; i++)
        {
            uint index0 = indexReader.Invoke(indexBuffer.Slice((int)(i * indexSize * 3), (int)indexSize));
            uint index1 = indexReader.Invoke(indexBuffer.Slice((int)(i * indexSize * 3 + indexSize), (int)indexSize));
            uint index2 = indexReader.Invoke(indexBuffer.Slice((int)(i * indexSize * 3 + indexSize * 2), (int)indexSize));
            triangleMemory.Memory.Span[i] = new()
            {
                VertexIndex0 = index0,
                VertexIndex1 = index1,
                VertexIndex2 = index2,
            };
        }

        return new SizedMemoryOwner<BvhTriangle>(triangleMemory, triangleCount);
    }

    /// <summary>
    /// Creates a new static BVH from the given buffer of interleaved vertex data and the given buffer of
    /// triangle list indices.
    /// </summary>
    /// <param name="vertexBuffer"></param>
    /// <param name="vertexStride"></param>
    /// <param name="positionOffset"></param>
    /// <param name="indexBuffer"></param>
    /// <param name="indexSize"></param>
    private StaticBvh(SizedMemoryOwner<BvhVertex> vertexMemory, SizedMemoryOwner<BvhTriangle> triangleMemory)
    {
        _vertexMemory = vertexMemory;
        _triangleMemory = triangleMemory;

        // Compute triangle centroids
        for (int i = 0; i < triangleMemory.Count; i++)
        {
            ref BvhTriangle triangle = ref _triangleMemory.Span[i];
            triangle.Centroid = (_vertexMemory.Span[(int)triangle.VertexIndex0].Position + _vertexMemory.Span[(int)triangle.VertexIndex1].Position + _vertexMemory.Span[(int)triangle.VertexIndex2].Position) / 3.0f;
        }

        // Initialize the triangle runs with a single run of all triangles for the root node
        _triangleRunMemory = new SizedMemoryOwner<uint>(MemoryPool<uint>.Shared.Rent(triangleMemory.Count), triangleMemory.Count);
        for (int i = 0; i < triangleMemory.Count; i++)
        {
            _triangleRunMemory.Span[i] = (uint)i;
        }

        // Initialize a single root node with all triangles
        int nodeMaxCount = triangleMemory.Count * 2 - 1; // Worst case: one leaf node per triangle, then all the non-leaf nodes
        _nodeMemory = new SizedMemoryOwner<BvhNode>(MemoryPool<BvhNode>.Shared.Rent(nodeMaxCount), nodeMaxCount);
        uint nodeCount = 1;
        const int rootNodeIndex = 0;
        _nodeMemory.Span[rootNodeIndex] = new BvhNode();
        _nodeMemory.Span[rootNodeIndex].SetLeaf(0, (uint)triangleMemory.Count);
        UpdateNodeBounds(rootNodeIndex);

        // Subdivide into a hierarchy
        Stack<int> subdivideStack = new Stack<int>(RecursionStackSize);
        subdivideStack.Push(rootNodeIndex);
        while (subdivideStack.TryPop(out int nodeIndex))
        {
            ref BvhNode node = ref _nodeMemory.Span[nodeIndex];
            Debug.Assert(node.IsLeaf);

            // Determine split axis
            Vector3 boundsSize = node.BoundsMax - node.BoundsMin;
            var splitAxis = 0;
            if (boundsSize.Y > boundsSize.X)
            {
                splitAxis = 1;
            }
            if (boundsSize.Z > boundsSize[splitAxis])
            {
                splitAxis = 2;
            }

            float splitValue = node.BoundsMin[splitAxis] + boundsSize[splitAxis] * 0.5f;

            // Split triangles
            int i = (int)node.TriangleStart;
            int j = i + (int)node.TriangleCount - 1;
            while (i <= j)
            {
                ref readonly BvhTriangle triangle = ref _triangleMemory.Span[(int)_triangleRunMemory.Span[i]];
                if (triangle.Centroid[splitAxis] < splitValue)
                {
                    // Can stay in first child
                    i += 1;
                }
                else
                {
                    // Swap triangles i and j
                    var k = _triangleRunMemory.Span[j];
                    _triangleRunMemory.Span[j] = _triangleRunMemory.Span[i];
                    _triangleRunMemory.Span[i] = k;

                    j -= 1;
                }
            }

            int leftCount = i - (int)node.TriangleStart;
            // If there are some triangles on each side of the partition, split into child nodes
            if (leftCount != 0 && leftCount != node.TriangleCount)
            {
                uint firstChildIndex = nodeCount;
                nodeCount += 2;

                _nodeMemory.Span[(int)firstChildIndex].SetLeaf(node.TriangleStart, (uint)leftCount);
                _nodeMemory.Span[(int)firstChildIndex + 1].SetLeaf(node.TriangleStart + (uint)leftCount, node.TriangleCount - (uint)leftCount);

                node.SetParent(firstChildIndex);

                UpdateNodeBounds(firstChildIndex);
                UpdateNodeBounds(firstChildIndex + 1);

                subdivideStack.Push((int)firstChildIndex);
                subdivideStack.Push((int)firstChildIndex + 1);

                Debug.Assert(subdivideStack.Count <= RecursionStackSize);
            }
        }
    }

    private void UpdateNodeBounds(uint nodeIndex)
    {
        ref BvhNode node = ref _nodeMemory.Span[(int)nodeIndex];
        node.BoundsMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        node.BoundsMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < node.TriangleCount; i++)
        {
            ref readonly BvhTriangle triangle = ref _triangleMemory.Span[(int)_triangleRunMemory.Span[(int)node.TriangleStart + i]];
            node.BoundsMin = Vector3.Min(
                Vector3.Min(
                    Vector3.Min(node.BoundsMin, _vertexMemory.Span[(int)triangle.VertexIndex0].Position),
                    _vertexMemory.Span[(int)triangle.VertexIndex1].Position),
                _vertexMemory.Span[(int)triangle.VertexIndex2].Position);

            node.BoundsMax = Vector3.Max(
                Vector3.Max(
                    Vector3.Max(node.BoundsMax, _vertexMemory.Span[(int)triangle.VertexIndex0].Position),
                    _vertexMemory.Span[(int)triangle.VertexIndex1].Position),
                _vertexMemory.Span[(int)triangle.VertexIndex2].Position);
        }
    }

    public bool IntersectsRay(Vector3 rayStart, Vector3 rayDirection, out Vector3 intersectionPoint, out Vector3 intersectionNormal)
    {
        int hitIndex = -1;
        float minT = float.MaxValue;

        Span<uint> traceStack = stackalloc uint[RecursionStackSize];

        // Push the root bvh node
        traceStack[0] = 0;
        int traceStackCount = 1;

        while (traceStackCount > 0)
        {
            // Pop node from stack
            uint nodeIndex = traceStack[traceStackCount - 1];
            traceStackCount -= 1;
            ref readonly BvhNode node = ref _nodeMemory.Span[(int)nodeIndex];

            if (IntersectsBounds(rayStart, rayDirection, node.BoundsMin, node.BoundsMax, minT))
            {
                if (node.IsLeaf)
                {
                    for (int triangleIndexIndex = (int)node.TriangleStart; triangleIndexIndex < node.TriangleStart + node.TriangleCount; triangleIndexIndex++)
                    {
                        int triangleIndex = (int)_triangleRunMemory.Span[triangleIndexIndex];
                        if (IntersectsTriangle(rayStart, rayDirection, triangleIndex, out float hitT) && hitT < minT)
                        {
                            minT = hitT;
                            hitIndex = triangleIndex;
                        }
                    }
                }
                else
                {
                    traceStack[traceStackCount] = node.LeftIndex;
                    traceStack[traceStackCount + 1] = node.RightIndex;
                    traceStackCount += 2;
                }
            }
        }

        if (hitIndex >= 0)
        {
            intersectionPoint = rayStart + rayDirection * minT;
            ref readonly BvhTriangle closestTriangle = ref _triangleMemory.Span[(int)hitIndex];
            Vector3 p0 = _vertexMemory.Span[(int)closestTriangle.VertexIndex0].Position;
            Vector3 p1 = _vertexMemory.Span[(int)closestTriangle.VertexIndex1].Position;
            Vector3 p2 = _vertexMemory.Span[(int)closestTriangle.VertexIndex2].Position;
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
            intersectionNormal = normal * MathF.Sign(Vector3.Dot(normal, -rayDirection));
        }
        else
        {
            intersectionPoint = Vector3.Zero;
            intersectionNormal = Vector3.Zero;
        }

        return hitIndex >= 0;
    }

    private bool IntersectsBounds(Vector3 rayStart, Vector3 rayDirection, Vector3 boundsMin, Vector3 boundsMax, float maxt)
    {
        Vector3 t1 = (boundsMin - rayStart) / rayDirection;
        Vector3 t2 = (boundsMax - rayStart) / rayDirection;
        Vector3 mins = Vector3.Min(t1, t2);
        Vector3 maxes = Vector3.Max(t1, t2);
        float tmin = float.Max(float.Max(mins.X, mins.Y), mins.Z);
        float tmax = float.Min(float.Min(maxes.X, maxes.Y), maxes.Z);
        return tmax >= tmin
            && tmax > 0.0f // box is not 100% behind ray origin
            && tmin < maxt; // box is not too far away
    }

    private bool IntersectsTriangle(Vector3 rayStart, Vector3 rayDirection, int triangleIndex, out float t)
    {
        ref readonly BvhTriangle triangle = ref _triangleMemory.Span[triangleIndex];
        ref readonly Vector3 p0 = ref _vertexMemory.Span[(int)triangle.VertexIndex0].Position;
        ref readonly Vector3 p1 = ref _vertexMemory.Span[(int)triangle.VertexIndex1].Position;
        ref readonly Vector3 p2 = ref _vertexMemory.Span[(int)triangle.VertexIndex2].Position;

        Vector3 edge1 = p1 - p0;
        Vector3 edge2 = p2 - p0;
        Vector3 h = Vector3.Cross(rayDirection, edge2);
        float a = Vector3.Dot(edge1, h);
        if (a > -0.0001f && a < 0.0001f)
        {
            t = 0.0f;
            // Ray parallel to triangle
            return false;
        }

        float f = 1.0f / a;
        Vector3 s = rayStart - p0;
        float u = f * Vector3.Dot(s, h);
        if (u < 0.0f || u > 1.0f)
        {
            t = 0.0f;
            return false;
        }

        Vector3 q = Vector3.Cross(s, edge1);
        float v = f * Vector3.Dot(rayDirection, q);
        if (v < 0.0f || u + v > 1.0f)
        {
            t = 0.0f;
            return false;
        }

        t = f * Vector3.Dot(edge2, q);
        if (t < 0.0f)
        {
            return false;
        }

        return true;
    }

    public void Dispose()
    {
        _nodeMemory.MemoryOwner.Dispose();
        _triangleRunMemory.MemoryOwner.Dispose();
        _triangleMemory.MemoryOwner.Dispose();
        _vertexMemory.MemoryOwner.Dispose();
    }
}
