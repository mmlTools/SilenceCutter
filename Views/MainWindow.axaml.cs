using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using SilenceCutter.Models;
using SilenceCutter.ViewModels;

namespace SilenceCutter.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StorageProvider = StorageProvider;
                if (vm.CheckForUpdatesCommand.CanExecute(null))
                    vm.CheckForUpdatesCommand.Execute(null);
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
            await launcher.LaunchUriAsync(new Uri("https://paypal.me/mmltools"));
    }

    private async void OpenLatestReleaseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || string.IsNullOrWhiteSpace(vm.LatestReleaseUrl))
            return;

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
            await launcher.LaunchUriAsync(new Uri(vm.LatestReleaseUrl));
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
        if (DataContext is MainWindowViewModel vm && vm.ClosePreviewCommand.CanExecute(null))
            vm.ClosePreviewCommand.Execute(null);

        base.OnClosed(e);
    }
}
