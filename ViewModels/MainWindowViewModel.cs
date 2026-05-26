using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SilenceCutter.Models;
using SilenceCutter.Services;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace SilenceCutter.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly FfmpegService _ffmpeg = new();
    private readonly UpdateChecker _updateChecker = new("mmlTools", "SilenceCutter");
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly List<TimelineSegment> _watchedSegments = new();
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(125) };
    private readonly List<string> _previewFrames = new();
    private CancellationTokenSource? _previewCts;
    private Process? _previewAudioProcess;
    private string? _previewFolder;
    private int _previewFrameIndex;
    private double _timelineTrackWidth = 760;
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".mkv",
        ".webm",
        ".avi"
    };

    public ObservableCollection<MediaClip> Clips { get; } = new();
    public ObservableCollection<string> Toasts { get; } = new();
    public ObservableCollection<TimelineSegmentBlock> TimelineBlocks { get; } = new();

    public event Action<string>? ExportCompleted;
    public event Action? FfmpegMissing;

    [ObservableProperty] private MediaClip? selectedClip;
    [ObservableProperty] private double thresholdDb = -35;
    [ObservableProperty] private double minSilenceSeconds = 0.45;
    [ObservableProperty] private double keepPaddingSeconds = 0.08;
    [ObservableProperty] private double resolveFps = 25;
    [ObservableProperty] private string status = "Add clips to begin.";
    [ObservableProperty] private bool reencodeExports = true;
    [ObservableProperty] private bool isPreviewOpen;
    [ObservableProperty] private string previewTitle = "";
    [ObservableProperty] private string previewDetails = "";
    [ObservableProperty] private Bitmap? previewFrame;
    [ObservableProperty] private bool isPreviewLoading;
    [ObservableProperty] private bool isUpdateAvailable;
    [ObservableProperty] private string latestVersion = "";
    [ObservableProperty] private string latestReleaseUrl = "";

    public MainWindowViewModel()
    {
        _previewTimer.Tick += (_, _) => AdvancePreviewFrame();
    }

    public IStorageProvider? StorageProvider { get; set; }

    public async Task CheckFfmpegAvailableAsync()
    {
        try
        {
            await _ffmpeg.CheckToolsAvailableAsync();
        }
        catch (Exception ex) when (FfmpegService.IsMissingToolException(ex))
        {
            Status = "FFmpeg was not found. Install FFmpeg and make sure it is available in PATH.";
            FfmpegMissing?.Invoke();
        }
    }

    public void AddClipPaths(IEnumerable<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;

            if (!SupportedVideoExtensions.Contains(Path.GetExtension(path)))
                continue;

            if (Clips.Any(x => string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            Clips.Add(new MediaClip { FilePath = path, FileName = Path.GetFileName(path) });
            added++;
        }

        SelectedClip ??= Clips.FirstOrDefault();

        if (added > 0)
            Status = added == 1 ? "Added 1 clip." : $"Added {added} clips.";
    }

    public void SetTimelineTrackWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _timelineTrackWidth) < 0.5)
            return;

        _timelineTrackWidth = width;
        RefreshTimelineBlocks();
    }

    public bool AllPausesSelected
    {
        get
        {
            var pauses = SelectedClip?.Segments.Where(x => x.Kind == SegmentKind.Silence).ToList();
            return pauses?.Count > 0 && pauses.All(x => x.Remove);
        }
        set
        {
            if (SelectedClip is null)
                return;

            foreach (var segment in SelectedClip.Segments.Where(x => x.Kind == SegmentKind.Silence))
                segment.Remove = value;

            OnPropertyChanged();
        }
    }

    partial void OnSelectedClipChanged(MediaClip? value)
    {
        WatchPauseSelection(value);
        RefreshTimelineBlocks();
        OnPropertyChanged(nameof(AllPausesSelected));
    }

    partial void OnStatusChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            _ = ShowToastAsync(value);
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            _settings.LastKnownVersion = _updateChecker.CurrentVersion.ToString();
            _settings.Save();

            var update = await _updateChecker.CheckLatestReleaseAsync();
            if (update is null)
            {
                IsUpdateAvailable = false;
                return;
            }

            LatestVersion = update.Version;
            LatestReleaseUrl = update.Url;
            IsUpdateAvailable = true;
            Status = $"Silence Cutter {update.Version} is available.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Update check failed: {ex.Message}");
        }
    }

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

        AddClipPaths(files.Select(f => f.TryGetLocalPath()).OfType<string>());
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
            ClosePreview();
            PreviewTitle = SelectedClip.FileName;
            PreviewDetails = $"{TimelineSegment.TimeFmt(segment.Start)} - {TimelineSegment.TimeFmt(segment.End)} ({segment.Duration:0.00}s)";
            IsPreviewOpen = true;
            IsPreviewLoading = true;
            Status = $"Playing {TimelineSegment.TimeFmt(segment.Start)} - {TimelineSegment.TimeFmt(segment.End)} from {SelectedClip.FileName}.";

            _previewCts = new CancellationTokenSource();
            _previewFolder = Path.Combine(Path.GetTempPath(), "silence-cutter-preview-" + Guid.NewGuid().ToString("N"));
            var frames = await _ffmpeg.ExtractPreviewFramesAsync(SelectedClip.FilePath, segment, _previewFolder, ct: _previewCts.Token);

            _previewFrames.Clear();
            _previewFrames.AddRange(frames);
            _previewFrameIndex = 0;

            if (_previewFrames.Count > 0)
            {
                SetPreviewFrame(_previewFrames[0]);
                _previewAudioProcess = _ffmpeg.StartSegmentAudioPlayback(SelectedClip.FilePath, segment);
                _previewTimer.Start();
            }

            IsPreviewLoading = false;
        }
        catch (Exception ex)
        {
            ClosePreview();

            if (HandleMissingFfmpeg(ex))
                return;

            Status = $"Could not play segment: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClosePreview()
    {
        _previewTimer.Stop();
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;
        StopPreviewAudio();
        _previewFrames.Clear();
        _previewFrameIndex = 0;
        SetPreviewFrame(null);
        IsPreviewOpen = false;
        IsPreviewLoading = false;

        if (_previewFolder is not null)
        {
            try { Directory.Delete(_previewFolder, true); } catch { }
            _previewFolder = null;
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
        try
        {
            await _ffmpeg.ExportCutVideoAsync(SelectedClip.FilePath, SelectedClip.Segments, path, ReencodeExports);
            Status = "Exported cut video.";
            NotifyExportCompleted(path);
        }
        catch (Exception ex)
        {
            if (!HandleMissingFfmpeg(ex))
                Status = $"Could not export cut video: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportPausesOnlyAsync()
    {
        if (SelectedClip is null || StorageProvider is null) return;
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose folder for pause clips", AllowMultiple = false });
        var path = folder.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;
        Status = "Exporting pause clips...";
        try
        {
            await _ffmpeg.ExportPausesOnlyAsync(SelectedClip.FilePath, SelectedClip.Segments, path);
            Status = "Exported pause clips.";
            NotifyExportCompleted(path);
        }
        catch (Exception ex)
        {
            if (!HandleMissingFfmpeg(ex))
                Status = $"Could not export pause clips: {ex.Message}";
        }
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
        NotifyExportCompleted(path);
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
        NotifyExportCompleted(path);
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
            if (ReferenceEquals(clip, SelectedClip))
                WatchPauseSelection(clip);
            if (ReferenceEquals(clip, SelectedClip))
                RefreshTimelineBlocks();
            OnPropertyChanged(nameof(AllPausesSelected));
            Status = clip.Status;
        }
        catch (Exception ex)
        {
            if (HandleMissingFfmpeg(ex))
            {
                clip.Status = "FFmpeg missing";
                return;
            }

            clip.Status = "Error";
            Status = ex.Message;
        }
    }

    private async Task ShowToastAsync(string message)
    {
        Toasts.Add(message);
        while (Toasts.Count > 3)
            Toasts.RemoveAt(0);

        await Task.Delay(4200);
        await Dispatcher.UIThread.InvokeAsync(() => Toasts.Remove(message));
    }

    private void NotifyExportCompleted(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            ExportCompleted?.Invoke(folder);
    }

    private bool HandleMissingFfmpeg(Exception ex)
    {
        if (!FfmpegService.IsMissingToolException(ex))
            return false;

        Status = "FFmpeg was not found. Install FFmpeg and make sure it is available in PATH.";
        FfmpegMissing?.Invoke();
        return true;
    }

    private void WatchPauseSelection(MediaClip? clip)
    {
        foreach (var segment in _watchedSegments)
            segment.PropertyChanged -= Segment_PropertyChanged;

        _watchedSegments.Clear();

        if (clip is null)
            return;

        foreach (var segment in clip.Segments.Where(x => x.Kind == SegmentKind.Silence))
        {
            segment.PropertyChanged += Segment_PropertyChanged;
            _watchedSegments.Add(segment);
        }
    }

    private void Segment_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineSegment.Remove))
        {
            RefreshTimelineBlocks();
            OnPropertyChanged(nameof(AllPausesSelected));
        }
    }

    private void RefreshTimelineBlocks()
    {
        TimelineBlocks.Clear();

        if (SelectedClip is null || SelectedClip.Segments.Count == 0)
            return;

        var duration = SelectedClip.Segments.Sum(x => x.Duration);
        if (duration <= 0)
            return;

        foreach (var segment in SelectedClip.Segments)
        {
            TimelineBlocks.Add(new TimelineSegmentBlock
            {
                Segment = segment,
                Width = Math.Max(8, segment.Duration / duration * _timelineTrackWidth)
            });
        }
    }

    private void AdvancePreviewFrame()
    {
        if (_previewFrames.Count == 0)
            return;

        _previewFrameIndex = (_previewFrameIndex + 1) % _previewFrames.Count;
        SetPreviewFrame(_previewFrames[_previewFrameIndex]);
    }

    private void StopPreviewAudio()
    {
        if (_previewAudioProcess is null)
            return;

        try
        {
            if (!_previewAudioProcess.HasExited)
                _previewAudioProcess.Kill(true);
        }
        catch
        {
        }
        finally
        {
            _previewAudioProcess.Dispose();
            _previewAudioProcess = null;
        }
    }

    private void SetPreviewFrame(string? path)
    {
        PreviewFrame = path is null ? null : new Bitmap(path);
    }

    partial void OnPreviewFrameChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        if (!ReferenceEquals(oldValue, newValue))
            oldValue?.Dispose();
    }

    partial void OnLatestVersionChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settings.LastSeenUpdateVersion = value;
            _settings.Save();
        }
    }
}
