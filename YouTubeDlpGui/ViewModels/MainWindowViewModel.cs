using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Threading;
using SharpCompress.Archives;
using YouTubeDlpGui.Views;

namespace YouTubeDlpGui.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    public void OnDownloadButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        var thread = new Thread(() =>
        {
            var ytDlPDownloadUrl = GetYtDlpDownloadUrl();
            instance.YtDlPath = GetYtDlpPath();
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

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = instance.YtDlPath,
                    Arguments = $"-f bestvideo[ext=mp4],bestaudio[ext=m4a] {instance.UrlTextBox.Text}",
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
            process.WaitForExit();
            process.Close();
            var ffmpegDownloadUrl = GetFfmpegDownloadUrl();
            var path = Path.GetTempFileName();
            var ffmpegFolderPath = Path.Combine(Path.GetTempPath(), "ffmpeg");
            DownloadFile(ffmpegDownloadUrl, path);
            Console.WriteLine(path);
            if (Environment.OSVersion.Platform.Equals(PlatformID.Win32NT) ||
                Environment.OSVersion.Platform.Equals(PlatformID.MacOSX))
            {
                ZipFile.ExtractToDirectory(path, ffmpegFolderPath);
            }
            else
            {
                SharpCompress.Archives.Tar.TarArchive.Open(path).WriteToDirectory(ffmpegFolderPath);
            }

            Console.WriteLine(ffmpegFolderPath);
        });
        thread.Start();
        instance.DownloadButton.IsEnabled = false;
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
            if (instance.VideoName == "")
                instance.VideoName = destination;
            else
                instance.AudioName = destination;
        }

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
    
    private void DownloadFileClient_ProgressChanged(long? totalfilesize, long totalbytesdownloaded, double? progresspercentage)
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
            return decimal.Round((decimal)(Math.Round(size / 10995116277.76) * 0.01), 2) + " TiB";
        if (size >= 1073741824)
            return decimal.Round((decimal)(Math.Round(size / 10737418.24) * 0.01), 2) + " GiB";
        if (size >= 1048576)
            return decimal.Round((decimal)(Math.Round(size / 10485.76) * 0.01), 2) + " MiB";
        if (size >= 1024)
            return decimal.Round((decimal)(Math.Round(size / 10.24) * 0.01), 2) + " KiB";
        return size + " bytes";
    }
}