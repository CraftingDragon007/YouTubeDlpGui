using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Controls;

namespace YouTubeDlpGui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VideoDownloadPathTextBox.Watermark = VideoDownloadPathTextBox.Text =
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        Closing += MainWindow_Closing;
        Instance = this;
    }

    public string YtDlPath { get; set; } = "";
    public string VideoName { get; set; } = "";
    public string AudioName { get; set; } = "";
    private static MainWindow? Instance { get; set; }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        try
        {
            File.Delete(YtDlPath);
        }
        catch (Exception)
        {
            //ignored
        }
    }

    public static MainWindow GetInstance()
    {
        return Instance ?? throw new InvalidOperationException("MainWindow not initialized");
    }
}