// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers;
using System.Runtime.InteropServices;

namespace ZeloraInfo.Infrastructure.Benchmark;

/// <summary>
/// Page-aligned native buffer. Unbuffered (FILE_FLAG_NO_BUFFERING) I/O requires sector-aligned memory,
/// which managed arrays do not guarantee.
/// </summary>
internal sealed unsafe class AlignedBuffer : MemoryManager<byte>
{
    public const int Alignment = 4096;
    private byte* _pointer;
    private readonly int _length;

    public AlignedBuffer(int length)
    {
        _length = length;
        _pointer = (byte*)NativeMemory.AlignedAlloc((nuint)length, Alignment);
        new Span<byte>(_pointer, length).Clear();
    }

    public void FillRandom() => Random.Shared.NextBytes(GetSpan());

    public override Span<byte> GetSpan()
    {
        ObjectDisposedException.ThrowIf(_pointer == null, this);
        return new Span<byte>(_pointer, _length);
    }

    public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

    public override void Unpin()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_pointer != null)
        {
            NativeMemory.AlignedFree(_pointer);
            _pointer = null;
        }
    }
}
