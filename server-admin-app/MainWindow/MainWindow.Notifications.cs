using System.Windows;

namespace Server.Admin.App;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, GuestSessionSnapshot> _guestSessionSnapshotByPcId = new(StringComparer.OrdinalIgnoreCase);
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
            ? "khach vang lai"
            : row.ActiveGuestDisplayName.Trim();
        var message = $"May tram {machineLabel} co {guestLabel} dang su dung.";

        ShowGuestLoginToast(message);
        AppendServiceLog($"[{DateTime.Now:HH:mm:ss}] Alert guest login: {machineLabel} dang duoc su dung");
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
