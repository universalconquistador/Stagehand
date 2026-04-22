using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Stagehand.Utils;

// Can we make this as close to a zero-cost abstraction as possible?
public struct Crc32Hasher
{
    private static readonly uint[] CrcTable =
        typeof(Lumina.Misc.Crc32).GetField("CrcTable", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as uint[]
             ?? throw new Exception("Could not fetch CrcTable from Lumina.");

    private uint _value;

    public readonly uint Value => ~_value;

    public Crc32Hasher()
    {
        _value = uint.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(byte value)
    {
        _value = CrcTable[(byte)(_value ^ value)] ^ (_value >> 8);
    }

    public void Advance(ReadOnlySpan<byte> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            _value = CrcTable[(byte)(_value ^ values[i])] ^ (_value >> 8);
        }
    }

    public void Advance<T>(T value)
        where T : unmanaged
    {
        Advance(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
    }

    public void AdvanceASCII(ReadOnlySpan<char> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            _value = CrcTable[(byte)(_value ^ (byte)values[i])] ^ (_value >> 8);
        }
    }
}
