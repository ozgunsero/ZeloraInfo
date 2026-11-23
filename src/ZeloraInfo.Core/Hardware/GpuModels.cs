// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

namespace ZeloraInfo.Core.Hardware;

/// <summary>Static description of one graphics adapter (DXGI + D3D12 + D3DKMT + registry + PnP).</summary>
public sealed class GpuAdapterInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Vendor { get; init; } = "";
    public uint VendorId { get; init; }
    public uint DeviceId { get; init; }
    public uint SubsystemId { get; init; }
    public uint Revision { get; init; }
    /// <summary>Adapter LUID (used to match PDH / D3DKMT data).</summary>
    public long Luid { get; init; }
    public bool IsSoftware { get; init; }
    /// <summary>True for UMA (integrated) GPUs, false for discrete, null if unknown.</summary>
    public bool? IsIntegrated { get; init; }

    public ulong DedicatedVideoMemory { get; init; }
    public ulong DedicatedSystemMemory { get; init; }
    public ulong SharedSystemMemory { get; init; }

    public string DriverVersion { get; init; } = "";
    /// <summary>Vendor-facing version, e.g. NVIDIA "560.94" (derived from the Windows driver version).</summary>
    public string? DriverVendorVersion { get; init; }
    public DateTime? DriverDate { get; init; }
    public string DriverProvider { get; init; } = "";
    public string WddmVersion { get; init; } = "";
    public bool? HardwareSchedulingSupported { get; init; }
    public bool? HardwareSchedulingEnabled { get; init; }

    public string ChipType { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public string PnpDeviceId { get; init; } = "";
    public string BusInterface { get; init; } = "";
    public string CurrentLink { get; init; } = "";

    // DirectX 12
    public string MaxFeatureLevel { get; init; } = "";
    public string ShaderModel { get; init; } = "";
    public string RaytracingTier { get; init; } = "";
    public string MeshShaderTier { get; init; } = "";
    public string VariableShadingRateTier { get; init; } = "";
    public string ResourceBindingTier { get; init; } = "";
    public bool? DirectX12Ultimate { get; init; }
    public int? NodeCount { get; init; }

    // Sensor capabilities (D3DKMT ADAPTERPERFDATA_CAPS)
    public int? MaxFanRpm { get; init; }
    public double? TemperatureMaxC { get; init; }
    public double? TemperatureWarningC { get; init; }
    public ulong? MaxMemoryBandwidth { get; init; }

    public string PciIdText => VendorId == 0 ? "" : $"{VendorId:X4}:{DeviceId:X4} · SUBSYS {SubsystemId:X8} · REV {Revision:X2}";
}

public sealed class GpuEngineLoad
{
    public required string Name { get; init; }
    public double Percent { get; init; }
}

/// <summary>Live values for one adapter.</summary>
public sealed class GpuLiveSample
{
    public long Luid { get; init; }
    public double UtilizationPercent { get; init; }
    public IReadOnlyList<GpuEngineLoad> Engines { get; init; } = [];
    public ulong? DedicatedUsedBytes { get; init; }
    public ulong? SharedUsedBytes { get; init; }
    public double? TemperatureC { get; init; }
    public int? FanRpm { get; init; }
    /// <summary>Power draw as a percentage of the board limit (D3DKMT reports 1/10 %).</summary>
    public double? PowerPercent { get; init; }
    public double? CoreClockMHz { get; init; }
    public double? MaxCoreClockMHz { get; init; }
    public double? MemoryClockMHz { get; init; }
    public double? MaxMemoryClockMHz { get; init; }
    public double? VoltageV { get; init; }
}
