using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Media;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, GuestSessionSnapshot> _guestSessionSnapshotByPcId = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _guestLoginSpeechLock = new(1, 1);
    private readonly object _guestLoginAudioPlaybackSync = new();
    private MediaPlayer? _guestLoginAudioPlayer;
    private static readonly string[] PreferredVietnameseVoiceNameHints = ["hoaimy", "hoai my", "an", "mai"];
    private bool _guestLoginNotificationsInitialized;
    private bool _guestSessionSnapshotInitialized;

    private sealed class GuestSessionSnapshot
    {
        public string StatusCode { get; init; } = string.Empty;
        public bool IsGuestSession { get; init; }
        public string? ActiveSessionId { get; init; }
    }

    private void InitializeGuestLoginNotifications()
    {
        if (_guestLoginNotificationsInitialized)
        {
            return;
        }

        _guestLoginNotificationsInitialized = true;
        HideGuestLoginToast();
    }

    private void ShutdownGuestLoginNotifications()
    {
        if (!_guestLoginNotificationsInitialized)
        {
            return;
        }

        _guestLoginNotificationsInitialized = false;
        HideGuestLoginToast();

        _guestSessionSnapshotByPcId.Clear();
        _guestSessionSnapshotInitialized = false;
    }

    private void TrackGuestSessionNotifications(IReadOnlyList<MachineRow> currentRows)
    {
        if (_guestSessionSnapshotInitialized)
        {
            foreach (var row in currentRows)
            {
                if (!IsGuestSessionActive(row))
                {
                    continue;
                }

                if (_guestSessionSnapshotByPcId.TryGetValue(row.Id, out var previousSnapshot) &&
                    previousSnapshot.IsGuestSession &&
                    previousSnapshot.StatusCode.Equals("IN_USE", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(previousSnapshot.ActiveSessionId, row.ActiveSessionId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                NotifyGuestSessionStarted(row);
            }
        }

        _guestSessionSnapshotByPcId.Clear();
        foreach (var row in currentRows)
        {
            _guestSessionSnapshotByPcId[row.Id] = new GuestSessionSnapshot
            {
                StatusCode = row.StatusCode,
                IsGuestSession = row.IsGuestSession,
                ActiveSessionId = row.ActiveSessionId,
            };
        }

        _guestSessionSnapshotInitialized = true;
    }

    private void NotifyGuestSessionStarted(MachineRow row)
    {
        var machineLabel = string.IsNullOrWhiteSpace(row.Name) ? row.AgentId : row.Name;
        var guestLabel = string.IsNullOrWhiteSpace(row.ActiveGuestDisplayName)
            ? "khách vãng lai"
            : row.ActiveGuestDisplayName.Trim();
        var message = $"Máy trạm {machineLabel} có {guestLabel} đang sử dụng.";

        ShowGuestLoginToast(message);
        _ = SpeakGuestLoginNotificationAsync(machineLabel);
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Alert guest login: {machineLabel} dang duoc su dung");
    }

    private async Task SpeakGuestLoginNotificationAsync(string machineLabel)
    {
        await _guestLoginSpeechLock.WaitAsync();
        try
        {
            if (TryPlayCustomGuestLoginAudio(out var customAudioPath))
            {
                AppendServiceLog(
                    $"[{DateTime.Now:HH:mm:ss}] Played custom guest-login audio: {customAudioPath}");
                return;
            }

            await Task.Run(() =>
            {
                using var synthesizer = new SpeechSynthesizer();
                synthesizer.SetOutputToDefaultAudioDevice();
                synthesizer.Volume = 100;
                TrySelectPreferredVietnameseVoice(synthesizer);

                var normalizedMachineLabel = string.IsNullOrWhiteSpace(machineLabel)
                    ? "không rõ"
                    : machineLabel.Trim();

                try
                {
                    var introStyle = new PromptStyle
                    {
                        Emphasis = PromptEmphasis.Strong,
                        Rate = PromptRate.Slow,
                        Volume = PromptVolume.ExtraLoud,
                    };
                    var detailStyle = new PromptStyle
                    {
                        Emphasis = PromptEmphasis.Moderate,
                        Rate = PromptRate.Medium,
                        Volume = PromptVolume.Loud,
                    };

                    var prompt = new PromptBuilder(new CultureInfo("vi-VN"));
                    prompt.StartStyle(introStyle);
                    prompt.AppendText("Luu y");
                    prompt.EndStyle();
                    prompt.AppendBreak(TimeSpan.FromMilliseconds(260));
                    prompt.StartStyle(detailStyle);
                    prompt.AppendText($"Máy trạm {normalizedMachineLabel} đang có khách sử dụng");
                    prompt.EndStyle();

                    synthesizer.Speak(prompt);
                }
                catch
                {
                    // Fall back to plain speech when style prompt is unsupported.
                    synthesizer.Speak($"Máy trạm {normalizedMachineLabel} đang có khách sử dụng.");
                }
            });
        }
        catch
        {
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch
            {
                // Keep notification flow resilient when audio output is unavailable.
            }
        }
        finally
        {
            _guestLoginSpeechLock.Release();
        }
    }

    private bool TryPlayCustomGuestLoginAudio(out string selectedPath)
    {
        selectedPath = string.Empty;
        foreach (var candidate in ResolveGuestLoginAudioCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            {
                continue;
            }

            if (!TryPlayAudioFile(candidate))
            {
                continue;
            }

            selectedPath = candidate;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ResolveGuestLoginAudioCandidates()
    {
        var fileNames = new[]
        {
            "guest-login.mp3",
            "guest-login.wav",
            "guest-login-alert.mp3",
            "guest-login-alert.wav",
        };

        var roots = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "audio"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ServerManagerBilling",
                "audio"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "Music"),
        };

        var result = new List<string>();
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var fileName in fileNames)
            {
                var path = Path.Combine(root, fileName);
                if (!result.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(path);
                }
            }
        }

        return result;
    }

    private bool TryPlayAudioFile(string filePath)
    {
        try
        {
            var absolutePath = Path.GetFullPath(filePath);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            Dispatcher.Invoke(() =>
            {
                lock (_guestLoginAudioPlaybackSync)
                {
                    _guestLoginAudioPlayer?.Stop();
                    _guestLoginAudioPlayer?.Close();

                    var player = new MediaPlayer
                    {
                        Volume = 1.0,
                    };
                    player.MediaFailed += (_, _) =>
                    {
                        try
                        {
                            player.Stop();
                            player.Close();
                        }
                        catch
                        {
                            // Keep notification flow resilient when media playback fails.
                        }
                    };

                    player.Open(new Uri(absolutePath, UriKind.Absolute));
                    player.Play();
                    _guestLoginAudioPlayer = player;
                }
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TrySelectPreferredVietnameseVoice(SpeechSynthesizer synthesizer)
    {
        try
        {
            var installedVoices = synthesizer
                .GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .ToList();

            if (installedVoices.Count == 0)
            {
                return;
            }

            var preferredVoice = installedVoices.FirstOrDefault(v =>
                    v.Culture.Name.Equals("vi-VN", StringComparison.OrdinalIgnoreCase) &&
                    PreferredVietnameseVoiceNameHints.Any(h => v.Name.Contains(h, StringComparison.OrdinalIgnoreCase)))
                ?? installedVoices.FirstOrDefault(v =>
                    v.Culture.Name.Equals("vi-VN", StringComparison.OrdinalIgnoreCase));

            if (preferredVoice is not null)
            {
                synthesizer.SelectVoice(preferredVoice.Name);
            }
        }
        catch
        {
            // Keep default system voice when selection fails.
        }
    }

    private void ShowGuestLoginToast(string message)
    {
        if (GuestLoginToastBorder is null || GuestLoginToastMessageTextBlock is null)
        {
            return;
        }

        GuestLoginToastMessageTextBlock.Text = message;
        GuestLoginToastBorder.Visibility = Visibility.Visible;
    }

    private void GuestLoginToastCloseButton_Click(object sender, RoutedEventArgs e)
    {
        HideGuestLoginToast();
    }

    private void HideGuestLoginToast()
    {
        if (GuestLoginToastBorder is not null)
        {
            GuestLoginToastBorder.Visibility = Visibility.Collapsed;
        }

        if (GuestLoginToastMessageTextBlock is not null)
        {
            GuestLoginToastMessageTextBlock.Text = string.Empty;
        }
    }

    private static bool IsGuestSessionActive(MachineRow row)
    {
        return row.IsGuestSession &&
               row.StatusCode.Equals("IN_USE", StringComparison.OrdinalIgnoreCase);
    }
}

