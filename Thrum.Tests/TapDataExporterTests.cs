using Thrum.Core.Dsp;
using Thrum.Core.Models;
using Thrum.Core.Storage;
using Xunit;

namespace Thrum.Tests;

public sealed class TapDataExporterTests : IDisposable
{
    private readonly string _tempDir;

    public TapDataExporterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ThrumTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void AppendAcceptedSample_CreatesAndAppendsValidCsv()
    {
        var sample = new TapFeatureSample(
            zoneId: "zone-1",
            zoneName: "Palm Rest Left",
            tapIndex: 1,
            features: new float[27],
            peak: 0.045f,
            rms: 0.009f,
            crest: 5.0f,
            lateEarlyRatio: 0.25f,
            durationMs: 32.5f,
            snr: 12.4f);

        TapDataExporter.AppendAcceptedSample("TestProfile", sample, _tempDir);

        string csvPath = TapDataExporter.GetDefaultCsvPath("TestProfile", _tempDir);
        Assert.True(File.Exists(csvPath));

        string[] lines = File.ReadAllLines(csvPath);
        Assert.Equal(2, lines.Length); // Header + 1 record
        Assert.Contains("Palm Rest Left", lines[1]);
        Assert.Contains("0.045000", lines[1]);
        Assert.Contains("5.000", lines[1]);
    }

    [Fact]
    public void AppendRejectedSample_LogsReasonAndMetrics()
    {
        var result = ImpulseGateResult.Reject(
            ImpulseRejectionReason.TooQuiet,
            "Too quiet (peak 0.001 < 0.002)",
            peak: 0.001f,
            rms: 0.0004f,
            crestFactor: 2.5f,
            lateEarlyRatio: 0.2f,
            effectiveDurationMs: 25f,
            snrRatio: 1.1f);

        TapDataExporter.AppendRejectedSample("TestProfile", "Desk Edge", result, _tempDir);

        string csvPath = TapDataExporter.GetRejectedTapsCsvPath("TestProfile", _tempDir);
        Assert.True(File.Exists(csvPath));

        string[] lines = File.ReadAllLines(csvPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Desk Edge", lines[1]);
        Assert.Contains("TooQuiet", lines[1]);
    }
}
