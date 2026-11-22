// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Runtime.Intrinsics.X86;
using System.Text;

namespace ZeloraInfo.Infrastructure.Cpu;

public readonly record struct CpuIdRegs(uint Eax, uint Ebx, uint Ecx, uint Edx);

public readonly record struct CpuSignature(int Family, int Model, int Stepping, int BaseFamily, int BaseModel, int ExtFamily, int ExtModel);

/// <summary>
/// CPUID access via <see cref="X86Base.CpuId"/> (user mode, no driver) plus pure decoding helpers.
/// </summary>
public static class CpuIdDecoder
{
    public static bool IsSupported => X86Base.IsSupported;

    public static CpuIdRegs Query(uint leaf, uint subLeaf = 0)
    {
        if (!X86Base.IsSupported) return default;
        var (a, b, c, d) = X86Base.CpuId((int)leaf, (int)subLeaf);
        return new CpuIdRegs((uint)a, (uint)b, (uint)c, (uint)d);
    }

    public static string ReadVendor()
    {
        var r = Query(0);
        return Ascii(r.Ebx, r.Edx, r.Ecx);
    }

    public static string ReadBrand()
    {
        if (Query(0x80000000).Eax < 0x80000004) return "";
        var sb = new StringBuilder();
        for (uint leaf = 0x80000002; leaf <= 0x80000004; leaf++)
        {
            var r = Query(leaf);
            sb.Append(Ascii(r.Eax, r.Ebx, r.Ecx, r.Edx));
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString().Trim('\0', ' '), @"\s+", " ");
    }

    /// <summary>Decodes CPUID.01h:EAX into the displayed family / model / stepping (Intel SDM / AMD APM rules).</summary>
    public static CpuSignature DecodeSignature(uint eax)
    {
        var stepping = (int)(eax & 0xF);
        var baseModel = (int)((eax >> 4) & 0xF);
        var baseFamily = (int)((eax >> 8) & 0xF);
        var extModel = (int)((eax >> 16) & 0xF);
        var extFamily = (int)((eax >> 20) & 0xFF);
        var family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
        var model = baseFamily is 0x6 or 0xF ? (extModel << 4) + baseModel : baseModel;
        return new CpuSignature(family, model, stepping, baseFamily, baseModel, extFamily, extModel);
    }

    /// <summary>Instruction-set extensions advertised by CPUID leaves 01h, 07h and 80000001h.</summary>
    public static List<string> DecodeInstructions(CpuIdRegs leaf1, CpuIdRegs leaf7, CpuIdRegs leaf7Sub1, CpuIdRegs ext1, bool isAmd)
    {
        var list = new List<string>();
        void Add(bool present, string name)
        {
            if (present) list.Add(name);
        }
        static bool Bit(uint reg, int bit) => ((reg >> bit) & 1) != 0;

        Add(Bit(leaf1.Edx, 23), "MMX");
        Add(Bit(leaf1.Edx, 25), "SSE");
        Add(Bit(leaf1.Edx, 26), "SSE2");
        Add(Bit(leaf1.Ecx, 0), "SSE3");
        Add(Bit(leaf1.Ecx, 9), "SSSE3");
        Add(Bit(leaf1.Ecx, 19), "SSE4.1");
        Add(Bit(leaf1.Ecx, 20), "SSE4.2");
        Add(Bit(ext1.Ecx, 6), "SSE4A");
        Add(Bit(ext1.Edx, 29), "x86-64");
        Add(Bit(ext1.Edx, 20), "NX");
        Add(Bit(leaf1.Ecx, 5), "VT-x");
        Add(isAmd && Bit(ext1.Ecx, 2), "AMD-V");
        Add(Bit(leaf1.Ecx, 25), "AES");
        Add(Bit(leaf1.Ecx, 1), "PCLMULQDQ");
        Add(Bit(leaf1.Ecx, 28), "AVX");
        Add(Bit(leaf7.Ebx, 5), "AVX2");
        Add(Bit(leaf7Sub1.Eax, 4), "AVX-VNNI");
        Add(Bit(leaf7.Ebx, 16), "AVX-512F");
        Add(Bit(leaf7.Ebx, 17), "AVX-512DQ");
        Add(Bit(leaf7.Ebx, 28), "AVX-512CD");
        Add(Bit(leaf7.Ebx, 30), "AVX-512BW");
        Add(Bit(leaf7.Ebx, 31), "AVX-512VL");
        Add(Bit(leaf7.Ebx, 21), "AVX-512IFMA");
        Add(Bit(leaf7.Ecx, 1), "AVX-512VBMI");
        Add(Bit(leaf7.Ecx, 11), "AVX-512VNNI");
        Add(Bit(leaf7.Edx, 23), "AVX-512FP16");
        Add(Bit(leaf1.Ecx, 12), "FMA3");
        Add(Bit(ext1.Ecx, 16), "FMA4");
        Add(Bit(leaf1.Ecx, 29), "F16C");
        Add(Bit(leaf7.Ebx, 3), "BMI1");
        Add(Bit(leaf7.Ebx, 8), "BMI2");
        Add(Bit(ext1.Ecx, 5), "LZCNT");
        Add(Bit(leaf1.Ecx, 23), "POPCNT");
        Add(Bit(leaf7.Ebx, 19), "ADX");
        Add(Bit(leaf7.Ebx, 29), "SHA");
        Add(Bit(leaf7.Ecx, 9), "VAES");
        Add(Bit(leaf7.Ecx, 8), "GFNI");
        Add(Bit(leaf1.Ecx, 30), "RDRAND");
        Add(Bit(leaf7.Ebx, 18), "RDSEED");
        Add(Bit(leaf1.Ecx, 22), "MOVBE");
        Add(Bit(leaf1.Ecx, 13), "CX16");
        Add(Bit(leaf7.Edx, 24), "AMX-TILE");
        Add(Bit(leaf7.Edx, 22), "AMX-BF16");
        Add(Bit(leaf7.Edx, 25), "AMX-INT8");
        Add(Bit(leaf7.Ebx, 11), "TSX");
        return list;
    }

    public static bool HypervisorPresent(CpuIdRegs leaf1) => ((leaf1.Ecx >> 31) & 1) != 0;

    private static string Ascii(params uint[] regs)
    {
        var bytes = new byte[regs.Length * 4];
        for (var i = 0; i < regs.Length; i++) BitConverter.GetBytes(regs[i]).CopyTo(bytes, i * 4);
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
    }
}
