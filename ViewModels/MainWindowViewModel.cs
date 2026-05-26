using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SilenceCutter.Models;
using SilenceCutter.Services;
using System.Collections.ObjectModel;

namespace SilenceCutter.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FfmpegService _ffmpeg = new();

    public ObservableCollection<MediaClip> Clips { get; } = new();

    [ObservableProperty] private MediaClip? selectedClip;
    [ObservableProperty] private double thresholdDb = -35;
    [ObservableProperty] private double minSilenceSeconds = 0.45;
    [ObservableProperty] private double keepPaddingSeconds = 0.08;
    [ObservableProperty] private double resolveFps = 25;
    [ObservableProperty] private string status = "Add clips to begin.";
    [ObservableProperty] private bool reencodeExports = true;

    public IStorageProvider? StorageProvider { get; set; }

    [RelayCommand]
    private async Task AddClipsAsync()
    {
        if (StorageProvider is null) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = "Select clips",
            FileTypeFilter = new[] { new FilePickerFileType("Video files") { Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.webm", "*.avi" } } }
        });

        foreach (var f in files)
        {
            var path = f.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path)) continue;
            Clips.Add(new MediaClip { FilePath = path, FileName = Path.GetFileName(path) });
        }
        SelectedClip ??= Clips.FirstOrDefault();
    }

    [RelayCommand]
    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedClip is null) return;
        await AnalyzeClip(SelectedClip);
    }

    [RelayCommand]
    private async Task AnalyzeAllAsync()
    {
        foreach (var clip in Clips) await AnalyzeClip(clip);
    }

    [RelayCommand]
    private void MarkAllPausesForRemoval()
    {
        if (SelectedClip is null) return;
        foreach (var s in SelectedClip.Segments.Where(x => x.Kind == SegmentKind.Silence)) s.Remove = true;
    }

    [RelayCommand]
    private void KeepAllPauses()
    {
        if (SelectedClip is null) return;
        foreach (var s in SelectedClip.Segments.Where(x => x.Kind == SegmentKind.Silence)) s.Remove = false;
    }

    [RelayCommand]
    private async Task PlaySegmentAsync(TimelineSegment segment)
    {
        if (SelectedClip is null || segment.Kind != SegmentKind.Speech) return;

        try
        {
            Status = $"Playing {TimelineSegment.TimeFmt(segment.Start)} - {TimelineSegment.TimeFmt(segment.End)} from {SelectedClip.FileName}.";
            await _ffmpeg.PlaySegmentAsync(SelectedClip.FilePath, segment);
        }
        catch (Exception ex)
        {
            Status = $"Could not play segment: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportCutVideoAsync()
    {
        if (SelectedClip is null || StorageProvider is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export cut video",
            SuggestedFileName = Path.GetFileNameWithoutExtension(SelectedClip.FileName) + "_cut.mp4",
            DefaultExtension = "mp4"
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        Status = "Rendering cut video...";
        await _ffmpeg.ExportCutVideoAsync(SelectedClip.FilePath, SelectedClip.Segments, path, ReencodeExports);
        Status = "Exported cut video.";
    }

    [RelayCommand]
    private async Task ExportPausesOnlyAsync()
    {
        if (SelectedClip is null || StorageProvider is null) return;
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose folder for pause clips", AllowMultiple = false });
        var path = folder.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        Status = "Exporting pause clips...";
        await _ffmpeg.ExportPausesOnlyAsync(SelectedClip.FilePath, SelectedClip.Segments, path);
        Status = "Exported pause clips.";
    }

    [RelayCommand]
    private async Task ExportEdlAsync()
    {
        if (SelectedClip is null || StorageProvider is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export EDL pause markers",
            SuggestedFileName = Path.GetFileNameWithoutExtension(SelectedClip.FileName) + "_pauses.edl",
            DefaultExtension = "edl"
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        await EdlExporter.ExportPauseMarkersAsync(path, SelectedClip, ResolveFps);
        Status = "Exported EDL pause markers.";
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (SelectedClip is null || StorageProvider is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export CSV cut list",
            SuggestedFileName = Path.GetFileNameWithoutExtension(SelectedClip.FileName) + "_cutlist.csv",
            DefaultExtension = "csv"
        });
        var path = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        await EdlExporter.ExportCutListCsvAsync(path, SelectedClip);
        Status = "Exported CSV cut list.";
    }

    private async Task AnalyzeClip(MediaClip clip)
    {
        try
        {
            clip.Status = "Analyzing...";
            Status = $"Analyzing {clip.FileName}...";
            var segments = await _ffmpeg.DetectSegmentsAsync(clip.FilePath, ThresholdDb, MinSilenceSeconds, KeepPaddingSeconds);
            clip.Segments = new ObservableCollection<TimelineSegment>(segments);
            clip.DurationSeconds = segments.Sum(s => s.Duration);
            clip.IsAnalyzed = true;
            clip.Status = $"{segments.Count(s => s.Kind == SegmentKind.Silence)} pauses found";
            Status = clip.Status;
        }
        catch (Exception ex)
        {
            clip.Status = "Error";
            Status = ex.Message;
        }
    }
}
