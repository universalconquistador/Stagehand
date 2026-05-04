using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;

namespace Stagehand.Definitions.ModResources;

/// <summary>
/// How the bytes of data in an embedded mod resource are compressed, if at all.
/// </summary>
public enum ModCompressionScheme
{
    /// <summary>
    /// The data bytes are not compressed.
    /// </summary>
    None = 0,

    /// <summary>
    /// The data bytes are compressed with <see cref="ZLibStream"/>.
    /// </summary>
    Zlib = 1,
}

/// <summary>
/// Provides a resource from data embedded directly in the Stage.
/// </summary>
public class EmbeddedModResourceDefinition : ModResourceDefinition
{
    /// <summary>
    /// The data to use for the resource, compressed with <see cref="CompressionScheme"/>.
    /// </summary>
    public byte[] CompressedDataBytes { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The scheme of how <see cref="CompressedDataBytes"/> is compressed.
    /// </summary>
    public ModCompressionScheme CompressionScheme { get; set; } = ModCompressionScheme.None;

    /// <inheritdoc/>
    public override TResult Visit<TVisitor, TParam, TResult>(ref TParam param)
    {
        return TVisitor.VisitEmbeddedModResourceDefinition(this, ref param);
    }

    /// <summary>
    /// Compresses the given data bytes with the given mod compression scheme.
    /// </summary>
    /// <param name="data">The input data to compress.</param>
    /// <param name="compressionScheme">The type of compression to use.</param>
    /// <returns>The compressed data.</returns>
    public static byte[] CompressDataBytes(byte[] data, ModCompressionScheme compressionScheme)
    {
        if (compressionScheme == ModCompressionScheme.None)
        {
            return data;
        }
        else if (compressionScheme == ModCompressionScheme.Zlib)
        {
            using (var compressedStream = new MemoryStream())
            {
                using (var zlibStream = new ZLibStream(compressedStream, CompressionLevel.SmallestSize))
                {
                    zlibStream.Write(data); // data: 0x34c38
                }
                return compressedStream.ToArray(); // a785
            }
        }
        else
        {
            throw new ArgumentException("Unknown compression scheme!", nameof(compressionScheme));
        }
    }

    /// <summary>
    /// Decompresses the given compressed data bytes using the given compression scheme.
    /// </summary>
    /// <param name="compressedDataBytes">The compressed data bytes to decompress.</param>
    /// <param name="compressionScheme">The type of compression that was used to compress the given compressed data bytes.</param>
    /// <returns>The decompressed data.</returns>
    public static byte[] DecompressDataBytes(byte[] compressedDataBytes, ModCompressionScheme compressionScheme)
    {
        if (compressionScheme == ModCompressionScheme.None)
        {
            return compressedDataBytes;
        }
        else if (compressionScheme == ModCompressionScheme.Zlib)
        {
            using (var stream = new MemoryStream(compressedDataBytes))
            using (var outputStream = new MemoryStream())
            {
                using (var zlibStream = new ZLibStream(stream, CompressionMode.Decompress))
                {
                    zlibStream.CopyTo(outputStream); // a785
                }
                return outputStream.ToArray(); // 2e18f
            }
        }
        else
        {
            throw new ArgumentException("Unknown compression scheme!", nameof(compressionScheme));
        }
    }
}
