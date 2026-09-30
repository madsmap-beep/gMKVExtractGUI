using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly ObservableCollection<InputFileGroup> _inputFiles = new();
    private bool _isAnalyzed;
    private bool _isAnalyzing;
    private int _completedExtractionFiles;
    private int _extractionFileCount;

    public ObservableCollection<SegmentRow> SegmentRows => _segmentRows;
    public ObservableCollection<InputFileGroup> InputFiles => _inputFiles;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string[]? inputPaths)
    {
        AvaloniaXamlLoader.Load(this);
        Opened += MainWindow_Opened;
        DataContext = this;
        this.FindControl<ItemsControl>("SegmentList")!.ItemsSource = _inputFiles;

        string[] existingPaths = (inputPaths ?? Array.Empty<string>()).Where(File.Exists).ToArray();
        if (existingPaths.Length > 0)
        {
            this.FindControl<TextBox>("InputPathBox")!.Text = string.Join(Environment.NewLine, existingPaths);
            this.FindControl<TextBox>("OutputPathBox")!.Text = Path.GetDirectoryName(existingPaths[0]) ?? "";
            SetStatus("Analyzing input files...");
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs e)
    {
        if (GetInputPaths().Length > 0)
        {
            await AnalyzeInputFilesAsync();
        }
    }

    private async void BrowseInput_Click(object? sender, RoutedEventArgs e) => await SelectInputFilesAsync(false);

    private async void AddInputFilesMenu_Click(object? sender, RoutedEventArgs e) => await SelectInputFilesAsync(true);

    private async Task SelectInputFilesAsync(bool append)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Matroska files",
            AllowMultiple = true,
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

        string[] selectedPaths = files.Select(file => file.Path.LocalPath).ToArray();
        string[] inputPaths = append
            ? GetInputPaths().Concat(selectedPaths).Distinct(StringComparer.Ordinal).ToArray()
            : selectedPaths;
        this.FindControl<TextBox>("InputPathBox")!.Text = string.Join(Environment.NewLine, inputPaths);
        TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
        if (string.IsNullOrWhiteSpace(outputBox.Text))
        {
            outputBox.Text = Path.GetDirectoryName(inputPaths[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        await AnalyzeInputFilesAsync();
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

    private async Task AnalyzeInputFilesAsync()
    {
        if (_isAnalyzing)
        {
            return;
        }

        string[] inputPaths = GetInputPaths();
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        if (inputPaths.Length == 0 || inputPaths.Any(inputPath => !File.Exists(inputPath)))
        {
            SetStatus("Select existing input files.");
            return;
        }

        if (!Directory.Exists(toolPath))
        {
            SetStatus("Select the folder containing MKVToolNix executables.");
            return;
        }

        _isAnalyzed = false;
        _isAnalyzing = true;
        _segmentRows.Clear();
        _inputFiles.Clear();
        this.FindControl<TextBlock>("ItemCountText")!.Text = "Analyzing...";
        SetBusy(true);
        try
        {
            for (int i = 0; i < inputPaths.Length; i++)
            {
                string inputPath = inputPaths[i];
                SetStatus($"Analyzing {i + 1} of {inputPaths.Length}: {Path.GetFileName(inputPath)}...");
                List<gMKVSegment> segments = await Task.Run(() => gMKVHelper.GetMergedMkvSegmentList(toolPath, inputPath));
                List<SegmentRow> fileRows = segments
                    .Where(IsExtractableSegment)
                    .Select(segment => new SegmentRow(inputPath, segment))
                    .ToList();
                foreach (SegmentRow row in fileRows)
                {
                    _segmentRows.Add(row);
                }

                _inputFiles.Add(new InputFileGroup(inputPath, fileRows));
            }

            this.FindControl<TextBlock>("ItemCountText")!.Text = $"{_segmentRows.Count} items";
            _isAnalyzed = true;
            this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = false;
            this.FindControl<ProgressBar>("FileProgressBar")!.Value = 0;
            this.FindControl<ProgressBar>("OverallProgressBar")!.Value = 0;
            this.FindControl<TextBlock>("FileProgressLabel")!.Text = "File progress";
            this.FindControl<TextBlock>("FileProgressPercent")!.Text = "0%";
            this.FindControl<TextBlock>("OverallProgressPercent")!.Text = "0%";
            SetStatus(_segmentRows.Count == 0 ? "No extractable tracks, chapters, or attachments found." : $"Analysis complete for {inputPaths.Length} file(s).");

            TextBox outputBox = this.FindControl<TextBox>("OutputPathBox")!;
            if (string.IsNullOrWhiteSpace(outputBox.Text))
            {
                outputBox.Text = Path.GetDirectoryName(inputPaths[0]) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Analysis failed: {ex.Message}");
        }
        finally
        {
            _isAnalyzing = false;
            SetBusy(false);
        }
    }

    private async void Extract_Click(object? sender, RoutedEventArgs e)
    {
        string[] inputPaths = GetInputPaths();
        string outputPath = this.FindControl<TextBox>("OutputPathBox")!.Text?.Trim() ?? "";
        string toolPath = this.FindControl<TextBox>("ToolPathBox")!.Text?.Trim() ?? "";
        if (inputPaths.Length == 0 || inputPaths.Any(inputPath => !File.Exists(inputPath)) || !Directory.Exists(toolPath))
        {
            SetStatus("Choose existing input files and a valid MKVToolNix folder first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Choose an output folder first.");
            return;
        }

        if (!_segmentRows.Any(row => row.IsSelected))
        {
            SetStatus("Select at least one item to extract.");
            return;
        }

        try
        {
            Directory.CreateDirectory(outputPath);
            List<(string InputPath, List<gMKVSegment> Segments)> extractionFiles = inputPaths
                .Select(inputPath => (
                    InputPath: inputPath,
                    Segments: _segmentRows
                        .Where(row => row.InputPath == inputPath && row.IsSelected)
                        .Select(row => row.Segment)
                        .ToList()))
                .Where(file => file.Segments.Count > 0)
                .ToList();

            _completedExtractionFiles = 0;
            _extractionFileCount = extractionFiles.Count;
            this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = _extractionFileCount > 1;

            var extractor = new gMKVExtract(toolPath);
            extractor.MkvExtractProgressUpdated += progress => Dispatcher.UIThread.Post(() => UpdateExtractionProgress(progress));
            extractor.MkvExtractTrackUpdated += (filename, trackName) => Dispatcher.UIThread.Post(() => SetStatus($"{Path.GetFileName(filename)}: {trackName}"));

            SetBusy(true);
            for (int i = 0; i < extractionFiles.Count; i++)
            {
                (string inputPath, List<gMKVSegment> selected) = extractionFiles[i];
                this.FindControl<TextBlock>("FileProgressLabel")!.Text = $"File progress: {Path.GetFileName(inputPath)}";
                UpdateExtractionProgress(0);

                var parameters = new gMKVExtractSegmentsParameters
                {
                    MKVFile = inputPath,
                    MKVSegmentsToExtract = selected,
                    OutputDirectory = outputPath,
                    ChapterType = (MkvChapterTypes)this.FindControl<ComboBox>("ChapterFormatBox")!.SelectedIndex,
                    FilenamePatterns = CreateFilenamePatterns(),
                    OverwriteExistingFile = this.FindControl<CheckBox>("OverwriteCheckBox")!.IsChecked == true
                };

                SetStatus($"Extracting {i + 1} of {extractionFiles.Count}: {Path.GetFileName(inputPath)}...");
                await Task.Run(() => extractor.ExtractMKVSegmentsThreaded(parameters));
                if (extractor.ThreadedException != null)
                {
                    throw extractor.ThreadedException;
                }

                _completedExtractionFiles++;
                UpdateExtractionProgress(100);
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

    private void UpdateExtractionProgress(int currentFileProgress)
    {
        int boundedProgress = Math.Clamp(currentFileProgress, 0, 100);
        double overallProgress = _extractionFileCount == 0
            ? 0
            : (_completedExtractionFiles + boundedProgress / 100d) / _extractionFileCount * 100;
        this.FindControl<ProgressBar>("FileProgressBar")!.Value = boundedProgress;
        this.FindControl<ProgressBar>("OverallProgressBar")!.Value = overallProgress;
        this.FindControl<TextBlock>("OverallProgressLabel")!.Text =
            $"Overall progress: {_completedExtractionFiles} of {_extractionFileCount} files";
        this.FindControl<TextBlock>("FileProgressPercent")!.Text = $"{boundedProgress}%";
        this.FindControl<TextBlock>("OverallProgressPercent")!.Text = $"{Math.Round(overallProgress)}%";
    }

    private void SelectNone_Click(object? sender, RoutedEventArgs e)
    {
        foreach (SegmentRow row in _segmentRows)
        {
            row.IsSelected = false;
        }
    }

    private void SelectByType_Click(object? sender, RoutedEventArgs e)
    {
        Button selectByTypeButton = this.FindControl<Button>("SelectByTypeButton")!;
        var menu = new ContextMenu();

        int selectedCount = _segmentRows.Count(row => row.IsSelected);
        menu.Items.Add(CreateSelectAllMenuItem($"Select All Extractable Elements ({_segmentRows.Count})", _segmentRows.ToList()));
        menu.Items.Add(CreateClearSelectionMenuItem($"Clear All Extractable Elements ({selectedCount})", _segmentRows.ToList()));
        menu.Items.Add(new Separator());

        AddTrackTypeMenu(menu, MkvTrackType.video, "Video");
        AddTrackTypeMenu(menu, MkvTrackType.audio, "Audio");
        AddTrackTypeMenu(menu, MkvTrackType.subtitles, "Subtitle");
        AddElementTypeMenu(menu, "Chapter Tracks", row => row.Segment is gMKVChapter);
        AddElementTypeMenu(menu, "Attachment Tracks", row => row.Segment is gMKVAttachment);

        menu.Open(selectByTypeButton);
    }

    private void AddTrackTypeMenu(ContextMenu menu, MkvTrackType trackType, string typeLabel)
    {
        string trackLabel = $"{typeLabel} Tracks";
        List<SegmentRow> rows = _segmentRows
            .Where(row => row.Segment is gMKVTrack track && track.TrackType == trackType)
            .ToList();
        int selectedCount = rows.Count(row => row.IsSelected);
        var trackMenu = new MenuItem
        {
            Header = $"{trackLabel} ({selectedCount}/{rows.Count})",
            IsEnabled = rows.Count > 0
        };

        trackMenu.Items.Add(CreateSelectAllMenuItem($"All {trackLabel} ({selectedCount}/{rows.Count})", rows));
        trackMenu.Items.Add(CreateClearSelectionMenuItem($"Clear {trackLabel} ({selectedCount})", rows));

        foreach ((string characteristic, Func<gMKVTrack, string> selector) in GetTrackCharacteristics(trackType))
        {
            List<(string Value, List<SegmentRow> Rows)> groups = rows
                .Select(row => (Row: row, Track: (gMKVTrack)row.Segment))
                .GroupBy(item => selector(item.Track) ?? "")
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => (group.Key, group.Select(item => item.Row).ToList()))
                .ToList();
            var characteristicMenu = new MenuItem
            {
                Header = $"{trackLabel} by {characteristic} ({groups.Count})...",
                IsEnabled = groups.Count > 0
            };

            foreach ((string value, List<SegmentRow> groupRows) in groups)
            {
                string displayValue = string.IsNullOrWhiteSpace(value) ? "(unspecified)" : value;
                int groupSelectedCount = groupRows.Count(row => row.IsSelected);
                characteristicMenu.Items.Add(CreateSelectAllMenuItem(
                    $"{displayValue} ({groupSelectedCount}/{groupRows.Count})",
                    groupRows));
            }

            trackMenu.Items.Add(characteristicMenu);
        }

        menu.Items.Add(trackMenu);
    }

    private static IReadOnlyList<(string Name, Func<gMKVTrack, string> Selector)> GetTrackCharacteristics(MkvTrackType trackType)
    {
        var characteristics = new List<(string Name, Func<gMKVTrack, string> Selector)>
        {
            ("Language", track => track.Language),
            ("Language IETF", track => track.LanguageIetf),
            ("Codec", track => track.CodecID),
            ("Track Name", track => track.TrackName),
            ("Forced", track => track.Forced ? "Yes" : "No")
        };

        if (trackType == MkvTrackType.video)
        {
            characteristics.Insert(2, ("Resolution", track => $"{track.VideoPixelWidth}x{track.VideoPixelHeight}"));
        }
        else if (trackType == MkvTrackType.audio)
        {
            characteristics.Insert(2, ("Channels", track => track.AudioChannels.ToString()));
        }

        return characteristics;
    }

    private void AddElementTypeMenu(ContextMenu menu, string typeLabel, Func<SegmentRow, bool> matches)
    {
        List<SegmentRow> rows = _segmentRows.Where(matches).ToList();
        int selectedCount = rows.Count(row => row.IsSelected);
        var elementMenu = new MenuItem
        {
            Header = $"{typeLabel} ({selectedCount}/{rows.Count})",
            IsEnabled = rows.Count > 0
        };

        elementMenu.Items.Add(CreateSelectAllMenuItem($"All {typeLabel} ({selectedCount}/{rows.Count})", rows));
        elementMenu.Items.Add(CreateClearSelectionMenuItem($"Clear {typeLabel} ({selectedCount})", rows));
        menu.Items.Add(elementMenu);
    }

    private static MenuItem CreateSelectAllMenuItem(string header, List<SegmentRow> rows)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = rows.Any(row => !row.IsSelected)
        };
        item.Click += (_, _) =>
        {
            foreach (SegmentRow row in rows)
            {
                row.IsSelected = true;
            }
        };
        return item;
    }

    private static MenuItem CreateClearSelectionMenuItem(string header, List<SegmentRow> rows)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = rows.Any(row => row.IsSelected)
        };
        item.Click += (_, _) =>
        {
            foreach (SegmentRow row in rows)
            {
                row.IsSelected = false;
            }
        };
        return item;
    }

    private void RemoveAllInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        this.FindControl<TextBox>("InputPathBox")!.Text = "";
    }

    private void RemoveSelectedInputFile_Click(object? sender, RoutedEventArgs e)
    {
        TextBox inputBox = this.FindControl<TextBox>("InputPathBox")!;
        string text = inputBox.Text ?? "";
        if (text.Length == 0)
        {
            return;
        }

        int selectionStart = Math.Clamp(inputBox.SelectionStart, 0, text.Length);
        int selectionEnd = Math.Clamp(inputBox.SelectionEnd, selectionStart, text.Length);
        int firstLineStart = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        int lastLinePosition = selectionEnd > selectionStart ? selectionEnd - 1 : selectionStart;
        int lineEnd = text.IndexOf('\n', lastLinePosition);
        int removeLength = (lineEnd < 0 ? text.Length : lineEnd + 1) - firstLineStart;
        inputBox.Text = text.Remove(firstLineStart, removeLength);
        inputBox.CaretIndex = Math.Min(firstLineStart, inputBox.Text.Length);
    }

    private void OpenSelectedInputFile_Click(object? sender, RoutedEventArgs e)
    {
        foreach (string inputPath in GetContextInputPaths().Where(File.Exists))
        {
            try
            {
                Process.Start(new ProcessStartInfo(inputPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus($"Could not open file: {ex.Message}");
                return;
            }
        }
    }

    private void OpenSelectedInputFolder_Click(object? sender, RoutedEventArgs e)
    {
        string? inputPath = GetContextInputPaths().FirstOrDefault(File.Exists);
        string? directory = inputPath == null ? null : Path.GetDirectoryName(inputPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open folder: {ex.Message}");
        }
    }

    private void ExpandInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.IsExpanded = true;
        }
    }

    private void CollapseInputFiles_Click(object? sender, RoutedEventArgs e)
    {
        foreach (InputFileGroup inputFile in _inputFiles)
        {
            inputFile.IsExpanded = false;
        }
    }

    private void InputPath_Changed(object? sender, TextChangedEventArgs e)
    {
        _isAnalyzed = false;
        _segmentRows.Clear();
        _inputFiles.Clear();
        this.FindControl<StackPanel>("OverallProgressPanel")!.IsVisible = false;
        this.FindControl<ProgressBar>("FileProgressBar")!.Value = 0;
        this.FindControl<ProgressBar>("OverallProgressBar")!.Value = 0;
        this.FindControl<TextBlock>("FileProgressLabel")!.Text = "File progress";
        this.FindControl<TextBlock>("FileProgressPercent")!.Text = "0%";
        this.FindControl<TextBlock>("OverallProgressPercent")!.Text = "0%";
        this.FindControl<TextBlock>("ItemCountText")!.Text = "No file analyzed";
        this.FindControl<Button>("ExtractButton")!.IsEnabled = false;
        this.FindControl<Button>("SelectByTypeButton")!.IsEnabled = false;
    }

    private string[] GetInputPaths() => (this.FindControl<TextBox>("InputPathBox")!.Text ?? "")
        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string[] GetContextInputPaths()
    {
        TextBox inputBox = this.FindControl<TextBox>("InputPathBox")!;
        string text = inputBox.Text ?? "";
        int selectionStart = Math.Clamp(inputBox.SelectionStart, 0, text.Length);
        int selectionEnd = Math.Clamp(inputBox.SelectionEnd, selectionStart, text.Length);
        int firstLineStart = selectionStart == 0 ? 0 : text.LastIndexOf('\n', selectionStart - 1) + 1;
        int lastLinePosition = selectionEnd > selectionStart ? selectionEnd - 1 : selectionStart;
        int lineEnd = text.IndexOf('\n', lastLinePosition);
        string selectedLines = text.Substring(firstLineStart, (lineEnd < 0 ? text.Length : lineEnd) - firstLineStart);
        return selectedLines.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private void SetBusy(bool isBusy)
    {
        this.FindControl<Button>("BrowseInputButton")!.IsEnabled = !isBusy;
        this.FindControl<TextBox>("InputPathBox")!.IsEnabled = !isBusy;
        this.FindControl<Button>("ExtractButton")!.IsEnabled = !isBusy && _isAnalyzed;
        this.FindControl<Button>("SelectByTypeButton")!.IsEnabled = !isBusy && _isAnalyzed;
    }

    private void SetStatus(string status) => this.FindControl<TextBlock>("StatusText")!.Text = status;

    private static bool IsExtractableSegment(gMKVSegment segment) => segment is gMKVTrack or gMKVChapter or gMKVAttachment;

    private static gMKVExtractFilenamePatterns CreateFilenamePatterns() => new()
    {
        VideoTrackFilenamePattern = "{FilenameNoExt}_track{TrackNumber}_[{Language}]",
        AudioTrackFilenamePattern = "{FilenameNoExt}_track{TrackNumber}_[{Language}]_DELAY {EffectiveDelay}ms",
        SubtitleTrackFilenamePattern = "{FilenameNoExt}_track{TrackNumber}_[{Language}]",
        ChapterFilenamePattern = "{FilenameNoExt}_chapters",
        AttachmentFilenamePattern = "{AttachmentFilename}",
        TagsFilenamePattern = "{FilenameNoExt}_tags"
    };
}

public sealed class SegmentRow : INotifyPropertyChanged
{
    private bool _isSelected;

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

    public SegmentRow(string inputPath, gMKVSegment segment)
    {
        InputPath = inputPath;
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

    public string InputPath { get; }
}

public sealed class InputFileGroup
{
    private bool _isExpanded = true;

    public string FileName { get; }
    public string ItemCount => $"{SegmentRows.Count} items";
    public ObservableCollection<SegmentRow> SegmentRows { get; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public InputFileGroup(string inputPath, IEnumerable<SegmentRow> segmentRows)
    {
        FileName = Path.GetFileName(inputPath);
        SegmentRows = new ObservableCollection<SegmentRow>(segmentRows);
    }
}