using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;

namespace YouTubeDlpGui.ViewModels;

public class FormatSelectionDialogViewModel : ViewModelBase
{
    private string _formatSelector = "";
    private YtDlpFormatOption? _selectedFormat;

    public ObservableCollection<YtDlpFormatOption> Formats { get; }

    public YtDlpFormatOption? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFormat, value);
            if (value != null) FormatSelector = value.FormatId;
        }
    }

    public string FormatSelector
    {
        get => _formatSelector;
        set => this.RaiseAndSetIfChanged(ref _formatSelector, value);
    }

    public ReactiveCommand<Unit, string?> UseFormatCommand { get; }
    public ReactiveCommand<Unit, string?> ClearCommand { get; }
    public ReactiveCommand<Unit, string?> CancelCommand { get; }

    public FormatSelectionDialogViewModel(ObservableCollection<YtDlpFormatOption> formats, string? currentSelector)
    {
        Formats = formats;
        _formatSelector = currentSelector ?? "";
        UseFormatCommand = ReactiveCommand.Create(() => string.IsNullOrWhiteSpace(FormatSelector) ? null : FormatSelector.Trim());
        ClearCommand = ReactiveCommand.Create<string?>(() => "");
        CancelCommand = ReactiveCommand.Create<string?>(() => null);
    }
}

public class YtDlpFormatOption
{
    public YtDlpFormatOption(string formatId, string details)
    {
        FormatId = formatId;
        Details = details;
    }

    public string FormatId { get; }
    public string Details { get; }
}
