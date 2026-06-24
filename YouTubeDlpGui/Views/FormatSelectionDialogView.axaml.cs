using Avalonia.Controls;
using Avalonia.Input;
using YouTubeDlpGui.ViewModels;
using System;

namespace YouTubeDlpGui.Views;

public partial class FormatSelectionDialogView : Window
{
    public FormatSelectionDialogView()
    {
        InitializeComponent();
    }

    public FormatSelectionDialogView(FormatSelectionDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.UseSelectionCommand.Subscribe(result => Close(result));
        viewModel.ClearCommand.Subscribe(result => Close(result));
        viewModel.CancelCommand.Subscribe(result => Close(result));
    }

    private void VideoFormatsListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if ((e.Source as Control)?.DataContext is not YtDlpFormatOption format) return;
        if (DataContext is not FormatSelectionDialogViewModel viewModel) return;
        if (!Equals(viewModel.SelectedVideoFormat, format)) return;

        viewModel.SelectedVideoFormat = null;
        e.Handled = true;
    }

    private void AudioFormatsListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if ((e.Source as Control)?.DataContext is not YtDlpFormatOption format) return;
        if (DataContext is not FormatSelectionDialogViewModel viewModel) return;
        if (!Equals(viewModel.SelectedAudioFormat, format)) return;

        viewModel.SelectedAudioFormat = null;
        e.Handled = true;
    }

    private void CombinedFormatsListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsRightClick(e)) return;
        if ((e.Source as Control)?.DataContext is not YtDlpFormatOption format) return;
        if (DataContext is not FormatSelectionDialogViewModel viewModel) return;
        if (!Equals(viewModel.SelectedCombinedFormat, format)) return;

        viewModel.SelectedCombinedFormat = null;
        viewModel.FormatSelector = "";
        viewModel.Summary = "Automatic";
        e.Handled = true;
    }

    private bool IsRightClick(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.RightButtonPressed;
    }
}
