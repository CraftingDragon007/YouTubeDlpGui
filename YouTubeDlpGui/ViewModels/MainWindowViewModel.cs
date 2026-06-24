using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using ReactiveUI;
using YouTubeDlpGui.Views;

namespace YouTubeDlpGui.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private string? _customFormatSelector;
    private string _selectedDetailedFormatText = "Automatic";

    public string SelectedDetailedFormatText
    {
        get => _selectedDetailedFormatText;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailedFormatText, value);
    }

    public void OnDownloadButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        if (string.IsNullOrWhiteSpace(instance.UrlTextBox.Text))
        {
            instance.UrlTextBox.Background = new SolidColorBrush(Colors.Red);
            return;
        }

        instance.UrlTextBox.Background = new SolidColorBrush(Colors.Black);
        if (string.IsNullOrWhiteSpace(instance.VideoDownloadPathTextBox.Text) ||
            !Directory.Exists(instance.VideoDownloadPathTextBox.Text))
        {
            instance.VideoDownloadPathTextBox.Background = new SolidColorBrush(Colors.Red);
            return;
        }

        instance.VideoDownloadPathTextBox.Background = new SolidColorBrush(Colors.Black);
        var useSystemYtDlp = instance.UseSystemYtDlpCheckBox.IsChecked == true;
        var useSystemFfmpeg = instance.UseSystemFfmpegCheckBox.IsChecked == true;
        var redownloadYtDlp = instance.RedownloadYtDlpCheckBox.IsChecked == true;
        var redownloadFfmpeg = instance.RedownloadFfmpegCheckBox.IsChecked == true;
        var thread = new Thread(() =>
        {
            try
            {
            instance.YtDlPath = EnsureYtDlpPath(instance, useSystemYtDlp, redownloadYtDlp);
            var ffmpegPath = EnsureFfmpegPath(instance, useSystemFfmpeg, redownloadFfmpeg);
            
            if (!File.Exists(ffmpegPath))
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    instance.DownloadButton.IsEnabled = true;
                    instance.VideoDownloadPathTextBox.IsEnabled = true;
                    instance.UrlTextBox.IsEnabled = true;
                    instance.FinalFormatComboBox.IsEnabled = true;
                    instance.FormattingMethodComboBox.IsEnabled = true;
                    instance.VideoEncodingComboBox.IsEnabled = true;
                    instance.AudioEncodingComboBox.IsEnabled = true;
                    instance.VideoDownloadPathButton.IsEnabled = true;
                    instance.SelectedDetailedFormatTextBox.IsEnabled = true;
                    instance.FormatSelectionButton.IsEnabled = true;
                    instance.UpdateToolPreferenceControls();
                    instance.ErrorTextBlock.Text = "Failed to download and extract ffmpeg";
                });
                return;
            }

            var format = "mp4";
            var formattingMethod = "auto";
            var customFormatSelector = _customFormatSelector;
            string videoEncoding = "Auto";
            string audioEncoding = "Auto";
            string url = "";
            string downloadPath = "";
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                format = instance.FinalFormatComboBox.SelectedItem == null
                    ? "mp4"
                    : instance.FinalFormatComboBox.SelectedItem.GetType() == typeof(ComboBoxItem)
                        ? ((ComboBoxItem) instance.FinalFormatComboBox.SelectedItem).Content?.ToString() ?? "mp4"
                        : "mp4";
                formattingMethod = instance.FormattingMethodComboBox.SelectedIndex switch
                {
                    1 => "remux",
                    2 => "recode",
                    _ => "auto"
                };
                videoEncoding = GetSelectedComboBoxText(instance.VideoEncodingComboBox, "Auto");
                audioEncoding = GetSelectedComboBoxText(instance.AudioEncodingComboBox, "Auto");
                url = instance.UrlTextBox.Text ?? "";
                downloadPath = instance.VideoDownloadPathTextBox.Text ?? "";
            }).Wait();

            var processGetFilename = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = instance.YtDlPath,
                    Arguments = $"--get-filename -o {Quote("%(title)s.%(ext)s")} {Quote(url)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = downloadPath
                }
            };
            processGetFilename.Start();
            var fileName = processGetFilename.StandardOutput.ReadToEnd().Trim();
            processGetFilename.WaitForExit();

            var existingFilePath = GetExistingOutputFilePath(downloadPath, fileName, format);
            if (existingFilePath != null)
            {
                var tcs = new TaskCompletionSource<bool>();

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var dialog = new ConfirmDialogView();
                    var vm = new ConfirmDialogViewModel($"{Path.GetFileName(existingFilePath)} already exists. Replace?", (result) =>
                    {
                        tcs.TrySetResult(result);
                        dialog.Close();
                    });
                    dialog.DataContext = vm;
                    dialog.Closed += (_, _) => tcs.TrySetResult(false);
                    dialog.ShowDialog(MainWindow.GetInstance());
                });

                if (!tcs.Task.Result)
                {
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        instance.DownloadButton.IsEnabled = true;
                        instance.VideoDownloadPathTextBox.IsEnabled = true;
                        instance.UrlTextBox.IsEnabled = true;
                        instance.FinalFormatComboBox.IsEnabled = true;
                        instance.FormattingMethodComboBox.IsEnabled = true;
                        instance.VideoEncodingComboBox.IsEnabled = true;
                        instance.AudioEncodingComboBox.IsEnabled = true;
                        instance.VideoDownloadPathButton.IsEnabled = true;
                        instance.SelectedDetailedFormatTextBox.IsEnabled = true;
                        instance.FormatSelectionButton.IsEnabled = true;
                        instance.UpdateToolPreferenceControls();
                        instance.StatusLabel.Text = "Ready";
                    });
                    return;
                }
                File.Delete(existingFilePath);
            }
            
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

            var formatSelector = GetFormatSelector(onlyAudio, customFormatSelector);
            var downloadWorkDirectory = Path.Combine(Path.GetTempPath(), "YouTubeDlpGui", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(downloadWorkDirectory);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = instance.YtDlPath,
                    Arguments =
                        $"-f {Quote(formatSelector)} --merge-output-format mkv -o {Quote("%(title)s.%(ext)s")} --ffmpeg-location {Quote(ffmpegPath)} {Quote(url)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = downloadWorkDirectory
                }
            };
            process.OutputDataReceived += Process_OutputDataReceived;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            Dispatcher.UIThread.InvokeAsync(() => { instance.StatusLabel.Text = "Downloading..."; }).Wait();
            process.WaitForExit();
            var downloadExitCode = process.ExitCode;
            process.Close();
            if (downloadExitCode != 0) throw new InvalidOperationException("Failed to download the selected format.");

            var downloadedFilePath = GetDownloadedFilePath(downloadWorkDirectory, fileName);

            var finalFilePath = Path.Combine(downloadPath, Path.ChangeExtension(fileName, format));
            RunFfmpegProcessing(ffmpegPath, downloadedFilePath, finalFilePath, format, formattingMethod, onlyAudio,
                videoEncoding, audioEncoding);

            try
            {
                Directory.Delete(downloadWorkDirectory, true);
            }
            catch (Exception)
            {
                // Temporary files are best-effort cleanup only.
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                instance.DownloadButton.IsEnabled = true;
                instance.VideoDownloadPathTextBox.IsEnabled = true;
                instance.UrlTextBox.IsEnabled = true;
                instance.FinalFormatComboBox.IsEnabled = true;
                instance.FormattingMethodComboBox.IsEnabled = true;
                instance.VideoEncodingComboBox.IsEnabled = true;
                instance.AudioEncodingComboBox.IsEnabled = true;
                instance.VideoDownloadPathButton.IsEnabled = true;
                instance.SelectedDetailedFormatTextBox.IsEnabled = true;
                instance.FormatSelectionButton.IsEnabled = true;
                instance.UpdateToolPreferenceControls();
                instance.CurrentDownloadProgressBar.IsIndeterminate = false;
                ResetDownloadProgressLabels(instance);
                instance.ErrorTextBlock.Text = "";
                instance.UrlTextBox.Text = "";
                instance.StatusLabel.Text = "Done! Ready to download next video!";
            });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RestoreDownloadControls(instance);
                    instance.CurrentDownloadProgressBar.IsIndeterminate = false;
                    ResetDownloadProgressLabels(instance);
                    instance.ErrorTextBlock.Text = ex.Message;
                    instance.ErrorTextBlock.IsVisible = true;
                    instance.StatusLabel.Text = "Ready";
                });
            }
        });

        thread.Start();
        instance.StatusLabel.Text = "Starting...";
        instance.DownloadButton.IsEnabled = false;
        instance.VideoDownloadPathTextBox.IsEnabled = false;
        instance.UrlTextBox.IsEnabled = false;
        instance.FinalFormatComboBox.IsEnabled = false;
        instance.FormattingMethodComboBox.IsEnabled = false;
        instance.VideoEncodingComboBox.IsEnabled = false;
        instance.AudioEncodingComboBox.IsEnabled = false;
        instance.VideoDownloadPathButton.IsEnabled = false;
        instance.SelectedDetailedFormatTextBox.IsEnabled = false;
        instance.FormatSelectionButton.IsEnabled = false;
        instance.UseSystemYtDlpCheckBox.IsEnabled = false;
        instance.UseSystemFfmpegCheckBox.IsEnabled = false;
        instance.RedownloadYtDlpCheckBox.IsEnabled = false;
        instance.RedownloadFfmpegCheckBox.IsEnabled = false;
    }

    public async void OnSelectFormatButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        if (string.IsNullOrWhiteSpace(instance.UrlTextBox.Text))
        {
            instance.UrlTextBox.Background = new SolidColorBrush(Colors.Red);
            return;
        }

        instance.UrlTextBox.Background = new SolidColorBrush(Colors.Black);
        instance.StatusLabel.Text = "Loading formats...";

        try
        {
            instance.YtDlPath = EnsureYtDlpPath(
                instance,
                instance.UseSystemYtDlpCheckBox.IsChecked == true,
                instance.RedownloadYtDlpCheckBox.IsChecked == true);

            var formats = GetFormats(instance.YtDlPath, instance.UrlTextBox.Text ?? "");
            var dialogViewModel = new FormatSelectionDialogViewModel(formats, _customFormatSelector);
            var dialog = new FormatSelectionDialogView(dialogViewModel);
            var selectedFormat = await dialog.ShowDialog<FormatSelectionResult?>(instance);

            if (selectedFormat != null)
            {
                _customFormatSelector = selectedFormat.Selector.Length == 0 ? null : selectedFormat.Selector;
                SelectedDetailedFormatText = selectedFormat.Summary;
            }

            instance.StatusLabel.Text = "Ready";
        }
        catch (Exception ex)
        {
            instance.ErrorTextBlock.Text = ex.Message;
            instance.ErrorTextBlock.IsVisible = true;
            instance.StatusLabel.Text = "Ready";
        }
    }

    private string EnsureYtDlpPath(MainWindow instance, bool useSystemYtDlp, bool redownloadYtDlp)
    {
        if (useSystemYtDlp && instance.SystemYtDlpPath != null)
            return instance.SystemYtDlpPath;

        var ytDlpPath = GetYtDlpPath();
        var forceDownload = redownloadYtDlp;
        if (forceDownload)
        {
            DeleteFileIfExists(ytDlpPath);
            DeleteFileIfExists(Path.Combine(Path.GetTempPath(), "yt-dlp.zip"));
            DeleteDirectoryIfExists(Path.Combine(Path.GetTempPath(), "yt-dlp"));
        }

        if (forceDownload || !File.Exists(ytDlpPath))
        {
            var ytDlPDownloadUrl = GetYtDlpDownloadUrl();
            Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Downloading YT-Dlp...");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ytDlpPath = DownloadYtDlpWindows(ytDlPDownloadUrl, Path.Combine(Path.GetTempPath(), "yt-dlp.zip"), forceDownload);
            else
                DownloadFile(ytDlPDownloadUrl, ytDlpPath, forceDownload);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var chmod = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = "755 " + ytDlpPath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            chmod.Start();
            chmod.WaitForExit();
        }

        return ytDlpPath;
    }

    private string EnsureFfmpegPath(MainWindow instance, bool useSystemFfmpeg, bool redownloadFfmpeg)
    {
        if (useSystemFfmpeg && instance.SystemFfmpegPath != null)
            return instance.SystemFfmpegPath;

        var ffmpegDownloadUrl = GetFfmpegDownloadUrl();
        var path = Path.Combine(Path.GetTempPath(),
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg.tar.xz" : "ffmpeg.zip");
        var ffmpegFolderPath = Path.Combine(Path.GetTempPath(), "ffmpeg");
        var ffmpegExeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        var forceDownload = redownloadFfmpeg;

        if (forceDownload)
        {
            DeleteFileIfExists(path);
            DeleteDirectoryIfExists(ffmpegFolderPath);
        }

        if (!forceDownload && Directory.Exists(ffmpegFolderPath))
        {
            var existingFfmpegPath = FindFile(ffmpegFolderPath, ffmpegExeName);
            if (existingFfmpegPath != null) return existingFfmpegPath;
        }

        Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Downloading ffmpeg...");
        DownloadFile(ffmpegDownloadUrl, path, forceDownload);
        Dispatcher.UIThread.InvokeAsync(() => instance.StatusLabel.Text = "Extracting ffmpeg...");
        Directory.CreateDirectory(ffmpegFolderPath);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ZipFile.ExtractToDirectory(path, ffmpegFolderPath, true);
            ffmpegFolderPath = Directory.GetDirectories(Directory.GetDirectories(ffmpegFolderPath).First()).First();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ExtractTarArchive(path, ffmpegFolderPath);
            ffmpegFolderPath = Directory.GetDirectories(ffmpegFolderPath).First();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            ZipFile.ExtractToDirectory(path, ffmpegFolderPath, true);

        return FindFile(ffmpegFolderPath, ffmpegExeName) ?? Path.Combine(ffmpegFolderPath, ffmpegExeName);
    }

    private string DownloadYtDlpWindows(string ytDlPDownloadUrl, string path, bool forceDownload = false)
    {
        DownloadFile(ytDlPDownloadUrl, path, forceDownload);
        var folderPath = Path.Combine(Path.GetTempPath(), "yt-dlp");
        var exePath = Path.Combine(folderPath, "yt-dlp.exe");
        if (forceDownload || !File.Exists(exePath))
        {
            ZipFile.ExtractToDirectory(path, folderPath, true);
        }
        return exePath;
    }

    private static string? GetExistingOutputFilePath(string downloadPath, string ytDlpFileName, string format)
    {
        var rawFilePath = Path.Combine(downloadPath, ytDlpFileName);
        if (File.Exists(rawFilePath)) return rawFilePath;

        var finalFilePath = Path.Combine(downloadPath, Path.ChangeExtension(ytDlpFileName, format));
        if (File.Exists(finalFilePath)) return finalFilePath;

        return null;
    }

    private static string GetDownloadedFilePath(string downloadWorkDirectory, string fileName)
    {
        var expectedFilePath = Path.Combine(downloadWorkDirectory, fileName);
        if (File.Exists(expectedFilePath)) return expectedFilePath;

        var mergedFilePath = Path.Combine(downloadWorkDirectory, Path.ChangeExtension(fileName, "mkv"));
        if (File.Exists(mergedFilePath)) return mergedFilePath;

        var newestFilePath = Directory.GetFiles(downloadWorkDirectory)
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        if (newestFilePath != null) return newestFilePath;

        throw new FileNotFoundException("yt-dlp completed but no downloaded file was found.", expectedFilePath);
    }

    private static string GetFormatSelector(bool onlyAudio, string? customFormatSelector)
    {
        return string.IsNullOrWhiteSpace(customFormatSelector)
            ? (onlyAudio ? "bestaudio" : "bestvideo+bestaudio")
            : customFormatSelector.Trim();
    }

    private static string GetSelectedComboBoxText(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem is ComboBoxItem item
            ? item.Content?.ToString() ?? fallback
            : fallback;
    }

    private static void RestoreDownloadControls(MainWindow instance)
    {
        instance.DownloadButton.IsEnabled = true;
        instance.VideoDownloadPathTextBox.IsEnabled = true;
        instance.UrlTextBox.IsEnabled = true;
        instance.FinalFormatComboBox.IsEnabled = true;
        instance.FormattingMethodComboBox.IsEnabled = true;
        instance.VideoEncodingComboBox.IsEnabled = true;
        instance.AudioEncodingComboBox.IsEnabled = true;
        instance.VideoDownloadPathButton.IsEnabled = true;
        instance.SelectedDetailedFormatTextBox.IsEnabled = true;
        instance.FormatSelectionButton.IsEnabled = true;
        instance.UpdateToolPreferenceControls();
    }

    private static ObservableCollection<YtDlpFormatOption> GetFormats(string ytDlpPath, string url)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ytDlpPath,
                Arguments = $"-J {Quote(url)}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());

        return ParseFormatsFromJson(output);
    }

    private static ObservableCollection<YtDlpFormatOption> ParseFormatsFromJson(string json)
    {
        var formats = new ObservableCollection<YtDlpFormatOption>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("formats", out var formatElements)) return formats;

        foreach (var formatElement in formatElements.EnumerateArray())
        {
            var formatId = GetJsonString(formatElement, "format_id");
            if (string.IsNullOrWhiteSpace(formatId)) continue;

            formats.Add(new YtDlpFormatOption
            {
                FormatId = formatId,
                Extension = GetJsonString(formatElement, "ext"),
                Resolution = GetJsonString(formatElement, "resolution"),
                Width = GetJsonInt(formatElement, "width"),
                Height = GetJsonInt(formatElement, "height"),
                Fps = GetJsonDouble(formatElement, "fps"),
                VideoCodec = GetJsonString(formatElement, "vcodec"),
                AudioCodec = GetJsonString(formatElement, "acodec"),
                TotalBitrate = GetJsonDouble(formatElement, "tbr"),
                AudioBitrate = GetJsonDouble(formatElement, "abr"),
                FileSize = GetJsonLong(formatElement, "filesize") ?? GetJsonLong(formatElement, "filesize_approx"),
                Protocol = GetJsonString(formatElement, "protocol")
            });
        }

        return formats;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)) return "";
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.ToString();
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.TryGetInt32(out var value) ? value : null;
    }

    private static long? GetJsonLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.TryGetInt64(out var value) ? value : null;
    }

    private static double? GetJsonDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        return property.TryGetDouble(out var value) ? value : null;
    }

    private void RunFfmpegProcessing(string ffmpegPath, string inputPath, string outputPath, string targetContainer,
        string formattingMethod, bool onlyAudio, string videoEncoding, string audioEncoding)
    {
        if (!File.Exists(inputPath)) throw new FileNotFoundException("Downloaded file was not found.", inputPath);

        if (formattingMethod == "recode")
        {
            var result = RunFfmpeg(ffmpegPath, GetReencodeArguments(inputPath, outputPath, targetContainer, onlyAudio,
                    videoEncoding, audioEncoding),
                "Re-Encoding... This may take a while! Please be patient!", false);
            if (!result.Success)
                throw CreateReencodeException(targetContainer, videoEncoding, audioEncoding, result.ErrorOutput);

            SetProcessingInfo($"Re-encoded to .{targetContainer} because re-encoding was selected.");
            return;
        }

        var remuxSucceeded = RunFfmpeg(ffmpegPath, GetRemuxArguments(inputPath, outputPath, onlyAudio),
            "Remuxing... (This may take a while)", true);
        if (remuxSucceeded.Success)
        {
            SetProcessingInfo($"Remuxed to .{targetContainer}; the downloaded codecs were accepted by the target container.");
            return;
        }

        if (formattingMethod == "remux")
            throw CreateRemuxException(targetContainer, remuxSucceeded.ErrorOutput);

        if (File.Exists(outputPath)) File.Delete(outputPath);
        var fallbackResult = RunFfmpeg(ffmpegPath, GetReencodeArguments(inputPath, outputPath, targetContainer, onlyAudio,
                videoEncoding, audioEncoding),
            "Re-Encoding... The target container needs compatible codecs.", false);
        if (!fallbackResult.Success)
            throw CreateReencodeException(targetContainer, videoEncoding, audioEncoding, fallbackResult.ErrorOutput);

        SetProcessingInfo($"Re-encoded to .{targetContainer}; direct remuxing was not compatible with the target container.");
    }

    private FfmpegResult RunFfmpeg(string ffmpegPath, string arguments, string statusText, bool allowFailure)
    {
        var instance = MainWindow.GetInstance();
        TimeSpan? duration = null;
        var errorOutput = new StringBuilder();
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            instance.StatusLabel.Text = statusText;
            instance.CurrentDownloadProgressBar.IsIndeterminate = false;
            instance.CurrentDownloadProgressBar.Value = 0;
            SetFfmpegProgressLabels(instance);
            instance.CurrentDownloadPercentageTextBlock.Text = "0%";
            instance.DownloadSizeTextBlock.Text = "00:00:00";
            instance.DownloadSpeedTextBlock.Text = "0x";
            instance.RemainingTimeTextBlock.Text = "--:--";
        }).Wait();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        while (!process.StandardError.EndOfStream)
        {
            var line = process.StandardError.ReadLine();
            if (line == null) continue;
            errorOutput.AppendLine(line);

            duration ??= TryReadDuration(line);
            var current = TryReadProgressTime(line);
            var speed = TryReadSpeed(line);
            if (duration == null || current == null || duration.Value.TotalSeconds <= 0) continue;

            var percent = Math.Min(100, current.Value.TotalSeconds / duration.Value.TotalSeconds * 100);
            var eta = CalculateEta(current.Value, duration.Value, speed);
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                instance.CurrentDownloadProgressBar.Value = percent;
                instance.CurrentDownloadPercentageTextBlock.Text = Math.Round(percent, 1) + "%";
                instance.DownloadSizeTextBlock.Text = FormatTime(current.Value) + " / " + FormatTime(duration.Value);
                instance.DownloadSpeedTextBlock.Text = speed != null ? Math.Round(speed.Value, 2) + "x" : "--";
                instance.RemainingTimeTextBlock.Text = eta != null ? FormatTime(eta.Value) : "--:--";
            });
        }

        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                instance.CurrentDownloadProgressBar.Value = 100;
                instance.CurrentDownloadPercentageTextBlock.Text = "100%";
                instance.RemainingTimeTextBlock.Text = "00:00";
            }).Wait();
            return new FfmpegResult(true, errorOutput.ToString());
        }

        var error = errorOutput.ToString();
        if (allowFailure) return new FfmpegResult(false, error);
        return new FfmpegResult(false, error);
    }

    private static InvalidOperationException CreateRemuxException(string targetContainer, string ffmpegError)
    {
        return new InvalidOperationException(
            $"Cannot remux into .{targetContainer}. The selected target container does not support one or more codecs from the downloaded YouTube format. Select Auto or Re-Encode, or choose different detailed formats/codecs.\n\nffmpeg output:\n{GetRelevantFfmpegError(ffmpegError)}");
    }

    private static InvalidOperationException CreateReencodeException(string targetContainer, string videoEncoding,
        string audioEncoding, string ffmpegError)
    {
        return new InvalidOperationException(
            $"Cannot re-encode into .{targetContainer} with video encoder '{videoEncoding}' and audio encoder '{audioEncoding}'. The selected codec/container combination may be incompatible, or your ffmpeg build may not include that encoder. Choose Auto or a codec supported by the target container.\n\nffmpeg output:\n{GetRelevantFfmpegError(ffmpegError)}");
    }

    private static string GetRelevantFfmpegError(string ffmpegError)
    {
        var lines = ffmpegError.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.TakeLast(Math.Min(lines.Length, 12)));
    }

    private readonly record struct FfmpegResult(bool Success, string ErrorOutput);

    private static string GetRemuxArguments(string inputPath, string outputPath, bool onlyAudio)
    {
        return $"-y -i {Quote(inputPath)} {(onlyAudio ? "-vn " : "")}-c copy {Quote(outputPath)}";
    }

    private static string GetReencodeArguments(string inputPath, string outputPath, string targetContainer, bool onlyAudio,
        string videoEncoding, string audioEncoding)
    {
        var audioCodec = audioEncoding == "Auto" ? targetContainer switch
        {
            "mp3" => "libmp3lame",
            "flac" => "flac",
            "wav" => "pcm_s16le",
            "ogg" => "libvorbis",
            "webm" => "libopus",
            _ => "aac"
        } : audioEncoding;

        if (onlyAudio) return $"-y -i {Quote(inputPath)} -vn -c:a {audioCodec} {Quote(outputPath)}";

        var videoCodec = videoEncoding == "Auto" ? targetContainer switch
        {
            "webm" => "libvpx-vp9",
            "flv" => "flv",
            "avi" => "mpeg4",
            _ => "libx264"
        } : videoEncoding;

        return $"-y -i {Quote(inputPath)} -c:v {videoCodec} -c:a {audioCodec} {Quote(outputPath)}";
    }

    private void SetProcessingInfo(string text)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var instance = MainWindow.GetInstance();
            instance.ProcessingInfoTextBlock.Text = text;
        });
    }

    private static TimeSpan? TryReadDuration(string line)
    {
        var durationIndex = line.IndexOf("Duration: ", StringComparison.Ordinal);
        if (durationIndex < 0) return null;

        var value = line.Substring(durationIndex + 10, 11);
        return TimeSpan.TryParse(value, out var duration) ? duration : null;
    }

    private static TimeSpan? TryReadProgressTime(string line)
    {
        var timeIndex = line.IndexOf("time=", StringComparison.Ordinal);
        if (timeIndex < 0) return null;

        var value = line[(timeIndex + 5)..].TrimStart();
        var endIndex = value.IndexOf(' ');
        if (endIndex >= 0) value = value[..endIndex];

        return TimeSpan.TryParse(value, out var time) ? time : null;
    }

    private static double? TryReadSpeed(string line)
    {
        var speedIndex = line.IndexOf("speed=", StringComparison.Ordinal);
        if (speedIndex < 0) return null;

        var value = line[(speedIndex + 6)..].TrimStart();
        var endIndex = value.IndexOf(' ');
        if (endIndex >= 0) value = value[..endIndex];
        value = value.TrimEnd('x');

        return double.TryParse(value, out var speed) ? speed : null;
    }

    private static TimeSpan? CalculateEta(TimeSpan current, TimeSpan duration, double? speed)
    {
        if (speed == null || speed.Value <= 0) return null;

        var remainingSeconds = Math.Max(0, duration.TotalSeconds - current.TotalSeconds) / speed.Value;
        return TimeSpan.FromSeconds(remainingSeconds);
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"hh\:mm\:ss")
            : time.ToString(@"mm\:ss");
    }

    private static void SetFfmpegProgressLabels(MainWindow instance)
    {
        instance.DownloadSizeTextBlock.Text = "processed";
        instance.DownloadOfTextBlock.Text = "";
        instance.DownloadAtTextBlock.Text = "speed";
        instance.DownloadEtaTextBlock.Text = "ETA";
    }

    private static void ResetDownloadProgressLabels(MainWindow instance)
    {
        instance.CurrentDownloadPercentageTextBlock.Text = "0%";
        instance.DownloadOfTextBlock.Text = "of";
        instance.DownloadSizeTextBlock.Text = "0MiB";
        instance.DownloadAtTextBlock.Text = "at";
        instance.DownloadSpeedTextBlock.Text = "0 KiB/s";
        instance.DownloadEtaTextBlock.Text = "ETA";
        instance.RemainingTimeTextBlock.Text = "00:00";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_linux";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_win.zip";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp_macos";
        throw new PlatformNotSupportedException("This platform is not supported!");
    }

    private string GetYtDlpPath()
    {
        string fileName;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            fileName = "yt-dlp_linux";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            fileName = "yt-dlp_win.exe";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            fileName = "yt-dlp_macos";
        else
            throw new PlatformNotSupportedException("This platform is not supported!");
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    private void DownloadFile(string url, string path, bool forceDownload = false)
    {
        if (File.Exists(path) && !forceDownload) return;
        if (forceDownload) DeleteFileIfExists(path);
        var client = new HttpClientDownloadWithProgress(url, path);
        client.ProgressChanged += DownloadFileClient_ProgressChanged;
        client.StartDownload().Wait();
    }

    private static string? FindFile(string directory, string fileName)
    {
        return Directory.Exists(directory)
            ? Directory.GetFiles(directory, fileName, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
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

        if (text.Contains("[VideoConvertor] ") || text.Contains("[VideoRemuxer] "))
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
        if (!TryReadYtDlpDownloadProgress(text, out var downloadProgress)) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            instance.CurrentDownloadProgressBar.Value = downloadProgress.Percent;
            instance.CurrentDownloadPercentageTextBlock.Text = downloadProgress.PercentText + "%";
            instance.DownloadSpeedTextBlock.Text = downloadProgress.Speed;
            instance.RemainingTimeTextBlock.Text = downloadProgress.Eta;
            instance.DownloadSizeTextBlock.Text = downloadProgress.TotalSize;
            instance.DownloadOfTextBlock.Text = "of";
            instance.DownloadAtTextBlock.Text = "at";
            instance.DownloadEtaTextBlock.Text = "ETA";
        });
    }

    private static bool TryReadYtDlpDownloadProgress(string text, out YtDlpDownloadProgress progress)
    {
        progress = default;
        var match = Regex.Match(text,
            @"\[download\]\s+(?<percent>\d+(?:\.\d+)?)%\s+of\s+~?\s*(?<size>\S+)\s+at\s+(?<speed>\S+)\s+ETA\s+(?<eta>\S+)");
        if (!match.Success) return false;
        if (!double.TryParse(match.Groups["percent"].Value, CultureInfo.InvariantCulture, out var percent)) return false;

        progress = new YtDlpDownloadProgress(
            percent,
            match.Groups["percent"].Value,
            match.Groups["size"].Value,
            match.Groups["speed"].Value,
            match.Groups["eta"].Value);
        return true;
    }

    private readonly record struct YtDlpDownloadProgress(double Percent, string PercentText, string TotalSize,
        string Speed, string Eta);

    public string FormattingMethod { get; set; } = "remux";

    private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        var instance = MainWindow.GetInstance();
        var text = e.Data;
        if (text == null) return;
        if (text.Contains("WARNING: Requested formats are incompatible for merge and will be merged into mkv")) return;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            instance.ErrorTextBlock.Text = text;
            instance.ErrorTextBlock.IsVisible = true;
        });
    }

    public async void OnVideoDownloadPathButtonClicked()
    {
        var instance = MainWindow.GetInstance();
        var folderPicker = await instance.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select download folder"
        });

        if (folderPicker.Count >= 1)
            instance.VideoDownloadPathTextBox.Text = folderPicker[0].Path.LocalPath;
    }

    public string GetFfmpegDownloadUrl()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return "https://johnvansickle.com/ffmpeg/builds/ffmpeg-git-amd64-static.tar.xz";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return
                "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "https://evermeet.cx/ffmpeg/getrelease/zip";
        throw new PlatformNotSupportedException("This platform is not supported!");
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
