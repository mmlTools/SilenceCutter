using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SilenceCutter.Models;

public partial class MediaClip : ObservableObject
{
    [ObservableProperty] private string filePath = "";
    [ObservableProperty] private string fileName = "";
    [ObservableProperty] private double durationSeconds;
    [ObservableProperty] private ObservableCollection<TimelineSegment> segments = new();
    [ObservableProperty] private bool isAnalyzed;
    [ObservableProperty] private string status = "Waiting";
}
