using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using ReactiveUI;

namespace YouTubeDlpGui.ViewModels;

public class FormatSelectionDialogViewModel : ViewModelBase
{
    private string _formatSelector = "";
    private string _summary = "Automatic";
    private YtDlpFormatOption? _selectedVideoFormat;
    private YtDlpFormatOption? _selectedAudioFormat;
    private YtDlpFormatOption? _selectedCombinedFormat;

    public ObservableCollection<YtDlpFormatOption> VideoFormats { get; }
    public ObservableCollection<YtDlpFormatOption> AudioFormats { get; }
    public ObservableCollection<YtDlpFormatOption> CombinedFormats { get; }

    public YtDlpFormatOption? SelectedVideoFormat
    {
        get => _selectedVideoFormat;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedVideoFormat, value);
            if (value != null)
            {
                SelectedCombinedFormat = null;
                UpdateSelectorFromSeparateStreams();
            }
        }
    }

    public YtDlpFormatOption? SelectedAudioFormat
    {
        get => _selectedAudioFormat;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAudioFormat, value);
            if (value != null)
            {
                SelectedCombinedFormat = null;
                UpdateSelectorFromSeparateStreams();
            }
        }
    }

    public YtDlpFormatOption? SelectedCombinedFormat
    {
        get => _selectedCombinedFormat;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCombinedFormat, value);
            if (value == null) return;

            _selectedVideoFormat = null;
            _selectedAudioFormat = null;
            this.RaisePropertyChanged(nameof(SelectedVideoFormat));
            this.RaisePropertyChanged(nameof(SelectedAudioFormat));
            FormatSelector = value.FormatId;
            Summary = value.DisplayName;
        }
    }

    public string FormatSelector
    {
        get => _formatSelector;
        set => this.RaiseAndSetIfChanged(ref _formatSelector, value);
    }

    public string Summary
    {
        get => _summary;
        set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    public ReactiveCommand<Unit, FormatSelectionResult?> UseSelectionCommand { get; }
    public ReactiveCommand<Unit, FormatSelectionResult?> ClearCommand { get; }
    public ReactiveCommand<Unit, FormatSelectionResult?> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> BestQualityCommand { get; }
    public ReactiveCommand<Unit, Unit> BestCompatibleCommand { get; }
    public ReactiveCommand<Unit, Unit> Max1080Command { get; }
    public ReactiveCommand<Unit, Unit> BestAudioCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearVideoCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearAudioCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCombinedCommand { get; }

    public FormatSelectionDialogViewModel(ObservableCollection<YtDlpFormatOption> formats, string? currentSelector)
    {
        VideoFormats = new ObservableCollection<YtDlpFormatOption>(formats
            .Where(format => format.IsVideoOnly)
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.Fps ?? 0)
            .ThenByDescending(format => format.TotalBitrate ?? 0));
        AudioFormats = new ObservableCollection<YtDlpFormatOption>(formats
            .Where(format => format.IsAudioOnly)
            .OrderByDescending(format => format.AudioBitrate ?? format.TotalBitrate ?? 0));
        CombinedFormats = new ObservableCollection<YtDlpFormatOption>(formats
            .Where(format => format.IsCombined)
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.Fps ?? 0)
            .ThenByDescending(format => format.TotalBitrate ?? 0));

        _formatSelector = currentSelector ?? "";
        _summary = string.IsNullOrWhiteSpace(currentSelector) ? "Automatic" : $"Custom: {currentSelector}";

        UseSelectionCommand = ReactiveCommand.Create(() => string.IsNullOrWhiteSpace(FormatSelector)
            ? null
            : new FormatSelectionResult(FormatSelector.Trim(), Summary));
        ClearCommand = ReactiveCommand.Create<FormatSelectionResult?>(() => new FormatSelectionResult("", "Automatic"));
        CancelCommand = ReactiveCommand.Create<FormatSelectionResult?>(() => null);
        BestQualityCommand = ReactiveCommand.Create(() => SetPreset("bestvideo+bestaudio/best", "Best quality"));
        BestCompatibleCommand = ReactiveCommand.Create(() => SetPreset(
            "bestvideo[vcodec^=avc1]+bestaudio[acodec^=mp4a]/best[vcodec^=avc1][acodec^=mp4a]/best",
            "Best compatible (H.264 + AAC)"));
        Max1080Command = ReactiveCommand.Create(() => SetPreset(
            "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            "Best up to 1080p"));
        BestAudioCommand = ReactiveCommand.Create(() => SetPreset("bestaudio", "Best audio"));
        ClearVideoCommand = ReactiveCommand.Create(() =>
        {
            SelectedVideoFormat = null;
            UpdateSelectorFromSeparateStreams();
        });
        ClearAudioCommand = ReactiveCommand.Create(() =>
        {
            SelectedAudioFormat = null;
            UpdateSelectorFromSeparateStreams();
        });
        ClearCombinedCommand = ReactiveCommand.Create(() =>
        {
            SelectedCombinedFormat = null;
            FormatSelector = "";
            Summary = "Automatic";
        });
    }

    private void UpdateSelectorFromSeparateStreams()
    {
        if (SelectedVideoFormat != null && SelectedAudioFormat != null)
        {
            FormatSelector = SelectedVideoFormat.FormatId + "+" + SelectedAudioFormat.FormatId;
            Summary = SelectedVideoFormat.DisplayName + " + " + SelectedAudioFormat.DisplayName;
            return;
        }

        if (SelectedVideoFormat != null)
        {
            FormatSelector = SelectedVideoFormat.FormatId + "+bestaudio";
            Summary = SelectedVideoFormat.DisplayName + " + best audio";
            return;
        }

        if (SelectedAudioFormat != null)
        {
            FormatSelector = SelectedAudioFormat.FormatId;
            Summary = SelectedAudioFormat.DisplayName;
            return;
        }

        FormatSelector = "";
        Summary = "Automatic";
    }

    private void SetPreset(string selector, string summary)
    {
        SelectedCombinedFormat = null;
        _selectedVideoFormat = null;
        _selectedAudioFormat = null;
        this.RaisePropertyChanged(nameof(SelectedVideoFormat));
        this.RaisePropertyChanged(nameof(SelectedAudioFormat));
        FormatSelector = selector;
        Summary = summary;
    }
}

public record FormatSelectionResult(string Selector, string Summary);

public class YtDlpFormatOption
{
    public string FormatId { get; set; } = "";
    public string Extension { get; set; } = "";
    public string Resolution { get; set; } = "";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public double? Fps { get; set; }
    public string VideoCodec { get; set; } = "";
    public string AudioCodec { get; set; } = "";
    public double? TotalBitrate { get; set; }
    public double? AudioBitrate { get; set; }
    public long? FileSize { get; set; }
    public string Protocol { get; set; } = "";

    public bool IsVideoOnly => HasVideo && !HasAudio;
    public bool IsAudioOnly => HasAudio && !HasVideo;
    public bool IsCombined => HasVideo && HasAudio;

    public string DisplayName
    {
        get
        {
            if (IsAudioOnly)
                return $"Audio {AudioCodecLabel} {BitrateLabel} {FileSizeLabel}".Trim();

            var type = IsCombined ? "Combined" : "Video";
            return $"{type} {ResolutionLabel} {FpsLabel} {VideoCodecLabel} {BitrateLabel} {FileSizeLabel}".Trim();
        }
    }

    public string DetailLine => $"ID {FormatId} | {Extension} | {ProtocolLabel} | v: {VideoCodecLabel} | a: {AudioCodecLabel}";

    private bool HasVideo => !string.IsNullOrWhiteSpace(VideoCodec) && VideoCodec != "none";
    private bool HasAudio => !string.IsNullOrWhiteSpace(AudioCodec) && AudioCodec != "none";
    private string ResolutionLabel => !string.IsNullOrWhiteSpace(Resolution) ? Resolution : Height != null ? Height + "p" : "unknown resolution";
    private string FpsLabel => Fps != null ? Fps + "fps" : "";
    private string VideoCodecLabel => string.IsNullOrWhiteSpace(VideoCodec) || VideoCodec == "none" ? "no video" : VideoCodec;
    private string AudioCodecLabel => string.IsNullOrWhiteSpace(AudioCodec) || AudioCodec == "none" ? "no audio" : AudioCodec;
    private string BitrateLabel => TotalBitrate != null ? Math.Round(TotalBitrate.Value) + "k" : AudioBitrate != null ? Math.Round(AudioBitrate.Value) + "k" : "";
    private string ProtocolLabel => string.IsNullOrWhiteSpace(Protocol) ? "unknown protocol" : Protocol;
    private string FileSizeLabel => FileSize != null ? MainWindowViewModel.FormatBytes(FileSize.Value) : "";
}
