using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using gMKVToolNix;
using gMKVToolNix.MkvExtract;
using gMKVToolNix.Segments;

namespace gMKVExtractGUI.Linux;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SegmentRow> _segmentRows = new();
    private bool _isAnalyzed;

    public ObservableCollection<SegmentRow> SegmentRows => _segmentRows;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = this;
        this.FindControl<ItemsControl>("SegmentList")!.ItemsSource = _segmentRows;
    }

    private async void BrowseInput_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a Matroska file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Matroska video") { Patterns = new[] { "*.mkv", "*.mka", "*.mks" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0)
        {
            return;
        }

        string inputPath = files[0].Path.LocalPath;
        this.FindControl<TextBox>("InputPathBox")!.Text = inputPath;
        TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
        outputBox.Text ??= Path.GetDirectoryName(inputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private async void BrowseOutput_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose output folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            this.FindControl<TextBox>("OutputPathBox")!.Text = folders[0].Path.LocalPath;
        }
    }

    private async void Analyze_Click(object? sender, RoutedEventArgs e)
    {
        string inputPath = this.FindControl<TextBox>("InputPathBox")!.Text?.Trim() ?? "";
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        if (!File.Exists(inputPath))
        {
            SetStatus("Select an existing input file.");
            return;
        }

        if (!Directory.Exists(toolPath))
        {
            SetStatus("Select the folder containing MKVToolNix executables.");
            return;
        }

        _isAnalyzed = false;
        _segmentRows.Clear();
        this.FindControl<TextBlock>("ItemCountText")!.Text = "Analyzing...";
        SetBusy(true);
        SetStatus("Analyzing file with mkvmerge and mkvinfo...");
        try
        {
            List<gMKVSegment> segments = await Task.Run(() => gMKVHelper.GetMergedMkvSegmentList(toolPath, inputPath));
            _segmentRows.Clear();
            foreach (gMKVSegment segment in segments.Where(IsExtractableSegment))
            {
                _segmentRows.Add(new SegmentRow(segment));
            }

            this.FindControl<TextBlock>("ItemCountText")!.Text = $"{_segmentRows.Count} items";
            _isAnalyzed = true;
            this.FindControl<ProgressBar>("ProgressBar")!.Value = 0;
            SetStatus(_segmentRows.Count == 0 ? "No extractable tracks, chapters, or attachments found." : "Analysis complete.");

            TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
            if (string.IsNullOrWhiteSpace(outputBox.Text))
            {
                outputBox.Text = Path.GetDirectoryName(inputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Analysis failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Extract_Click(object? sender, RoutedEventArgs e)
    {
        string inputPath = this.FindControl<TextBox>("InputPathBox")!.Text?.Trim() ?? "";
        string outputPath = this.FindControl<TextBox>("OutputPathBox")!.Text?.Trim() ?? "";
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        int mode = this.FindControl<ComboBox>("ExtractionModeBox")!.SelectedIndex;
        List<gMKVSegment> selected = _segmentRows.Where(row => row.IsSelected).Select(row => row.Segment).ToList();
        if (mode is 1 or 2)
        {
            selected = selected.OfType<gMKVTrack>().Cast<gMKVSegment>().ToList();
        }

        if (!File.Exists(inputPath) || !Directory.Exists(toolPath))
        {
            SetStatus("Choose an input file and a valid MKVToolNix folder first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Choose an output folder first.");
            return;
        }

        if (mode <= 2 && selected.Count == 0)
        {
            SetStatus("Select at least one item to extract.");
            return;
        }

        try
        {
            Directory.CreateDirectory(outputPath);
            var parameters = new gMKVExtractSegmentsParameters
            {
                MKVFile = inputPath,
                MKVSegmentsToExtract = selected,
                OutputDirectory = outputPath,
                ChapterType = (MkvChapterTypes)this.FindControl<ComboBox>("ChapterFormatBox")!.SelectedIndex,
                TimecodesExtractionMode = TimecodesExtractionMode.NoTimecodes,
                CueExtractionMode = CuesExtractionMode.NoCues,
                FilenamePatterns = CreateFilenamePatterns(),
                OverwriteExistingFile = this.FindControl<CheckBox>("OverwriteCheckBox")!.IsChecked == true
            };

            var extractor = new gMKVExtract(toolPath);
            extractor.MkvExtractProgressUpdated += progress => Dispatcher.UIThread.Post(() => this.FindControl<ProgressBar>("ProgressBar")!.Value = progress);
            extractor.MkvExtractTrackUpdated += (filename, trackName) => Dispatcher.UIThread.Post(() => SetStatus($"{Path.GetFileName(filename)}: {trackName}"));
            Action<gMKVExtractSegmentsParameters> extract = mode switch
            {
                0 => extractor.ExtractMKVSegmentsThreaded,
                1 => extractor.ExtractMKVCuesThreaded,
                2 => extractor.ExtractMKVTimecodesThreaded,
                3 => extractor.ExtractMkvTagsThreaded,
                _ => extractor.ExtractMkvCuesheetThreaded
            };

            SetBusy(true);
            this.FindControl<ProgressBar>("ProgressBar")!.Value = 0;
            SetStatus("Starting extraction...");
            await Task.Run(() => extract(parameters));
            if (extractor.ThreadedException != null)
            {
                throw extractor.ThreadedException;
            }

            SetStatus("Extraction complete.");
        }
        catch (Exception ex)
        {
            SetStatus($"Extraction failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (SegmentRow row in _segmentRows)
        {
            row.IsSelected = true;
        }
    }

    private void SelectNone_Click(object? sender, RoutedEventArgs e)
    {
        foreach (SegmentRow row in _segmentRows)
        {
            row.IsSelected = false;
        }
    }

    private void InputPath_Changed(object? sender, TextChangedEventArgs e)
    {
        _isAnalyzed = false;
        _segmentRows.Clear();
        this.FindControl<TextBlock>("ItemCountText")!.Text = "No file analyzed";
        this.FindControl<Button>("ExtractButton")!.IsEnabled = false;
    }

    private void SetBusy(bool isBusy)
    {
        this.FindControl<Button>("AnalyzeButton")!.IsEnabled = !isBusy;
        this.FindControl<Button>("ExtractButton")!.IsEnabled = !isBusy && _isAnalyzed;
    }

    private void SetStatus(string status) => this.FindControl<TextBlock>("StatusText")!.Text = status;

    private static bool IsExtractableSegment(gMKVSegment segment) => segment is gMKVTrack or gMKVChapter or gMKVAttachment;

    private static gMKVExtractFilenamePatterns CreateFilenamePatterns() => new()
    {
        VideoTrackFilenamePattern = "{FilenameNoExt}_video_{TrackNumber:00}",
        AudioTrackFilenamePattern = "{FilenameNoExt}_audio_{TrackNumber:00}",
        SubtitleTrackFilenamePattern = "{FilenameNoExt}_subtitles_{TrackNumber:00}",
        ChapterFilenamePattern = "{FilenameNoExt}_chapters",
        AttachmentFilenamePattern = "{AttachmentFilename}",
        TagsFilenamePattern = "{FilenameNoExt}_tags"
    };
}

public sealed class SegmentRow : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public gMKVSegment Segment { get; }
    public string Kind { get; }
    public string Summary { get; }
    public string Details { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SegmentRow(gMKVSegment segment)
    {
        Segment = segment;
        (Kind, Summary, Details) = segment switch
        {
            gMKVTrack track => (
                track.TrackType.ToString().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(track.TrackName) ? $"Track {track.TrackNumber}" : track.TrackName,
                string.Join(" | ", new[] { track.CodecID, track.Language, track.ExtraInfo }.Where(value => !string.IsNullOrWhiteSpace(value)))),
            gMKVChapter chapter => ("CHAPTERS", "Chapter markers", $"{chapter.ChapterCount} entries"),
            gMKVAttachment attachment => ("ATTACHMENT", attachment.Filename, $"{attachment.MimeType} | {attachment.FileSize} bytes"),
            _ => ("OTHER", segment.ToString() ?? "", "")
        };
    }
}