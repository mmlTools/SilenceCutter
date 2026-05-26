using CommunityToolkit.Mvvm.ComponentModel;

namespace SilenceCutter.Models;

public partial class TimelineSegmentBlock : ObservableObject
{
    public TimelineSegment Segment { get; init; } = new();
    public string Label => Segment.Kind == SegmentKind.Silence ? "Cut" : "Keep";
    public string Background => Segment.Kind == SegmentKind.Silence && Segment.Remove ? "#D45B5B" : "#5DBB7D";
    public string ToolTip => $"{Segment.Label} - {Label}";

    [ObservableProperty] private double width;
}
