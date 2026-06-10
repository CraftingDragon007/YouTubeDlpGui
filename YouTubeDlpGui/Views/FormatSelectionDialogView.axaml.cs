using Avalonia.Controls;
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
        viewModel.UseFormatCommand.Subscribe(result => Close(result));
        viewModel.ClearCommand.Subscribe(result => Close(result));
        viewModel.CancelCommand.Subscribe(result => Close(result));
    }
}
