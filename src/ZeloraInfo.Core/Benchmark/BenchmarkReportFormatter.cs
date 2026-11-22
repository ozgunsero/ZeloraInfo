// Copyright 2026 Şero Teknoloji
// Licensed under the Apache License, Version 2.0

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZeloraInfo.Core.Formatting;

namespace ZeloraInfo.Core.Benchmark;

/// <summary>Turns a <see cref="BenchmarkReport"/> into TXT (clipboard), CSV and JSON.</summary>
public static class BenchmarkReportFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToText(BenchmarkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("------------------------------------------------------------------------------");
        sb.AppendLine($"{report.Application} {report.ApplicationVersion} — Disk Benchmark");
        sb.AppendLine("Copyright 2026 Şero Teknoloji");
        sb.AppendLine("------------------------------------------------------------------------------");
        sb.AppendLine("* MB/s = 1,000,000 bytes/s   * KB = 1000 bytes, KiB = 1024 bytes");
        sb.AppendLine();
        foreach (var isRead in new[] { true, false })
        {
            var rows = report.Results.Where(r => r.IsRead == isRead).ToList();
            if (rows.Count == 0) continue;
            sb.AppendLine(isRead ? "[Read]" : "[Write]");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Create(Inv,
                    $"  {r.Test.Label,-14} {r.MBps,10:0.00} MB/s [{r.Iops,10:0.0} IOPS] <{r.AverageLatencyMicroseconds,10:0.00} us>"));
            }
            sb.AppendLine();
        }
        sb.AppendLine(string.Create(Inv, $"  Profile: {report.Mode}"));
        sb.AppendLine(string.Create(Inv, $"     Test: {ByteFormatter.FormatBinary(report.TestFileSizeBytes, 0, Inv)} (x{report.Iterations}) [{report.TargetDirectory}]"));
        sb.AppendLine(string.Create(Inv, $"     Pass: {report.DurationPerPassSeconds:0} s"));
        if (!string.IsNullOrWhiteSpace(report.DriveDescription))
            sb.AppendLine($"    Drive: {report.DriveDescription}");
        sb.AppendLine($"     Date: {report.Timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"       OS: {report.OperatingSystem}");
        return sb.ToString();
    }

    public static string ToCsv(BenchmarkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Test,Operation,BlockSizeKiB,QueueDepth,Threads,MBps,IOPS,AvgLatencyUs,MaxLatencyUs,TotalBytes,TotalOperations,Iterations");
        foreach (var r in report.Results)
        {
            sb.AppendLine(string.Join(',',
                Escape(r.Test.Label),
                r.IsRead ? "Read" : "Write",
                r.Test.BlockSizeKiB.ToString(Inv),
                r.Test.QueueDepth.ToString(Inv),
                r.Test.Threads.ToString(Inv),
                r.MBps.ToString("0.000", Inv),
                r.Iops.ToString("0.0", Inv),
                r.AverageLatencyMicroseconds.ToString("0.00", Inv),
                r.MaxLatencyMicroseconds.ToString("0.00", Inv),
                r.TotalBytes.ToString(Inv),
                r.TotalOperations.ToString(Inv),
                r.IterationsCompleted.ToString(Inv)));
        }
        return sb.ToString();
    }

    public static string ToJson(BenchmarkReport report) => JsonSerializer.Serialize(report, JsonOptions);

    private static string Escape(string value)
        => value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
