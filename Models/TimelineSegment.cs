using CommunityToolkit.Mvvm.ComponentModel;

namespace SilenceCutter.Models;

public enum SegmentKind { Speech, Silence }

public partial class TimelineSegment : ObservableObject
{
    public SegmentKind Kind { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public double Duration => End - Start;
    public string Label => $"{Kind}  {TimeFmt(Start)} → {TimeFmt(End)}  ({Duration:0.00}s)";

    [ObservableProperty] private bool remove;

    public static string TimeFmt(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}
