// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeloraInfo.Infrastructure.Gpu;

/// <summary>
/// Minimal user-mode access to DXGI, D3D12 and the WDDM kernel thunks (D3DKMT*) through raw COM vtables —
/// no third-party wrapper, no driver, no elevation.
/// </summary>
internal static unsafe partial class GpuInterop
{
    // ------------------------------------------------------------------ DXGI

    private static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid IID_ID3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");

    public sealed record DxgiAdapter(IntPtr Pointer, string Description, uint VendorId, uint DeviceId, uint SubSysId, uint Revision,
        ulong DedicatedVideoMemory, ulong DedicatedSystemMemory, ulong SharedSystemMemory, long Luid, bool IsSoftware, string? UmdVersion);

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(in Guid riid, out IntPtr factory);

    [LibraryImport("d3d12.dll")]
    private static partial int D3D12CreateDevice(IntPtr adapter, int minimumFeatureLevel, in Guid riid, out IntPtr device);

    private static void** Vtbl(IntPtr obj) => *(void***)obj;

    public static void Release(IntPtr obj)
    {
        if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vtbl(obj)[2])(obj);
    }

    /// <summary>Enumerates adapters. Caller must <see cref="Release"/> every returned pointer.</summary>
    public static List<DxgiAdapter> EnumerateAdapters()
    {
        var list = new List<DxgiAdapter>();
        if (CreateDXGIFactory1(IID_IDXGIFactory1, out var factory) < 0 || factory == IntPtr.Zero) return list;
        try
        {
            var enumAdapters1 = (delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vtbl(factory)[12];
            for (uint i = 0; i < 16; i++)
            {
                IntPtr adapter;
                if (enumAdapters1(factory, i, &adapter) < 0) break; // DXGI_ERROR_NOT_FOUND
                var desc = stackalloc byte[312];
                var getDesc1 = (delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Vtbl(adapter)[10];
                if (getDesc1(adapter, desc) < 0)
                {
                    Release(adapter);
                    continue;
                }
                var span = new ReadOnlySpan<byte>(desc, 312);
                var name = Encoding.Unicode.GetString(span[..256]).TrimEnd('\0').Trim();
                var luidLow = BinaryPrimitives.ReadUInt32LittleEndian(span[296..]);
                var luidHigh = BinaryPrimitives.ReadInt32LittleEndian(span[300..]);
                var flags = BinaryPrimitives.ReadUInt32LittleEndian(span[304..]);

                // UMD version via CheckInterfaceSupport(IDXGIDevice): returns the D3D10/11 user-mode driver version.
                string? umd = null;
                var iid = IID_IDXGIDevice;
                long version;
                var check = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, long*, int>)Vtbl(adapter)[9];
                if (check(adapter, &iid, &version) >= 0)
                    umd = $"{(version >> 48) & 0xFFFF}.{(version >> 32) & 0xFFFF}.{(version >> 16) & 0xFFFF}.{version & 0xFFFF}";

                list.Add(new DxgiAdapter(adapter, name,
                    BinaryPrimitives.ReadUInt32LittleEndian(span[256..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[260..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[264..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[268..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(span[272..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(span[280..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(span[288..]),
                    ((long)luidHigh << 32) | luidLow,
                    (flags & 0x2) != 0, // DXGI_ADAPTER_FLAG_SOFTWARE
                    umd));
            }
        }
        finally
        {
            Release(factory);
        }
        return list;
    }

    // ------------------------------------------------------------------ D3D12

    public sealed record D3D12Caps(int MaxFeatureLevel, int ShaderModel, int RaytracingTier, int MeshShaderTier, int VrsTier,
        int ResourceBindingTier, bool? Uma, int NodeCount);

    public static D3D12Caps? QueryD3D12(IntPtr adapter)
    {
        IntPtr device;
        try
        {
            if (D3D12CreateDevice(adapter, 0xB000, IID_ID3D12Device, out device) < 0 || device == IntPtr.Zero) return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        try
        {
            var vt = Vtbl(device);
            var getNodeCount = (delegate* unmanaged[Stdcall]<IntPtr, uint>)vt[7];
            var check = (delegate* unmanaged[Stdcall]<IntPtr, int, void*, uint, int>)vt[13];

            // D3D12_FEATURE_FEATURE_LEVELS (2)
            var levels = stackalloc int[] { 0xC200, 0xC100, 0xC000, 0xB100, 0xB000 };
            var fl = stackalloc byte[24];
            *(uint*)fl = 5;
            *(int**)(fl + 8) = levels;
            var maxLevel = check(device, 2, fl, 24) >= 0 ? *(int*)(fl + 16) : 0;

            // D3D12_FEATURE_SHADER_MODEL (7): ask for the newest known model, step down if the runtime is older.
            var sm = 0;
            for (var candidate = 0x69; candidate >= 0x60 && sm == 0; candidate--)
            {
                var value = candidate;
                if (check(device, 7, &value, 4) >= 0) sm = value;
            }

            var opt = stackalloc byte[64];
            int Read(int feature, uint size, int offset)
            {
                new Span<byte>(opt, 64).Clear();
                return check(device, feature, opt, size) >= 0 ? *(int*)(opt + offset) : 0;
            }

            var binding = Read(0, 60, 16);   // D3D12_FEATURE_D3D12_OPTIONS → ResourceBindingTier
            var rt = Read(27, 12, 8);        // OPTIONS5 → RaytracingTier
            var vrs = Read(30, 20, 8);       // OPTIONS6 → VariableShadingRateTier
            var mesh = Read(32, 8, 0);       // OPTIONS7 → MeshShaderTier

            // D3D12_FEATURE_ARCHITECTURE1 (16) → UMA (integrated GPU)
            var arch = stackalloc byte[20];
            bool? uma = check(device, 16, arch, 20) >= 0 ? *(int*)(arch + 8) != 0 : null;

            return new D3D12Caps(maxLevel, sm, rt, mesh, vrs, binding, uma, (int)getNodeCount(device));
        }
        finally
        {
            Release(device);
        }
    }

    // ------------------------------------------------------------------ D3DKMT (WDDM kernel thunks in gdi32)

    public const int KMTQAITYPE_DRIVERVERSION = 13;
    public const int KMTQAITYPE_ADAPTERTYPE = 15;
    public const int KMTQAITYPE_NODEPERFDATA = 61;
    public const int KMTQAITYPE_ADAPTERPERFDATA = 62;
    public const int KMTQAITYPE_ADAPTERPERFDATA_CAPS = 63;
    public const int KMTQAITYPE_GPUVERSION = 64;
    public const int KMTQAITYPE_WDDM_2_7_CAPS = 70;

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_OPENADAPTERFROMLUID
    {
        public uint LuidLow;
        public int LuidHigh;
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public void* pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID args);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO args);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(ref uint hAdapter);

    public static uint OpenAdapter(long luid)
    {
        try
        {
            var args = new D3DKMT_OPENADAPTERFROMLUID { LuidLow = (uint)(luid & 0xFFFFFFFF), LuidHigh = (int)(luid >> 32) };
            return D3DKMTOpenAdapterFromLuid(ref args) == 0 ? args.hAdapter : 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    public static void CloseAdapter(uint handle)
    {
        if (handle == 0) return;
        try { D3DKMTCloseAdapter(ref handle); }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>Queries adapter info into <paramref name="buffer"/> (pre-filled with any input fields). Returns false on NTSTATUS failure.</summary>
    public static bool Query(uint handle, int type, Span<byte> buffer)
    {
        if (handle == 0) return false;
        fixed (byte* p = buffer)
        {
            var args = new D3DKMT_QUERYADAPTERINFO { hAdapter = handle, Type = type, pPrivateDriverData = p, PrivateDriverDataSize = (uint)buffer.Length };
            try
            {
                return D3DKMTQueryAdapterInfo(ref args) == 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    // ------------------------------------------------------------------ PnP (PCIe link)

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    private static readonly Guid PciDeviceKeys = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    private static partial uint CM_Get_DevNode_Property(uint devInst, in DEVPROPKEY key, out uint type, byte* buffer, ref uint size, uint flags);

    /// <summary>Reads DEVPKEY_PciDevice_{Current,Max}Link{Speed,Width} (pids 9–12). Returns null when not a PCIe device.</summary>
    public static (uint CurSpeed, uint CurWidth, uint MaxSpeed, uint MaxWidth)? ReadPcieLink(string pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId)) return null;
        try
        {
            if (CM_Locate_DevNode(out var node, pnpDeviceId, 0) != 0) return null;
            uint Read(uint pid)
            {
                var key = new DEVPROPKEY { fmtid = PciDeviceKeys, pid = pid };
                uint value = 0, size = 4;
                return CM_Get_DevNode_Property(node, key, out _, (byte*)&value, ref size, 0) == 0 ? value : 0;
            }
            var result = (Read(9), Read(10), Read(11), Read(12));
            return result.Item3 == 0 && result.Item1 == 0 ? null : result;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
