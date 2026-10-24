using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Lame;
using NAudio.MediaFoundation;
using NAudio.Wave;
using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YouTubeAudioDownloader;

public partial class Form1 : Form
{
    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YouTubeAudioDownloader",
        "settings.json");

    // Znaki dostępne na standardowej klawiaturze (PL/US QWERTY), które są jednocześnie dozwolone w nazwach plików Windows
    private static readonly HashSet<char> AllowedKeyboardSymbols =
    [
        ' ', '!', '@', '#', '$', '%', '^', '&', '(', ')',
        '-', '_', '=', '+', '[', ']', '{', '}', ';', '\'',
        ',', '.', '~', '`'
    ];

    private static readonly HashSet<char> AllowedPolishLetters =
    [
        'ą', 'ć', 'ę', 'ł', 'ń', 'ó', 'ś', 'ź', 'ż',
        'Ą', 'Ć', 'Ę', 'Ł', 'Ń', 'Ó', 'Ś', 'Ź', 'Ż'
    ];

    private static readonly HttpClient SharedHttpClient = new();
    private readonly YoutubeClient _youtube = new();
    private CancellationTokenSource? _cts;

    public Form1()
    {
        InitializeComponent();
        MediaFoundationApi.Startup();
    }

    private void btnPaste_Click(object? sender, EventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            txtUrl.Text = Clipboard.GetText().Trim();
        }
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        _cts?.Cancel();
    }

    private async void btnDownload_Click(object? sender, EventArgs e)
    {
        string url = txtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(
                "Wklej najpierw adres filmu z YouTube.",
                "Brak adresu URL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            txtUrl.Focus();
            return;
        }

        VideoId? videoId = VideoId.TryParse(url);
        if (videoId is null)
        {
            MessageBox.Show(
                "Podany adres nie jest prawidłowym linkiem do filmu na YouTube.",
                "Nieprawidłowy adres URL",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            txtUrl.Focus();
            return;
        }

        SetBusyState(true);
        _cts = new CancellationTokenSource();

        try
        {
            lblStatus.Text = "Pobieranie informacji o filmie...";
            progressBar.Style = ProgressBarStyle.Marquee;

            string rawTitle = await FetchVideoTitleAsync(videoId.Value, _cts.Token);
            string sanitizedTitle = SanitizeFileName(rawTitle);

            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;

            string? outputMp3Path = PromptForSavePath(sanitizedTitle);
            if (string.IsNullOrWhiteSpace(outputMp3Path))
            {
                lblStatus.Text = "Anulowano wybór pliku.";
                return;
            }

            var progress = new Progress<(string StatusText, int? Percent)>(update =>
            {
                lblStatus.Text = update.StatusText;
                if (update.Percent.HasValue)
                {
                    progressBar.Style = ProgressBarStyle.Blocks;
                    progressBar.Value = Math.Clamp(update.Percent.Value, 0, 100);
                }
                else
                {
                    progressBar.Style = ProgressBarStyle.Marquee;
                }
            });

            await DownloadAndConvertToMp3Async(videoId.Value, outputMp3Path, progress, _cts.Token);

            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 100;
            lblStatus.Text = $"Gotowe! Zapisano: {Path.GetFileName(outputMp3Path)}";

            MessageBox.Show(
                $"Plik MP3 został pomyślnie zapisany:\n{outputMp3Path}",
                "Pobieranie zakończone",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
            lblStatus.Text = "Pobieranie zostało anulowane.";
        }
        catch (Exception ex)
        {
            progressBar.Style = ProgressBarStyle.Blocks;
            progressBar.Value = 0;
            lblStatus.Text = "Wystąpił błąd podczas pobierania.";
            MessageBox.Show(
                $"Nie udało się pobrać lub zapisać pliku MP3:\n\n{ex.Message}",
                "Błąd",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            SetBusyState(false);
        }
    }

    /// <summary>
    /// Pobiera tytuł filmu z oficjalnego punktu końcowego YouTube oEmbed (niezawodnego i odpornego na zmiany HTML),
    /// a w razie niepowodzenia próbuje przez YoutubeExplode.
    /// </summary>
    public async Task<string> FetchVideoTitleAsync(VideoId videoId, CancellationToken cancellationToken = default)
    {
        try
        {
            string oembedUrl = $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId.Value}&format=json";
            var response = await SharedHttpClient.GetFromJsonAsync<OEmbedResponse>(oembedUrl, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(response?.Title))
            {
                return response.Title;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Przejdź do metody zapasowej
        }

        try
        {
            var video = await _youtube.Videos.GetAsync(videoId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(video.Title))
            {
                return video.Title;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Zwróć domyślną nazwę jeśli nie udało się pobrać metadanych strony
        }

        return $"youtube_{videoId.Value}";
    }

    public Task DownloadAndConvertToMp3Async(
        VideoId videoId,
        string outputMp3Path,
        IProgress<(string StatusText, int? Percent)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            string? tempAudioPath = null;
            try
            {
                progress?.Report(("Szukanie najlepszego strumienia audio...", null));

                var manifest = await _youtube.Videos.Streams.GetManifestAsync(videoId, cancellationToken).ConfigureAwait(false);

                // Preferujemy strumień MP4/AAC (natywnie wspierany przez Windows Media Foundation)
                IStreamInfo? streamInfo = manifest
                    .GetAudioOnlyStreams()
                    .Where(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                    .TryGetWithHighestBitrate();

                streamInfo ??= manifest
                    .GetAudioOnlyStreams()
                    .TryGetWithHighestBitrate();

                streamInfo ??= manifest
                    .GetMuxedStreams()
                    .Where(s => s.Container == YoutubeExplode.Videos.Streams.Container.Mp4)
                    .TryGetWithHighestBitrate();

                streamInfo ??= manifest
                    .Streams
                    .TryGetWithHighestBitrate();

                if (streamInfo is null)
                {
                    throw new InvalidOperationException("Nie znaleziono żadnego obsługiwanego strumienia audio dla tego filmu.");
                }

                string ext = streamInfo.Container == YoutubeExplode.Videos.Streams.Container.Mp4
                    ? ".mp4"
                    : $".{streamInfo.Container.Name}";
                tempAudioPath = Path.Combine(Path.GetTempPath(), $"yt_audio_{Guid.NewGuid():N}{ext}");

                progress?.Report(("Pobieranie audio (0%)...", 0));

                var downloadProgress = new Progress<double>(p =>
                {
                    int percent = (int)Math.Round(p * 100);
                    progress?.Report(($"Pobieranie audio ({percent}%)...", percent));
                });

                await _youtube.Videos.Streams.DownloadAsync(streamInfo, tempAudioPath, downloadProgress, cancellationToken).ConfigureAwait(false);

                progress?.Report(("Konwersja do formatu MP3...", null));

                ConvertToMp3(tempAudioPath, outputMp3Path, cancellationToken);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempAudioPath) && File.Exists(tempAudioPath))
                {
                    try { File.Delete(tempAudioPath); } catch { /* Ignoruj błędy usuwania pliku tymczasowego */ }
                }
            }
        }, cancellationToken);
    }

    private string? PromptForSavePath(string sanitizedTitle)
    {
        string initialDir = LoadLastDirectory();

        using var saveDialog = new SaveFileDialog
        {
            Title = "Zapisz plik MP3 jako...",
            Filter = "Pliki MP3 (*.mp3)|*.mp3",
            DefaultExt = "mp3",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = initialDir,
            FileName = $"{sanitizedTitle}.mp3"
        };

        if (saveDialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        string? selectedDir = Path.GetDirectoryName(saveDialog.FileName);
        if (!string.IsNullOrWhiteSpace(selectedDir))
        {
            SaveLastDirectory(selectedDir);
        }

        return saveDialog.FileName;
    }

    public static void ConvertToMp3(string inputAudioPath, string outputMp3Path, CancellationToken cancellationToken = default)
    {
        MediaFoundationApi.Startup();
        using var reader = new MediaFoundationReader(inputAudioPath);

        try
        {
            using var writer = new LameMP3FileWriter(outputMp3Path, reader.WaveFormat, LAMEPreset.STANDARD);
            byte[] buffer = new byte[reader.WaveFormat.AverageBytesPerSecond];
            int bytesRead;
            while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.Write(buffer, 0, bytesRead);
            }
            writer.Flush();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fallback do natywnego kodera MP3 wbudowanego w system Windows Media Foundation
            if (File.Exists(outputMp3Path))
            {
                try { File.Delete(outputMp3Path); } catch { }
            }

            reader.Position = 0;
            MediaFoundationEncoder.EncodeToMp3(reader, outputMp3Path, 192000);
        }
    }

    /// <summary>
    /// Oczyszcza tytuł filmu z niedozwolonych znaków, zostawiając wyłącznie litery (ASCII + polskie znaki),
    /// cyfry oraz bezpieczne znaki dostępne na klawiaturze.
    /// </summary>
    public static string SanitizeFileName(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return "audio";
        }

        var sb = new StringBuilder(rawTitle.Length);
        bool lastWasSpace = false;

        foreach (char c in rawTitle)
        {
            // Zamień separatory spotykane w tytułach YouTube (np. |, /, \, :) na myślnik lub spację
            char normalized = c switch
            {
                '|' or '/' or '\\' => '-',
                ':' => ' ',
                _ => c
            };

            bool isAsciiLetterOrDigit = char.IsAsciiLetterOrDigit(normalized);
            bool isPolishLetter = AllowedPolishLetters.Contains(normalized);
            bool isAllowedSymbol = AllowedKeyboardSymbols.Contains(normalized);

            if (isAsciiLetterOrDigit || isPolishLetter || isAllowedSymbol)
            {
                if (normalized == ' ')
                {
                    if (!lastWasSpace)
                    {
                        sb.Append(' ');
                        lastWasSpace = true;
                    }
                }
                else
                {
                    sb.Append(normalized);
                    lastWasSpace = false;
                }
            }
        }

        // Windows nie pozwala na spacje ani kropki na końcu lub początku nazwy pliku
        string cleaned = sb.ToString().Trim(' ', '.');

        return string.IsNullOrWhiteSpace(cleaned) ? "audio" : cleaned;
    }

    private static string LoadLastDirectory()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (!string.IsNullOrWhiteSpace(settings?.LastDirectory) && Directory.Exists(settings.LastDirectory))
                {
                    return settings.LastDirectory;
                }
            }
        }
        catch
        {
            // W razie problemu z odczytem ustawień użyj domyślnego katalogu
        }

        string musicFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        return Directory.Exists(musicFolder)
            ? musicFolder
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    private static void SaveLastDirectory(string directoryPath)
    {
        try
        {
            string? configDir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var settings = new AppSettings { LastDirectory = directoryPath };
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Ignoruj błędy zapisu ustawień, aby nie przerywać działania aplikacji
        }
    }

    private void SetBusyState(bool isBusy)
    {
        txtUrl.Enabled = !isBusy;
        btnPaste.Enabled = !isBusy;
        btnDownload.Enabled = !isBusy;
        btnCancel.Enabled = isBusy;
    }

    private sealed class AppSettings
    {
        public string? LastDirectory { get; set; }
    }

    private sealed class OEmbedResponse
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }
}
