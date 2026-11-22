// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using ZeloraInfo.Core.Hardware;

namespace ZeloraInfo.Infrastructure.Cpu;

public sealed class ProcessorTopologyInfo
{
    public int Packages { get; init; }
    public int Cores { get; init; }
    public int Threads { get; init; }
    public int? PerformanceCores { get; init; }
    public int? EfficiencyCores { get; init; }
    public List<CacheLevelInfo> Caches { get; init; } = [];
}

/// <summary>Reads and parses GetLogicalProcessorInformationEx (works without elevation, on every vendor).</summary>
public static partial class ProcessorTopology
{
    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;
    private const int RelationProcessorPackage = 3;
    private const int RelationAll = 0xFFFF;

    public static ProcessorTopologyInfo Read()
    {
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationAll, IntPtr.Zero, ref length);
        if (length == 0) return new ProcessorTopologyInfo { Packages = 1, Cores = Environment.ProcessorCount, Threads = Environment.ProcessorCount };
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationAll, buffer, ref length))
                return new ProcessorTopologyInfo { Packages = 1, Cores = Environment.ProcessorCount, Threads = Environment.ProcessorCount };
            var bytes = new byte[length];
            Marshal.Copy(buffer, bytes, 0, (int)length);
            return Parse(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Parses a SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX buffer (x64 layout, 16-byte GROUP_AFFINITY).</summary>
    public static ProcessorTopologyInfo Parse(ReadOnlySpan<byte> data)
    {
        int packages = 0, cores = 0, threads = 0;
        var efficiencyClasses = new List<int>();
        var caches = new List<(int Level, CacheKind Kind, long Size, int Assoc, int Line, int Shared)>();

        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            var relationship = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 4)..]);
            if (size <= 0 || offset + size > data.Length) break;
            var rec = data.Slice(offset, size);

            switch (relationship)
            {
                case RelationProcessorCore when size >= 48:
                {
                    cores++;
                    efficiencyClasses.Add(rec[9]);
                    var groupCount = BinaryPrimitives.ReadUInt16LittleEndian(rec[30..]);
                    for (var g = 0; g < groupCount && 32 + g * 16 + 8 <= size; g++)
                        threads += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(rec[(32 + g * 16)..]));
                    break;
                }
                case RelationProcessorPackage:
                    packages++;
                    break;
                case RelationCache when size >= 48:
                {
                    var level = rec[8];
                    var assoc = rec[9];
                    var line = BinaryPrimitives.ReadUInt16LittleEndian(rec[10..]);
                    var cacheSize = BinaryPrimitives.ReadUInt32LittleEndian(rec[12..]);
                    var type = BinaryPrimitives.ReadInt32LittleEndian(rec[16..]);
                    var groupCount = Math.Max((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(rec[38..]));
                    var shared = 0;
                    for (var g = 0; g < groupCount && 40 + g * 16 + 8 <= size; g++)
                        shared += BitOperations.PopCount(BinaryPrimitives.ReadUInt64LittleEndian(rec[(40 + g * 16)..]));
                    var kind = type switch { 1 => CacheKind.Instruction, 2 => CacheKind.Data, 3 => CacheKind.Trace, _ => CacheKind.Unified };
                    caches.Add((level, kind, cacheSize, assoc, line, shared));
                    break;
                }
            }
            offset += size;
        }

        int? pCores = null, eCores = null;
        var distinctClasses = efficiencyClasses.Distinct().ToList();
        if (distinctClasses.Count > 1)
        {
            var max = distinctClasses.Max();
            pCores = efficiencyClasses.Count(c => c == max);
            eCores = efficiencyClasses.Count - pCores;
        }

        var grouped = caches
            .GroupBy(c => (c.Level, c.Kind, c.Size, c.Assoc, c.Line))
            .Select(g => new CacheLevelInfo
            {
                Level = g.Key.Level,
                Kind = g.Key.Kind,
                SizeBytes = g.Key.Size,
                Associativity = g.Key.Assoc,
                LineSizeBytes = g.Key.Line,
                Count = g.Count(),
                SharedByThreads = g.Max(x => x.Shared),
            })
            .OrderBy(c => c.Level).ThenByDescending(c => c.Kind == CacheKind.Data).ThenByDescending(c => c.SizeBytes)
            .ToList();

        return new ProcessorTopologyInfo
        {
            Packages = Math.Max(1, packages),
            Cores = cores == 0 ? Environment.ProcessorCount : cores,
            Threads = threads == 0 ? Environment.ProcessorCount : threads,
            PerformanceCores = pCores,
            EfficiencyCores = eCores,
            Caches = grouped,
        };
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);
}
