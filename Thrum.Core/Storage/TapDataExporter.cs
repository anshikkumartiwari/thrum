using System.Globalization;
using System.Text;
using System.Text.Json;
using Thrum.Core.Dsp;
using Thrum.Core.Models;

namespace Thrum.Core.Storage;

/// <summary>
/// Persists and exports tap recordings, acoustic feature vectors, and rejection diagnostics
/// to CSV and JSON formats for offline inspection, analysis, and custom tuning.
/// </summary>
public static class TapDataExporter
{
    private static readonly object FileLock = new();

    public static string GetRecordingsDirectory(string? customBaseDirectory = null)
    {
        string baseDir = customBaseDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thrum");
        string recordingsDir = Path.Combine(baseDir, "recordings");
        if (!Directory.Exists(recordingsDir))
        {
            Directory.CreateDirectory(recordingsDir);
        }
        return recordingsDir;
    }

    public static string GetDefaultCsvPath(string profileName, string? customBaseDirectory = null)
    {
        string safeName = SanitizeFileName(profileName);
        return Path.Combine(GetRecordingsDirectory(customBaseDirectory), $"{safeName}_taps.csv");
    }

    public static string GetRejectedTapsCsvPath(string profileName, string? customBaseDirectory = null)
    {
        string safeName = SanitizeFileName(profileName);
        return Path.Combine(GetRecordingsDirectory(customBaseDirectory), $"{safeName}_rejected_taps.csv");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c);
        }
        return sb.Length > 0 ? sb.ToString() : "default";
    }

    /// <summary>
    /// Appends a newly accepted tap sample and its 27 acoustic features to the CSV dataset file.
    /// </summary>
    public static void AppendAcceptedSample(string profileName, TapFeatureSample sample, string? customBaseDirectory = null)
    {
        lock (FileLock)
        {
            string csvPath = GetDefaultCsvPath(profileName, customBaseDirectory);
            bool fileExists = File.Exists(csvPath);

            using var sw = new StreamWriter(csvPath, append: true, Encoding.UTF8);
            if (!fileExists)
            {
                sw.WriteLine(BuildCsvHeader());
            }

            var line = new StringBuilder();
            line.Append(sample.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',');
            line.Append(EscapeCsv(profileName)).Append(',');
            line.Append(EscapeCsv(sample.ZoneId)).Append(',');
            line.Append(EscapeCsv(sample.ZoneName)).Append(',');
            line.Append(sample.TapIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.PeakAmplitude.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.Rms.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.CrestFactor.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.LateEarlyEnergyRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.EffectiveDurationMs.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            line.Append(sample.SnrRatio.ToString("F2", CultureInfo.InvariantCulture));

            // Append all 27 feature dimensions
            for (int i = 0; i < sample.Features.Length; i++)
            {
                line.Append(',').Append(sample.Features[i].ToString("F6", CultureInfo.InvariantCulture));
            }

            sw.WriteLine(line.ToString());
        }
    }

    /// <summary>
    /// Logs rejected taps with reasons and values for tuning and diagnostic inspection.
    /// </summary>
    public static void AppendRejectedSample(
        string profileName,
        string zoneName,
        ImpulseGateResult result,
        string? customBaseDirectory = null)
    {
        lock (FileLock)
        {
            string csvPath = GetRejectedTapsCsvPath(profileName, customBaseDirectory);
            bool fileExists = File.Exists(csvPath);

            using var sw = new StreamWriter(csvPath, append: true, Encoding.UTF8);
            if (!fileExists)
            {
                sw.WriteLine("Timestamp,ProfileName,ZoneName,Reason,Message,PeakAmplitude,Rms,CrestFactor,LateEarlyRatio,EffectiveDurationMs,SnrRatio");
            }

            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5:F6},{6:F6},{7:F3},{8:F4},{9:F2},{10:F2}",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                EscapeCsv(profileName),
                EscapeCsv(zoneName),
                result.Reason.ToString(),
                EscapeCsv(result.RejectionMessage),
                result.PeakAmplitude,
                result.Rms,
                result.CrestFactor,
                result.LateEarlyEnergyRatio,
                result.EffectiveDurationMs,
                result.SnrRatio);

            sw.WriteLine(line);
        }
    }

    /// <summary>
    /// Exports the complete profile's tap samples to a standalone CSV.
    /// </summary>
    public static void ExportProfileTapsToCsv(Profile profile, string targetFilePath)
    {
        lock (FileLock)
        {
            using var sw = new StreamWriter(targetFilePath, append: false, Encoding.UTF8);
            sw.WriteLine(BuildCsvHeader());

            var zonesById = profile.Zones.ToDictionary(z => z.Id, z => z.Name);

            foreach (var sample in profile.TrainingSamples)
            {
                string zoneName = !string.IsNullOrEmpty(sample.ZoneName)
                    ? sample.ZoneName
                    : (zonesById.TryGetValue(sample.ZoneId, out var zn) ? zn : "Unknown");

                var line = new StringBuilder();
                line.Append(sample.Timestamp.ToString("o", CultureInfo.InvariantCulture)).Append(',');
                line.Append(EscapeCsv(profile.Name)).Append(',');
                line.Append(EscapeCsv(sample.ZoneId)).Append(',');
                line.Append(EscapeCsv(zoneName)).Append(',');
                line.Append(sample.TapIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.PeakAmplitude.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.Rms.ToString("F6", CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.CrestFactor.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.LateEarlyEnergyRatio.ToString("F4", CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.EffectiveDurationMs.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                line.Append(sample.SnrRatio.ToString("F2", CultureInfo.InvariantCulture));

                for (int i = 0; i < sample.Features.Length; i++)
                {
                    line.Append(',').Append(sample.Features[i].ToString("F6", CultureInfo.InvariantCulture));
                }

                sw.WriteLine(line.ToString());
            }
        }
    }

    private static string BuildCsvHeader()
    {
        var sb = new StringBuilder();
        sb.Append("Timestamp,ProfileName,ZoneId,ZoneName,TapIndex,PeakAmplitude,Rms,CrestFactor,LateEarlyRatio,EffectiveDurationMs,SnrRatio");
        sb.Append(",Feat0_Peak,Feat1_Rms,Feat2_CrestFactor,Feat3_DecayTimeMs,Feat4_EarlyEnergyRatio");
        for (int b = 0; b < FeatureExtractor.NumFilterBands; b++)
        {
            sb.Append($",Feat{5 + b}_LogBand{b}");
        }
        sb.Append(",Feat25_SpectralCentroid,Feat26_SpectralRolloff");
        return sb.ToString();
    }

    private static string EscapeCsv(string val)
    {
        if (string.IsNullOrEmpty(val)) return string.Empty;
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }
}
