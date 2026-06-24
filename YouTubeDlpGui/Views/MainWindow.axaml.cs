using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls;

namespace YouTubeDlpGui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        VideoDownloadPathTextBox.PlaceholderText = VideoDownloadPathTextBox.Text =
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        SystemYtDlpPath = FindExecutableOnPath(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "yt-dlp.exe" : "yt-dlp");
        SystemFfmpegPath = FindExecutableOnPath(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg");
        LoadPreferences();
        UseSystemYtDlpCheckBox.IsCheckedChanged += (_, _) => UpdateToolPreferenceControls();
        UseSystemFfmpegCheckBox.IsCheckedChanged += (_, _) => UpdateToolPreferenceControls();
        UpdateToolPreferenceControls();
        Closing += MainWindow_Closing;
        Instance = this;
    }

    public string YtDlPath { get; set; } = "";
    public string? SystemYtDlpPath { get; private set; }
    public string? SystemFfmpegPath { get; private set; }
    public string VideoName { get; set; } = "";
    public string AudioName { get; set; } = "";
    private static MainWindow? Instance { get; set; }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        SavePreferences();
    }

    public static MainWindow GetInstance()
    {
        return Instance ?? throw new InvalidOperationException("MainWindow not initialized");
    }

    private static string? FindExecutableOnPath(string executableName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return null;

        return path.Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.Combine(directory, executableName))
            .FirstOrDefault(File.Exists);
    }

    public void UpdateToolPreferenceControls()
    {
        UseSystemYtDlpCheckBox.IsEnabled = SystemYtDlpPath != null;
        if (SystemYtDlpPath == null) UseSystemYtDlpCheckBox.IsChecked = false;
        RedownloadYtDlpCheckBox.IsEnabled = UseSystemYtDlpCheckBox.IsChecked != true;
        if (UseSystemYtDlpCheckBox.IsChecked == true) RedownloadYtDlpCheckBox.IsChecked = false;

        UseSystemFfmpegCheckBox.IsEnabled = SystemFfmpegPath != null;
        if (SystemFfmpegPath == null) UseSystemFfmpegCheckBox.IsChecked = false;
        RedownloadFfmpegCheckBox.IsEnabled = UseSystemFfmpegCheckBox.IsChecked != true;
        if (UseSystemFfmpegCheckBox.IsChecked == true) RedownloadFfmpegCheckBox.IsChecked = false;
    }

    private void LoadPreferences()
    {
        var preferences = AppPreferences.Load();
        if (!string.IsNullOrWhiteSpace(preferences.VideoDownloadPath))
            VideoDownloadPathTextBox.Text = preferences.VideoDownloadPath;

        UseSystemYtDlpCheckBox.IsChecked = SystemYtDlpPath != null && preferences.UseSystemYtDlp;
        UseSystemFfmpegCheckBox.IsChecked = SystemFfmpegPath != null && preferences.UseSystemFfmpeg;
        RedownloadYtDlpCheckBox.IsChecked = preferences.RedownloadYtDlp;
        RedownloadFfmpegCheckBox.IsChecked = preferences.RedownloadFfmpeg;
        FinalFormatComboBox.SelectedIndex = preferences.FinalFormatIndex;
        FormattingMethodComboBox.SelectedIndex = preferences.FormattingMethodIndex;
        VideoEncodingComboBox.SelectedIndex = preferences.VideoEncodingIndex;
        AudioEncodingComboBox.SelectedIndex = preferences.AudioEncodingIndex;
    }

    private void SavePreferences()
    {
        var preferences = new AppPreferences
        {
            VideoDownloadPath = VideoDownloadPathTextBox.Text ?? "",
            UseSystemYtDlp = UseSystemYtDlpCheckBox.IsChecked == true,
            UseSystemFfmpeg = UseSystemFfmpegCheckBox.IsChecked == true,
            RedownloadYtDlp = RedownloadYtDlpCheckBox.IsChecked == true,
            RedownloadFfmpeg = RedownloadFfmpegCheckBox.IsChecked == true,
            FinalFormatIndex = FinalFormatComboBox.SelectedIndex,
            FormattingMethodIndex = FormattingMethodComboBox.SelectedIndex,
            VideoEncodingIndex = VideoEncodingComboBox.SelectedIndex,
            AudioEncodingIndex = AudioEncodingComboBox.SelectedIndex
        };
        preferences.Save();
    }
}

public class AppPreferences
{
    public string VideoDownloadPath { get; set; } = "";
    public bool UseSystemYtDlp { get; set; } = true;
    public bool UseSystemFfmpeg { get; set; } = true;
    public bool RedownloadYtDlp { get; set; }
    public bool RedownloadFfmpeg { get; set; }
    public int FinalFormatIndex { get; set; }
    public int FormattingMethodIndex { get; set; }
    public int VideoEncodingIndex { get; set; }
    public int AudioEncodingIndex { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YouTubeDlpGui",
        "preferences.json");

    public static AppPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppPreferences();
            return JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath)) ?? new AppPreferences();
        }
        catch (Exception)
        {
            return new AppPreferences();
        }
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (directory != null) Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
