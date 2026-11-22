// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeloraInfo.Infrastructure.Memory;

public sealed class SmbiosStructure(byte type, ushort handle, byte[] formatted, IReadOnlyList<string> strings)
{
    public byte Type { get; } = type;
    public ushort Handle { get; } = handle;
    public byte[] Formatted { get; } = formatted;
    public IReadOnlyList<string> Strings { get; } = strings;
    public int Length => Formatted.Length;

    public bool Has(int offset, int size) => offset + size <= Formatted.Length;
    public byte Byte(int offset) => Has(offset, 1) ? Formatted[offset] : (byte)0;
    public ushort Word(int offset) => Has(offset, 2) ? BinaryPrimitives.ReadUInt16LittleEndian(Formatted.AsSpan(offset)) : (ushort)0;
    public uint DWord(int offset) => Has(offset, 4) ? BinaryPrimitives.ReadUInt32LittleEndian(Formatted.AsSpan(offset)) : 0;
    public ulong QWord(int offset) => Has(offset, 8) ? BinaryPrimitives.ReadUInt64LittleEndian(Formatted.AsSpan(offset)) : 0;

    public string Str(int offset)
    {
        var index = Byte(offset);
        return index == 0 || index > Strings.Count ? "" : Strings[index - 1].Trim();
    }
}

public sealed record SmbiosTable(byte MajorVersion, byte MinorVersion, IReadOnlyList<SmbiosStructure> Structures)
{
    public string Version => $"{MajorVersion}.{MinorVersion}";
}

/// <summary>Reads the raw SMBIOS table with GetSystemFirmwareTable('RSMB') — no administrator rights required.</summary>
public static partial class SmbiosParser
{
    private const uint RSMB = 0x52534D42;

    public static SmbiosTable? ReadFromFirmware()
    {
        try
        {
            var size = GetSystemFirmwareTable(RSMB, 0, null, 0);
            if (size == 0) return null;
            var buffer = new byte[size];
            if (GetSystemFirmwareTable(RSMB, 0, buffer, size) != size) return null;
            return ParseRaw(buffer);
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Parses a RawSMBIOSData blob (8-byte header followed by the structure table).</summary>
    public static SmbiosTable ParseRaw(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 8) return new SmbiosTable(0, 0, []);
        var major = raw[1];
        var minor = raw[2];
        var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]), (uint)(raw.Length - 8));
        return new SmbiosTable(major, minor, ParseTable(raw.Slice(8, length)));
    }

    public static List<SmbiosStructure> ParseTable(ReadOnlySpan<byte> table)
    {
        var list = new List<SmbiosStructure>();
        var pos = 0;
        while (pos + 4 <= table.Length)
        {
            var type = table[pos];
            var length = table[pos + 1];
            if (length < 4 || pos + length > table.Length) break;
            var handle = BinaryPrimitives.ReadUInt16LittleEndian(table[(pos + 2)..]);
            var formatted = table.Slice(pos, length).ToArray();

            // String set: NUL-terminated strings, terminated by an extra NUL (or a double NUL if empty).
            var strings = new List<string>();
            var s = pos + length;
            if (s + 1 < table.Length && table[s] == 0 && table[s + 1] == 0)
            {
                s += 2;
            }
            else
            {
                while (s < table.Length)
                {
                    var end = table[s..].IndexOf((byte)0);
                    if (end < 0) { s = table.Length; break; }
                    strings.Add(Encoding.ASCII.GetString(table.Slice(s, end)));
                    s += end + 1;
                    if (s < table.Length && table[s] == 0) { s++; break; }
                }
            }

            list.Add(new SmbiosStructure(type, handle, formatted, strings));
            if (type == 127) break; // End-of-table
            pos = s;
        }
        return list;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, [Out] byte[]? buffer, uint bufferSize);
}
