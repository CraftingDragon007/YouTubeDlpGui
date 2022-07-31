using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using YouTubeDlpGui.Views;

namespace YouTubeDlpGui.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public void OnDownloadButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        if (instance.UrlTextBox.Text.Length == 0)
        {
            instance.UrlTextBox.Background = new SolidColorBrush(Colors.Red);
            return;
        }

        instance.UrlTextBox.Background = new SolidColorBrush(Colors.Black);
        if (instance.VideoDownloadPathTextBox.Text.Length == 0 ||
            !Directory.Exists(instance.VideoDownloadPathTextBox.Text))
        {
            instance.VideoDownloadPathTextBox.Background = new SolidColorBrush(Colors.Red);
            return;
        }

        instance.VideoDownloadPathTextBox.Background = new SolidColorBrush(Colors.Black);
        var thread = new Thread(() =>
        {
            var ytDlPDownloadUrl = GetYtDlpDownloadUrl();
            instance.YtDlPath = GetYtDlpPath();
            Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Downloading YT-Dlp...");
            DownloadFile(ytDlPDownloadUrl, instance.YtDlPath);
            if (Environment.OSVersion.Platform.Equals(PlatformID.Unix) ||
                Environment.OSVersion.Platform.Equals(PlatformID.MacOSX))
            {
                var chmod = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = "755 " + instance.YtDlPath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                chmod.Start();
                chmod.WaitForExit();
            }

            var ffmpegDownloadUrl = GetFfmpegDownloadUrl();
            var path = Path.Combine(Path.GetTempPath(),
                Environment.OSVersion.Platform.Equals(PlatformID.Unix) ? "ffmpeg.tar.xz" : "ffmpeg.zip");
            var ffmpegFolderPath = Path.Combine(Path.GetTempPath(), "ffmpeg");
            Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Downloading ffmpeg...");
            DownloadFile(ffmpegDownloadUrl, path);
            Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Extracting ffmpeg...");
            Console.WriteLine(path);
            Directory.CreateDirectory(ffmpegFolderPath);
            if (Environment.OSVersion.Platform.Equals(PlatformID.Win32NT) ||
                Environment.OSVersion.Platform.Equals(PlatformID.MacOSX))
            {
                ZipFile.ExtractToDirectory(path, ffmpegFolderPath);
            }
            else
            {
                ExtractTarArchive(path, ffmpegFolderPath);
                ffmpegFolderPath = Directory.GetDirectories(ffmpegFolderPath).First();
            }

            Console.WriteLine(ffmpegFolderPath);
            var ffmpegPath = Path.Combine(ffmpegFolderPath,
                Environment.OSVersion.Platform.Equals(PlatformID.Win32NT) ? "ffmpeg.exe" : "ffmpeg");
            if (!File.Exists(ffmpegPath))
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    instance.DownloadButton.IsEnabled = true;
                    instance.VideoDownloadPathTextBox.IsEnabled = true;
                    instance.UrlTextBox.IsEnabled = true;
                    instance.FinalFormatComboBox.IsEnabled = true;
                    instance.FormattingMethodComboBox.IsEnabled = true;
                    instance.VideoDownloadPathButton.IsEnabled = true;
                    instance.ErrorTextBlock.Text = "Failed to download and extract ffmpeg";
                });

            var format = "mp4";
            var formattingMethod = "remux";
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                format = instance.FinalFormatComboBox.SelectedItem == null
                    ? "mp4"
                    :
                    instance.FinalFormatComboBox.SelectedItem.GetType() == typeof(ComboBoxItem)
                        ?
                        ((ComboBoxItem) instance.FinalFormatComboBox.SelectedItem).Content.ToString()
                        : "mp4";
                formattingMethod = instance.FormattingMethodComboBox.SelectedIndex == 0 ? "remux" : "recode";
            }).Wait();
            FormattingMethod = formattingMethod;
            bool onlyAudio;
            switch (format)
            {
                case "ogg":
                case "flac":
                case "wav":
                case "aac":
                case "mp3":
                    onlyAudio = true;
                    break;
                default:
                    onlyAudio = false;
                    break;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = instance.YtDlPath,
                    Arguments =
                        $"-f {(onlyAudio ? "" : "bestvideo+")}bestaudio -o {"%(title)s.%(ext)s"} --ffmpeg-location {ffmpegPath} --{formattingMethod}-video {format} {instance.UrlTextBox.Text}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = instance.VideoDownloadPathTextBox.Text
                }
            };
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Dispatcher.UIThread.InvokeAsync(() => { instance.StatusLabel.Text = "Downloading..."; }).Wait();
            process.WaitForExit();
            process.Close();
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                instance.DownloadButton.IsEnabled = true;
                instance.VideoDownloadPathTextBox.IsEnabled = true;
                instance.UrlTextBox.IsEnabled = true;
                instance.FinalFormatComboBox.IsEnabled = true;
                instance.FormattingMethodComboBox.IsEnabled = true;
                instance.VideoDownloadPathButton.IsEnabled = true;
                instance.StatusLabel.Text = "Done! Ready to download next video!";
            });
        });

        thread.Start();
        instance.DownloadButton.Content = "Starting...";
        instance.DownloadButton.IsEnabled = false;
        instance.VideoDownloadPathTextBox.IsEnabled = false;
        instance.UrlTextBox.IsEnabled = false;
        instance.FinalFormatComboBox.IsEnabled = false;
        instance.FormattingMethodComboBox.IsEnabled = false;
        instance.VideoDownloadPathButton.IsEnabled = false;
    }

    private void ExtractTarArchive(string path, string ffmpegFolderPath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xvf {path} -C {ffmpegFolderPath}",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        process.WaitForExit();
        process.Close();
    }

    private string GetYtDlpDownloadUrl()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux",
            PlatformID.MacOSX => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos",
            PlatformID.Win32NT => "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_min.exe",
            _ => throw new PlatformNotSupportedException("This platform is not supported!")
        };
    }

    private string GetYtDlpPath()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => Path.Combine(Path.GetTempPath(), "yt-dlp_linux"),
            PlatformID.MacOSX => Path.Combine(Path.GetTempPath(), "yt-dlp_macos"),
            PlatformID.Win32NT => Path.Combine(Path.GetTempPath(), "yt-dlp_win.exe"),
            _ => throw new PlatformNotSupportedException("This platform is not supported!")
        };
    }

    private void DownloadFile(string url, string path)
    {
        if (File.Exists(path)) return;
        var client = new HttpClientDownloadWithProgress(url, path);
        client.ProgressChanged += DownloadFileClient_ProgressChanged;
        client.StartDownload().Wait();
    }

    private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        var instance = MainWindow.GetInstance();
        var text = e.Data;
        if (text == null) return;
        Console.WriteLine(text);
        if (text.Contains("Destination: "))
        {
            var destination = text[(text.IndexOf("Destination: ", StringComparison.Ordinal) + 14)..];
            instance.VideoName = destination;
        }

        if (text.Contains("[VideoConverter] "))
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                // make the progressbar animate
                instance.CurrentDownloadProgressBar.Value = instance.CurrentDownloadProgressBar.Maximum;
                instance.CurrentDownloadProgressBar.Value = 0;
                instance.CurrentDownloadProgressBar.IsIndeterminate = true;
                instance.StatusLabel.Text = FormattingMethod == "remux"
                    ? "Remuxing... (This may take a while)"
                    : "Re-Encoding... This may take a while! Please be patient!";
            });
        if (!text.Contains('%')) return;
        if (!text.Contains("ETA")) return;
        var percent = text.Split('%')[0].Replace("[download]", "").Replace(" ", "");
        var downloadSpeed = text.Split('%')[1].Split("at")[1].Split("ETA")[0].Replace(" ", "");
        var maxSize = text.Split('%')[1].Split(' ')[2];
        var eta = text.Split('%')[1].Split("ETA")[1].Replace(" ", "");
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            instance.CurrentDownloadProgressBar.Value = double.Parse(percent);
            instance.CurrentDownloadPercentageTextBlock.Text = percent + "%";
            instance.DownloadSpeedTextBlock.Text = downloadSpeed;
            instance.RemainingTimeTextBlock.Text = eta;
            instance.DownloadSizeTextBlock.Text = maxSize;
            instance.DownloadAtTextBlock.Text = "at";
            instance.DownloadEtaTextBlock.Text = "ETA";
        });
    }

    public string FormattingMethod { get; set; } = "remux";

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        var instance = MainWindow.GetInstance();
        var text = e.Data;
        if (text == null) return;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            instance.ErrorTextBlock.Text = text;
            instance.ErrorTextBlock.IsVisible = true;
        });
    }

    public async void OnVideoDownloadPathButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        var dialog = new OpenFolderDialog();
        var result = await dialog.ShowAsync(instance);
        if (result != null) instance.VideoDownloadPathTextBox.Text = result;
    }

    public string GetFfmpegDownloadUrl()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix => "https://johnvansickle.com/ffmpeg/builds/ffmpeg-git-amd64-static.tar.xz",
            PlatformID.MacOSX => "https://ffmpeg.zeranoe.com/builds/macos64/static/ffmpeg-latest-macos64-static.zip",
            PlatformID.Win32NT => "https://ffmpeg.zeranoe.com/builds/win64/static/ffmpeg-latest-win64-static.zip",
            _ => throw new PlatformNotSupportedException("This platform is not supported!")
        };
    }

    private void DownloadFileClient_ProgressChanged(long? totalfilesize, long totalbytesdownloaded,
        double? progresspercentage)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var instance = MainWindow.GetInstance();
            instance.CurrentDownloadProgressBar.Value = progresspercentage ?? 0;
            instance.CurrentDownloadPercentageTextBlock.Text = (progresspercentage ?? 0) + "%";
            instance.DownloadSizeTextBlock.Text = FormatBytes(totalfilesize ?? 0);
            instance.DownloadAtTextBlock.Text = "";
            instance.DownloadEtaTextBlock.Text = "";
            instance.RemainingTimeTextBlock.Text = "";
            instance.DownloadSpeedTextBlock.Text = "";
        });
    }

    public static string FormatBytes(long size)
    {
        if (size >= 1099511627776)
            return decimal.Round((decimal) (Math.Round(size / 10995116277.76) * 0.01), 2) + " TiB";
        if (size >= 1073741824)
            return decimal.Round((decimal) (Math.Round(size / 10737418.24) * 0.01), 2) + " GiB";
        if (size >= 1048576)
            return decimal.Round((decimal) (Math.Round(size / 10485.76) * 0.01), 2) + " MiB";
        if (size >= 1024)
            return decimal.Round((decimal) (Math.Round(size / 10.24) * 0.01), 2) + " KiB";
        return size + " bytes";
    }
}