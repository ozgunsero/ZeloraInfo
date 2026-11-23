// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using ZeloraInfo.Core.Hardware;
using ZeloraInfo.Core.Settings;
using ZeloraInfo.Infrastructure.Memory;

namespace ZeloraInfo.Tests;

public class MemoryMapTests
{
    [Fact]
    public void ParsesMemoryListInformation()
    {
        var data = new byte[MemoryMapParsers.MemoryListInformationSize];
        void Set(int index, ulong pages) => BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(index * 8), pages);
        Set(0, 100);   // zeroed
        Set(1, 50);    // free
        Set(2, 25);    // modified
        Set(3, 5);     // modified no-write
        Set(4, 1);     // bad
        for (var p = 0; p < 8; p++)
        {
            Set(5 + p, (ulong)(p + 1) * 10);   // standby by priority
            Set(13 + p, (ulong)p);             // repurposed
        }

        var lists = MemoryMapParsers.ParseMemoryListInformation(data, 4096);

        Assert.Equal(100UL * 4096, lists.Zeroed);
        Assert.Equal(50UL * 4096, lists.Free);
        Assert.Equal(150UL * 4096, lists.FreeAndZeroed);
        Assert.Equal(25UL * 4096, lists.Modified);
        Assert.Equal(5UL * 4096, lists.ModifiedNoWrite);
        Assert.Equal(4096UL, lists.Bad);
        Assert.Equal(8, lists.StandbyByPriority.Count);
        Assert.Equal("7", lists.StandbyByPriority[7].Priority);
        Assert.Equal(80UL * 4096, lists.StandbyByPriority[7].Bytes);
        Assert.Equal(7UL * 4096, lists.StandbyByPriority[7].Repurposed);
        Assert.Equal(360UL * 4096, lists.Standby); // 10 + 20 + … + 80
    }

    [Fact]
    public void ShortMemoryListBufferGivesEmptyLists()
        => Assert.Empty(MemoryMapParsers.ParseMemoryListInformation(new byte[16], 4096).StandbyByPriority);

    [Fact]
    public void ParsesPhysicalMemoryResourceList()
    {
        // CM_RESOURCE_LIST with one full descriptor and three partial descriptors (20 bytes each, pack 4)
        var data = new byte[4 + 16 + 3 * 20];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4 + 12), 3);
        void Partial(int i, byte type, ushort flags, ulong start, uint length)
        {
            var o = 20 + i * 20;
            data[o] = type;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(o + 2), flags);
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(o + 4), start);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(o + 12), length);
        }
        Partial(0, 3, 0, 0x100000, 0x09A00000);                   // plain memory
        Partial(1, 7, 0x200, 0x1_0000_0000, 0x0073_F380);         // MemoryLarge, length40 (<< 8)
        Partial(2, 1, 0, 0x3F8, 8);                               // I/O port → ignored

        var ranges = MemoryMapParsers.ParseResourceList(data);

        Assert.Equal(2, ranges.Count);
        Assert.Equal(new PhysicalRange(0x100000, 0x09A00000), ranges[0]);
        Assert.Equal(0x1_0000_0000UL, ranges[1].Start);
        Assert.Equal(0x0073_F380UL << 8, ranges[1].Length);
    }

    [Fact]
    public unsafe void ParsesSystemProcessInformationChain()
    {
        const int entry = 256;
        var size = entry * 2 + 64;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            new Span<byte>((void*)buffer, size).Clear();
            var name = Encoding.Unicode.GetBytes("explorer.exe");
            var nameAddress = buffer + entry * 2;
            Marshal.Copy(name, 0, nameAddress, name.Length);

            void Write(int index, uint next, long pid, ushort nameLength, IntPtr namePtr, long privateWs, ulong ws, ulong privateBytes)
            {
                var p = (byte*)buffer + index * entry;
                *(uint*)p = next;
                *(uint*)(p + 4) = 42;                    // threads
                *(long*)(p + 8) = privateWs;
                *(ushort*)(p + 56) = nameLength;
                *(IntPtr*)(p + 64) = namePtr;
                *(long*)(p + 80) = pid;
                *(long*)(p + 88) = 4;                    // parent
                *(uint*)(p + 96) = 1234;                 // handles
                *(uint*)(p + 100) = 1;                   // session
                *(uint*)(p + 128) = 999;                 // page faults
                *(ulong*)(p + 144) = ws;
                *(ulong*)(p + 160) = 4096;               // paged pool
                *(ulong*)(p + 176) = 2048;               // non-paged pool
                *(ulong*)(p + 184) = privateBytes;
            }
            Write(0, entry, 0, 0, IntPtr.Zero, 0, 8192, 0);
            Write(1, 0, 4321, (ushort)name.Length, nameAddress, 100 << 20, 150UL << 20, 120UL << 20);

            var list = MemoryMapParsers.ParseProcesses(buffer, size);

            Assert.Equal(2, list.Count);
            Assert.Equal("Idle", list[0].Name);
            var explorer = list[1];
            Assert.Equal("explorer.exe", explorer.Name);
            Assert.Equal(4321, explorer.ProcessId);
            Assert.Equal(4, explorer.ParentProcessId);
            Assert.Equal(150UL << 20, explorer.WorkingSet);
            Assert.Equal(100UL << 20, explorer.PrivateWorkingSet);
            Assert.Equal(50UL << 20, explorer.SharedWorkingSet);
            Assert.Equal(120UL << 20, explorer.PrivateBytes);
            Assert.Equal(1234u, explorer.Handles);
            Assert.Equal(42u, explorer.Threads);
            Assert.Equal(999u, explorer.PageFaults);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [Fact]
    public void SnapshotDerivesInUseAndHardwareReserved()
    {
        var s = new MemoryMapSnapshot
        {
            InstalledBytes = 32UL << 30,
            TotalPhysicalBytes = (32UL << 30) - (100UL << 20),
            Lists = new MemoryLists
            {
                Free = 1UL << 30,
                Modified = 1UL << 29,
                StandbyByPriority = [new StandbyPriority { Priority = "2–5", Bytes = 10UL << 30 }],
            },
        };
        Assert.Equal(100UL << 20, s.HardwareReservedBytes);
        Assert.Equal(s.TotalPhysicalBytes - (1UL << 30) - (1UL << 29) - (10UL << 30), s.InUseBytes);
    }

    [Fact]
    public void LegacyRamDetailsStartPageMapsToMemory()
    {
        var settings = AppSettings.CreateDefault();
        settings.General.StartPage = "RamDetails";
        settings.Normalize();
        Assert.Equal("Memory", settings.General.StartPage);
    }
}
