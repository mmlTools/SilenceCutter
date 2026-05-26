using SilenceCutter.Models;
using System.Text;

namespace SilenceCutter.Services;

public static class EdlExporter
{
    public static async Task ExportPauseMarkersAsync(string path, MediaClip clip, double fps = 25)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TITLE: SILENCE CUTTER PAUSE MARKERS");
        sb.AppendLine("FCM: NON-DROP FRAME");
        sb.AppendLine();

        var index = 1;
        foreach (var s in clip.Segments.Where(x => x.Kind == SegmentKind.Silence).OrderBy(x => x.Start))
        {
            sb.AppendLine($"{index:000}  AX       V     C        {Tc(s.Start, fps)} {Tc(s.End, fps)} {Tc(s.Start, fps)} {Tc(s.End, fps)}");
            sb.AppendLine($"* FROM CLIP NAME: {clip.FileName}");
            sb.AppendLine($"* LOC: {Tc(s.Start, fps)} {(s.Remove ? "REMOVE" : "KEEP")} pause {s.Duration:0.00}s");
            sb.AppendLine();
            index++;
        }

        await File.WriteAllTextAsync(path, sb.ToString());
    }

    public static async Task ExportCutListCsvAsync(string path, MediaClip clip)
    {
        var lines = new List<string> { "file,kind,start,end,duration,remove" };
        lines.AddRange(clip.Segments.Select(s => $"\"{clip.FileName}\",{s.Kind},{s.Start:0.###},{s.End:0.###},{s.Duration:0.###},{s.Remove}"));
        await File.WriteAllLinesAsync(path, lines);
    }

    private static string Tc(double seconds, double fps)
    {
        var framesTotal = (long)Math.Round(seconds * fps);
        var frames = framesTotal % (long)fps;
        var totalSeconds = framesTotal / (long)fps;
        var s = totalSeconds % 60;
        var m = (totalSeconds / 60) % 60;
        var h = totalSeconds / 3600;
        return $"{h:00}:{m:00}:{s:00}:{frames:00}";
    }
}
