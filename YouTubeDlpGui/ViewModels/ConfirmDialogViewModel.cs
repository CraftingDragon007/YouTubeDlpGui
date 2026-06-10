using System;
using System.Reactive;
using ReactiveUI;
using YouTubeDlpGui.ViewModels;

namespace YouTubeDlpGui.ViewModels;

public class ConfirmDialogViewModel : ViewModelBase
{
    private string _message;
    public string Message
    {
        get => _message;
        set => this.RaiseAndSetIfChanged(ref _message, value);
    }

    public ReactiveCommand<Unit, bool> YesCommand { get; }
    public ReactiveCommand<Unit, bool> NoCommand { get; }

    public ConfirmDialogViewModel(string message, Action<bool> callback)
    {
        _message = message;
        YesCommand = ReactiveCommand.Create(() => true);
        YesCommand.Subscribe(result => callback(result));
        NoCommand = ReactiveCommand.Create(() => false);
        NoCommand.Subscribe(result => callback(result));
    }
}
