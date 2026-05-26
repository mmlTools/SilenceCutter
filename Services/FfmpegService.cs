using SilenceCutter.Models;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SilenceCutter.Services;

public sealed class FfmpegService
{
    private static readonly Regex DurationRegex = new(@"Duration:\s(?<h>\d+):(?<m>\d+):(?<s>\d+(\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex SilenceStartRegex = new(@"silence_start:\s(?<v>[0-9\.]+)", RegexOptions.Compiled);
    private static readonly Regex SilenceEndRegex = new(@"silence_end:\s(?<v>[0-9\.]+)", RegexOptions.Compiled);

    public string FfmpegPath { get; set; } = "ffmpeg";
    public string FfprobePath { get; set; } = "ffprobe";

    public async Task<double> GetDurationAsync(string file, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync(FfprobePath,
            $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 {Q(file)}", ct);

        if (double.TryParse(result.StdOut.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var sec))
            return sec;

        var fallback = await ProcessRunner.RunAsync(FfmpegPath, $"-i {Q(file)} -f null -", ct);
        var m = DurationRegex.Match(fallback.StdErr);
        if (!m.Success) throw new InvalidOperationException("Could not read clip duration. Make sure FFmpeg/FFprobe is installed and available in PATH.");

        return int.Parse(m.Groups["h"].Value) * 3600 + int.Parse(m.Groups["m"].Value) * 60 + double.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
    }

    public async Task<List<TimelineSegment>> DetectSegmentsAsync(string file, double thresholdDb, double minSilenceSeconds, double keepPaddingSeconds, CancellationToken ct = default)
    {
        var duration = await GetDurationAsync(file, ct);
        var args = $"-hide_banner -i {Q(file)} -af silencedetect=noise={thresholdDb.ToString(CultureInfo.InvariantCulture)}dB:d={minSilenceSeconds.ToString(CultureInfo.InvariantCulture)} -f null -";
        var result = await ProcessRunner.RunAsync(FfmpegPath, args, ct);

        var silenceRanges = new List<(double Start, double End)>();
        double? pendingStart = null;

        foreach (var line in result.StdErr.Split('\n'))
        {
            var ss = SilenceStartRegex.Match(line);
            if (ss.Success) pendingStart = Parse(ss.Groups["v"].Value);

            var se = SilenceEndRegex.Match(line);
            if (se.Success && pendingStart.HasValue)
            {
                var end = Parse(se.Groups["v"].Value);
                silenceRanges.Add((Math.Max(0, pendingStart.Value + keepPaddingSeconds), Math.Min(duration, end - keepPaddingSeconds)));
                pendingStart = null;
            }
        }

        if (pendingStart.HasValue)
            silenceRanges.Add((Math.Max(0, pendingStart.Value + keepPaddingSeconds), duration));

        silenceRanges = silenceRanges.Where(x => x.End > x.Start).OrderBy(x => x.Start).ToList();
        return BuildFullTimeline(duration, silenceRanges);
    }

    public async Task ExportCutVideoAsync(string inputFile, IEnumerable<TimelineSegment> segments, string outputFile, bool reencode, CancellationToken ct = default)
    {
        var keep = segments.Where(s => !(s.Kind == SegmentKind.Silence && s.Remove)).OrderBy(s => s.Start).ToList();
        if (keep.Count == 0) throw new InvalidOperationException("No segments left to export.");

        var temp = Path.Combine(Path.GetTempPath(), "silence-cutter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var parts = new List<string>();
            for (var i = 0; i < keep.Count; i++)
            {
                var part = Path.Combine(temp, $"part_{i:0000}.mp4");
                parts.Add(part);
                var seek = keep[i].Start.ToString(CultureInfo.InvariantCulture);
                var dur = keep[i].Duration.ToString(CultureInfo.InvariantCulture);
                var codec = reencode ? "-c:v libx264 -preset veryfast -crf 18 -c:a aac -b:a 192k" : "-c copy";
                await ProcessRunner.RunAsync(FfmpegPath, $"-y -ss {seek} -i {Q(inputFile)} -t {dur} {codec} {Q(part)}", ct);
            }

            var listFile = Path.Combine(temp, "concat.txt");
            await File.WriteAllLinesAsync(listFile, parts.Select(p => $"file '{p.Replace("'", "'\\''")}'"), ct);
            var concatCodec = reencode ? "-c copy" : "-c copy";
            var concat = await ProcessRunner.RunAsync(FfmpegPath, $"-y -f concat -safe 0 -i {Q(listFile)} {concatCodec} {Q(outputFile)}", ct);
            if (concat.ExitCode != 0) throw new InvalidOperationException(concat.StdErr);
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    public async Task ExportPausesOnlyAsync(string inputFile, IEnumerable<TimelineSegment> segments, string outputFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputFolder);
        var pauses = segments.Where(s => s.Kind == SegmentKind.Silence && s.Remove).OrderBy(s => s.Start).ToList();
        for (var i = 0; i < pauses.Count; i++)
        {
            var s = pauses[i];
            var outFile = Path.Combine(outputFolder, $"pause_{i + 1:000}_{TimelineSegment.TimeFmt(s.Start).Replace(':','-')}.mp4");
            await ProcessRunner.RunAsync(FfmpegPath,
                $"-y -ss {s.Start.ToString(CultureInfo.InvariantCulture)} -i {Q(inputFile)} -t {s.Duration.ToString(CultureInfo.InvariantCulture)} -c copy {Q(outFile)}", ct);
        }
    }

    public Task PlaySegmentAsync(string inputFile, TimelineSegment segment)
    {
        var args = $"-autoexit -ss {segment.Start.ToString(CultureInfo.InvariantCulture)} -t {segment.Duration.ToString(CultureInfo.InvariantCulture)} -i {Q(inputFile)}";
        Process.Start(new ProcessStartInfo
        {
            FileName = "ffplay",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return Task.CompletedTask;
    }

    private static List<TimelineSegment> BuildFullTimeline(double duration, List<(double Start, double End)> silences)
    {
        var list = new List<TimelineSegment>();
        var cursor = 0d;
        foreach (var s in silences)
        {
            if (s.Start > cursor) list.Add(new TimelineSegment { Kind = SegmentKind.Speech, Start = cursor, End = s.Start, Remove = false });
            list.Add(new TimelineSegment { Kind = SegmentKind.Silence, Start = s.Start, End = s.End, Remove = true });
            cursor = s.End;
        }
        if (cursor < duration) list.Add(new TimelineSegment { Kind = SegmentKind.Speech, Start = cursor, End = duration, Remove = false });
        return list;
    }

    private static double Parse(string v) => double.Parse(v, CultureInfo.InvariantCulture);
    private static string Q(string path) => "\"" + path.Replace("\"", "\\\"") + "\"";
}
