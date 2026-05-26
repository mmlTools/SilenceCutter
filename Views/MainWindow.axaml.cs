using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Diagnostics;
using SilenceCutter.Models;
using SilenceCutter.ViewModels;

namespace SilenceCutter.Views;

public partial class MainWindow : Window
{
    private bool _isFfmpegDialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StorageProvider = StorageProvider;
                vm.ExportCompleted -= ViewModel_ExportCompleted;
                vm.ExportCompleted += ViewModel_ExportCompleted;
                vm.FfmpegMissing -= ViewModel_FfmpegMissing;
                vm.FfmpegMissing += ViewModel_FfmpegMissing;
                if (vm.CheckForUpdatesCommand.CanExecute(null))
                    vm.CheckForUpdatesCommand.Execute(null);
                await vm.CheckFfmpegAvailableAsync();
            }
        };
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Visual visual && visual.GetVisualAncestors().OfType<Button>().Any())
            return;

        BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private async void SupportProjectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri("https://www.paypal.com/donate/?hosted_button_id=ZKTLLYY9ADWYQ"));
    }

    private async void OpenLatestReleaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(vm.LatestReleaseUrl))
            return;

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri(vm.LatestReleaseUrl));
    }

    private async void ViewModel_ExportCompleted(string folder)
    {
        await ShowExportCompleteDialogAsync(folder);
    }

    private async void ViewModel_FfmpegMissing()
    {
        await ShowFfmpegMissingDialogAsync();
    }

    private void AddClipsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.AddClipsCommand);
    }

    private void ClipsDropArea_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasDroppedFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ClipsDropArea_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var paths = e.DataTransfer.TryGetFiles()?
                .Select(file => file.Path.LocalPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>();

            if (paths is not null)
                vm.AddClipPaths(paths);
        }

        e.Handled = true;
    }

    private void AnalyzeSelectedButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.AnalyzeSelectedCommand);
    }

    private void AnalyzeAllButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.AnalyzeAllCommand);
    }

    private void ExportCutVideoButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.ExportCutVideoCommand);
    }

    private void ExportPausesOnlyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.ExportPausesOnlyCommand);
    }

    private void ExportEdlButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.ExportEdlCommand);
    }

    private void ExportCsvButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            ExecuteIfReady(vm.ExportCsvCommand);
    }

    private void TimelineTrackContainer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.SetTimelineTrackWidth(e.NewSize.Width);
    }

    private static void ExecuteIfReady(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private static bool HasDroppedFiles(DragEventArgs e) => e.DataTransfer.TryGetFiles()?.Any() == true;

    private void PlaySegmentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Button { Tag: TimelineSegment segment } &&
            vm.PlaySegmentCommand.CanExecute(segment))
        {
            vm.PlaySegmentCommand.Execute(segment);
        }
    }

    private void TrackSegmentButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm &&
            sender is Button { Tag: TimelineSegmentBlock { Segment: { Kind: SegmentKind.Speech } segment } } &&
            vm.PlaySegmentCommand.CanExecute(segment))
        {
            vm.PlaySegmentCommand.Execute(segment);
        }
    }

    private void ResizeTop_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.North, e);
    private void ResizeBottom_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.South, e);
    private void ResizeLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.West, e);
    private void ResizeRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.East, e);
    private void ResizeTopLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthWest, e);
    private void ResizeTopRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthEast, e);
    private void ResizeBottomLeft_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthWest, e);
    private void ResizeBottomRight_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthEast, e);

    private void BeginResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        BeginResizeDrag(edge, e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ExportCompleted -= ViewModel_ExportCompleted;
            vm.FfmpegMissing -= ViewModel_FfmpegMissing;
            if (vm.ClosePreviewCommand.CanExecute(null))
                vm.ClosePreviewCommand.Execute(null);
        }

        base.OnClosed(e);
    }

    private async Task ShowExportCompleteDialogAsync(string folder)
    {
        var dialog = new Window
        {
            Title = "Export Complete",
            Width = 430,
            Height = 310,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Colors.Transparent),
            SystemDecorations = SystemDecorations.None,
            Content = BuildExportDialogContent(folder)
        };

        await dialog.ShowDialog(this);
    }

    private async Task ShowFfmpegMissingDialogAsync()
    {
        if (_isFfmpegDialogOpen)
            return;

        _isFfmpegDialogOpen = true;
        try
        {
            var dialog = new Window
            {
                Title = "FFmpeg Required",
                Width = 460,
                Height = 260,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = new SolidColorBrush(Colors.Transparent),
                SystemDecorations = SystemDecorations.None,
                Content = BuildFfmpegMissingDialogContent()
            };

            await dialog.ShowDialog(this);
        }
        finally
        {
            _isFfmpegDialogOpen = false;
        }
    }

    private Control BuildFfmpegMissingDialogContent()
    {
        var title = new TextBlock
        {
            Text = "FFmpeg is missing",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#E5ECF7"))
        };

        var closeButton = new Button
        {
            Content = "x",
            Width = 32,
            Height = 28,
            MinWidth = 32,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        closeButton.Click += (_, _) => ((Window)closeButton.GetVisualRoot()!).Close();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 10),
            Children = { title, closeButton }
        };
        Grid.SetColumn(closeButton, 1);

        var headerBand = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#101722")),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Child = header
        };
        headerBand.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual visual && visual.GetVisualAncestors().OfType<Button>().Any())
                return;

            if (e.GetCurrentPoint(headerBand).Properties.IsLeftButtonPressed &&
                headerBand.GetVisualRoot() is Window window)
            {
                window.BeginMoveDrag(e);
            }
        };

        var message = new TextBlock
        {
            Text = "Silence Cutter needs FFmpeg, FFprobe, and FFplay to analyze, preview, and export clips. Install FFmpeg and make sure it is available in PATH, then restart the app.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#B8C2D6"))
        };

        var downloadButton = new Button
        {
            Content = "Download FFmpeg",
            Width = 180,
            MinHeight = 36,
            Padding = new Thickness(14, 7),
            Background = new SolidColorBrush(Color.Parse("#2A3447")),
            Foreground = new SolidColorBrush(Color.Parse("#E5ECF7")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4B5C76")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        downloadButton.Click += async (_, _) =>
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
                await launcher.LaunchUriAsync(new Uri("https://ffmpeg.org/download.html"));
        };

        var body = new StackPanel
        {
            Spacing = 16,
            Margin = new Thickness(18),
            Children = { message, downloadButton }
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#151922")),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Children = { headerBand, body }
            },
        };
    }

    private Control BuildExportDialogContent(string folder)
    {
        var title = new TextBlock
        {
            Text = "Your export is ready",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#E5ECF7"))
        };

        var headerCloseButton = new Button
        {
            Content = "×",
            Width = 32,
            Height = 28,
            MinWidth = 32,
            MinHeight = 28,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
            Foreground = new SolidColorBrush(Color.Parse("#CBD5E1")),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Cursor = new Cursor(StandardCursorType.Hand),
            FontSize = 16,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        headerCloseButton.Click += (_, _) => ((Window)headerCloseButton.GetVisualRoot()!).Close();

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 10),
            Children = { title, headerCloseButton }
        };
        Grid.SetColumn(headerCloseButton, 1);

        var headerBand = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#101722")),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Child = header
        };
        headerBand.PointerPressed += (_, e) =>
        {
            if (e.Source is Visual visual && visual.GetVisualAncestors().OfType<Button>().Any())
                return;

            if (e.GetCurrentPoint(headerBand).Properties.IsLeftButtonPressed &&
                headerBand.GetVisualRoot() is Window window)
            {
                window.BeginMoveDrag(e);
            }
        };

        var message = new TextBlock
        {
            Text = "Nice, the file finished exporting. You can open the destination folder now and check the result.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#B8C2D6"))
        };

        var support = new TextBlock
        {
            Text = "Silence Cutter is an independent tool. If it saves you editing time, even a small amount helps a lot in the development.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#B8C2D6"))
        };

        var openFolderButton = new Button
        {
            Content = "Open Folder",
            Width = 180,
            MinHeight = 36,
            Padding = new Thickness(14, 7),
            Background = new SolidColorBrush(Color.Parse("#2A3447")),
            Foreground = new SolidColorBrush(Color.Parse("#E5ECF7")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4B5C76")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        openFolderButton.Click += (_, _) => OpenFolder(folder);

        var supportButton = new Button
        {
            Content = "♥ Support Project",
            Width = 180,
            MinHeight = 36,
            Padding = new Thickness(14, 7),
            Background = new SolidColorBrush(Color.Parse("#2B2235")),
            Foreground = new SolidColorBrush(Color.Parse("#F3D9FF")),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        supportButton.Click += async (_, _) =>
        {
            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is not null)
                await launcher.LaunchUriAsync(new Uri("https://www.paypal.com/donate/?hosted_button_id=ZKTLLYY9ADWYQ"));
        };

        var orDelimiter = new TextBlock
        {
            Text = "OR",
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#7E8AA0")),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold
        };

        var actions = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children = { supportButton, orDelimiter, openFolderButton }
        };

        var body = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(18),
            Children = { message, support, actions }
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#151922")),
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Children = { headerBand, body }
            },
        };
    }

    private static void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }
}
