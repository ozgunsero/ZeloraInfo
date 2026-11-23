// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using ZeloraInfo.Core.Hardware;

namespace ZeloraInfo.Infrastructure.Memory;

/// <summary>Pure parsers for the kernel / registry structures used by the memory map (x64 layouts).</summary>
public static class MemoryMapParsers
{
    public const int MemoryListInformationSize = 176; // 5 + 8 + 8 + 1 ULONG_PTR

    /// <summary>SYSTEM_MEMORY_LIST_INFORMATION (page counts) → bytes.</summary>
    public static MemoryLists ParseMemoryListInformation(ReadOnlySpan<byte> data, ulong pageSize)
    {
        if (data.Length < MemoryListInformationSize) return new MemoryLists();
        var values = new ulong[MemoryListInformationSize / 8];
        for (var i = 0; i < values.Length; i++) values[i] = BinaryPrimitives.ReadUInt64LittleEndian(data[(i * 8)..]);
        ulong P(int index) => values[index];
        var priorities = new List<StandbyPriority>(8);
        for (var i = 0; i < 8; i++)
            priorities.Add(new StandbyPriority { Priority = i.ToString(), Bytes = P(5 + i) * pageSize, Repurposed = P(13 + i) * pageSize });
        return new MemoryLists
        {
            Zeroed = P(0) * pageSize,
            Free = P(1) * pageSize,
            Modified = P(2) * pageSize,
            ModifiedNoWrite = P(3) * pageSize,
            Bad = P(4) * pageSize,
            StandbyByPriority = priorities,
        };
    }

    /// <summary>
    /// Walks a SYSTEM_PROCESS_INFORMATION chain returned by NtQuerySystemInformation(SystemProcessInformation).
    /// The buffer must be unmanaged memory because ImageName.Buffer points into it.
    /// </summary>
    public static unsafe List<MemoryProcessInfo> ParseProcesses(IntPtr buffer, int length)
    {
        var list = new List<MemoryProcessInfo>();
        var basePtr = (byte*)buffer;
        var offset = 0;
        while (offset + 232 <= length)
        {
            var p = basePtr + offset;
            var next = *(uint*)p;
            var nameLength = *(ushort*)(p + 56);
            var namePtr = *(IntPtr*)(p + 64);
            var pid = (int)*(long*)(p + 80);
            var name = nameLength > 0 && namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr, nameLength / 2) : pid == 0 ? "Idle" : "";
            list.Add(new MemoryProcessInfo
            {
                ProcessId = pid,
                Name = name ?? "",
                ParentProcessId = (int)*(long*)(p + 88),
                Threads = *(uint*)(p + 4),
                PrivateWorkingSet = (ulong)*(long*)(p + 8),
                Handles = *(uint*)(p + 96),
                SessionId = (int)*(uint*)(p + 100),
                VirtualSize = *(ulong*)(p + 120),
                PageFaults = *(uint*)(p + 128),
                PeakWorkingSet = *(ulong*)(p + 136),
                WorkingSet = *(ulong*)(p + 144),
                PagedPool = *(ulong*)(p + 160),
                NonPagedPool = *(ulong*)(p + 176),
                PrivateBytes = *(ulong*)(p + 184),
            });
            if (next == 0) break;
            offset += (int)next;
        }
        return list;
    }

    /// <summary>
    /// CM_RESOURCE_LIST (REG_RESOURCE_LIST, e.g. HKLM\HARDWARE\RESOURCEMAP\System Resources\Physical Memory\.Translated)
    /// → physical memory ranges. Partial descriptors are 20 bytes (pack 4).
    /// </summary>
    public static List<PhysicalRange> ParseResourceList(ReadOnlySpan<byte> data)
    {
        var ranges = new List<PhysicalRange>();
        if (data.Length < 4) return ranges;
        var fullCount = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var pos = 4;
        for (var f = 0; f < fullCount && pos + 16 <= data.Length; f++)
        {
            // CM_FULL_RESOURCE_DESCRIPTOR: InterfaceType(4) BusNumber(4) Version(2) Revision(2) Count(4)
            var count = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 12)..]);
            pos += 16;
            for (var i = 0; i < count && pos + 20 <= data.Length; i++, pos += 20)
            {
                var type = data[pos];
                var flags = BinaryPrimitives.ReadUInt16LittleEndian(data[(pos + 2)..]);
                var start = BinaryPrimitives.ReadUInt64LittleEndian(data[(pos + 4)..]);
                ulong len = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 12)..]);
                if (type == 7) // CmResourceTypeMemoryLarge
                {
                    if ((flags & 0x200) != 0) len <<= 8;       // CM_RESOURCE_MEMORY_LARGE_40
                    else if ((flags & 0x400) != 0) len <<= 16; // CM_RESOURCE_MEMORY_LARGE_48
                    else if ((flags & 0x800) != 0) len <<= 32; // CM_RESOURCE_MEMORY_LARGE_64
                }
                if (type is 3 or 7 && len > 0) ranges.Add(new PhysicalRange(start, len));
            }
        }
        return ranges.OrderBy(r => r.Start).ToList();
    }
}
