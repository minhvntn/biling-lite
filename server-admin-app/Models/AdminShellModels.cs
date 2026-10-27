using System.Windows.Media;

namespace Server.Admin.App;

public sealed class AdminShellSettings
{
    public string AdminBaseUrl { get; set; } = "http://127.0.0.1:5400";

    public string BackendApiBaseUrl { get; set; } = "http://localhost:39090/api/v1";

    public string StartPath { get; set; } = "/pcs";

    public int MachineRefreshSeconds { get; set; } = 3;

    public double UiFontSize { get; set; } = 13;

    public double MachineTableFontSize { get; set; } = 14;

    public double MachineContextMenuItemPadding { get; set; } = 12;

    public double MachineContextMenuFontSize { get; set; } = 14;

    public Dictionary<string, WakeLanProfile> WakeLanProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public string ColorInUseBg { get; set; } = "#EFF6FF";
    public string ColorInUseFg { get; set; } = "#2563EB";
    public string ColorAdminBg { get; set; } = "#FEF9C3";
    public string ColorAdminFg { get; set; } = "#B45309";
    public string ColorOfflineBg { get; set; } = "#F1F5F9";
    public string ColorOfflineFg { get; set; } = "#64748B";
    public string ColorOnlineBg { get; set; } = "#ECFDF5";
    public string ColorOnlineFg { get; set; } = "#16A34A";
    public string ColorUsedTimeBg { get; set; } = "#FEF2F2";
    public string ColorUsedTimeFg { get; set; } = "#DC2626";
    public string ColorRemainingTimeBg { get; set; } = "#EFF6FF";
    public string ColorRemainingTimeFg { get; set; } = "#2563EB";
    public string ColorMinorBg { get; set; } = "#FFFFFF";
    public string ColorMinorFg { get; set; } = "#F97316";
    public string ColorServiceCallBg { get; set; } = "#8B5CF6";
    public string ColorServiceDebtBg { get; set; } = "#0D9488";
    public string ColorTransferPayBg { get; set; } = "#F59E0B";
    public string ColorVipBg { get; set; } = "#FEF3C7";
    public string ColorVipFg { get; set; } = "#D97706";
}

public sealed class WakeLanProfile
{
    public string MacAddress { get; set; } = string.Empty;
    public string? BroadcastAddress { get; set; }
}

public sealed class WakeLanProfileRow
{
    public string Key { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public string BroadcastAddress { get; set; } = string.Empty;
}

public sealed class PcListResponse
{
    public List<PcListItem> Items { get; set; } = new();
}

public sealed class PcListItem
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public decimal HourlyRate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? MacAddress { get; set; }
    public int ConnectedSockets { get; set; }
    public bool IsConnected { get; set; }
    public string? LastSeenAt { get; set; }
    public ActiveSessionInfo? ActiveSession { get; set; }
    public ActiveMemberInfo? ActiveMember { get; set; }
    public ActiveGuestInfo? ActiveGuest { get; set; }
    public ActiveAdminInfo? ActiveAdmin { get; set; }
}

public sealed class ActiveMemberInfo
{
    public string MemberId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int PlaySeconds { get; set; }
    public bool UsePromoPlayTime { get; set; }
    public string MemberType { get; set; } = "REGULAR";
}

public sealed class ActiveGuestInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public decimal PrepaidAmount { get; set; }
}

public sealed class ActiveAdminInfo
{
    public string Username { get; set; } = "Admin";
    public string FullName { get; set; } = "Admin";
}

public sealed class ActiveSessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public int ElapsedSeconds { get; set; }
    public decimal PricePerMinute { get; set; }
    public decimal EstimatedAmount { get; set; }
}

public sealed class CommandDispatchResponse
{
    public string Id { get; set; } = string.Empty;
    public string PcId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class MachineRow : ObservableObject
{
    private string _id = string.Empty;
    public string Id { get => _id; set => SetProperty(ref _id, value); }

    private string _agentId = string.Empty;
    public string AgentId { get => _agentId; set => SetProperty(ref _agentId, value); }

    private string _name = string.Empty;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string? _groupId;
    public string? GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }

    private decimal _hourlyRate;
    public decimal HourlyRate { get => _hourlyRate; set => SetProperty(ref _hourlyRate, value); }

    private string _ipAddress = "-";
    public string IpAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }

    private string _macAddress = string.Empty;
    public string MacAddress { get => _macAddress; set => SetProperty(ref _macAddress, value); }

    private string _lastSeenAtText = "-";
    public string LastSeenAtText { get => _lastSeenAtText; set => SetProperty(ref _lastSeenAtText, value); }

    private string _statusCode = string.Empty;
    public string StatusCode { get => _statusCode; set => SetProperty(ref _statusCode, value); }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private string _statusSubText = string.Empty;
    public string StatusSubText { get => _statusSubText; set => SetProperty(ref _statusSubText, value); }

    private bool _isOfflineUnpaidGuest;
    public bool IsOfflineUnpaidGuest { get => _isOfflineUnpaidGuest; set => SetProperty(ref _isOfflineUnpaidGuest, value); }

    private Brush _statusBrush = Brushes.Gray;
    public Brush StatusBrush { get => _statusBrush; set => SetProperty(ref _statusBrush, value); }

    private Brush _statusIconBrush = Brushes.Gray;
    public Brush StatusIconBrush { get => _statusIconBrush; set => SetProperty(ref _statusIconBrush, value); }

    private string _statusIconPath = "/Assets/pc-default.svg";
    public string StatusIconPath { get => _statusIconPath; set => SetProperty(ref _statusIconPath, value); }

    private string _statusIconToolTip = string.Empty;
    public string StatusIconToolTip { get => _statusIconToolTip; set => SetProperty(ref _statusIconToolTip, value); }

    private string _userName = "-";
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

    private string _startedAtText = "-";
    public string StartedAtText { get => _startedAtText; set => SetProperty(ref _startedAtText, value); }

    private string _usedText = "-";
    public string UsedText { get => _usedText; set => SetProperty(ref _usedText, value); }

    private string _remainingText = "-";
    public string RemainingText { get => _remainingText; set => SetProperty(ref _remainingText, value); }

    private string _moneyText = "-";
    public string MoneyText { get => _moneyText; set => SetProperty(ref _moneyText, value); }

    private decimal _serviceAmountRaw;
    public decimal ServiceAmountRaw { get => _serviceAmountRaw; set => SetProperty(ref _serviceAmountRaw, value); }

    private string _serviceAmountText = "-";
    public string ServiceAmountText { get => _serviceAmountText; set => SetProperty(ref _serviceAmountText, value); }

    private bool _hasPendingClientServiceOrderHighlight;
    public bool HasPendingClientServiceOrderHighlight { get => _hasPendingClientServiceOrderHighlight; set => SetProperty(ref _hasPendingClientServiceOrderHighlight, value); }

    private bool _hasServiceDebtHighlight;
    public bool HasServiceDebtHighlight { get => _hasServiceDebtHighlight; set => SetProperty(ref _hasServiceDebtHighlight, value); }

    private string _dateText = "-";
    public string DateText { get => _dateText; set => SetProperty(ref _dateText, value); }

    private string _versionText = "0.1.0";
    public string VersionText { get => _versionText; set => SetProperty(ref _versionText, value); }

    private string _groupName = "Mặc định";
    public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }

    private string? _activeSessionId;
    public string? ActiveSessionId { get => _activeSessionId; set => SetProperty(ref _activeSessionId, value); }

    private string? _activeSessionStartedAt;
    public string? ActiveSessionStartedAt { get => _activeSessionStartedAt; set => SetProperty(ref _activeSessionStartedAt, value); }

    private int _activeSessionElapsedSeconds;
    public int ActiveSessionElapsedSeconds { get => _activeSessionElapsedSeconds; set => SetProperty(ref _activeSessionElapsedSeconds, value); }

    private decimal _activeSessionPricePerMinute;
    public decimal ActiveSessionPricePerMinute { get => _activeSessionPricePerMinute; set => SetProperty(ref _activeSessionPricePerMinute, value); }

    private decimal _activeSessionEstimatedAmount;
    public decimal ActiveSessionEstimatedAmount { get => _activeSessionEstimatedAmount; set => SetProperty(ref _activeSessionEstimatedAmount, value); }

    private string? _activeMemberId;
    public string? ActiveMemberId { get => _activeMemberId; set => SetProperty(ref _activeMemberId, value); }

    private string? _activeMemberUsername;
    public string? ActiveMemberUsername { get => _activeMemberUsername; set => SetProperty(ref _activeMemberUsername, value); }

    private string? _activeMemberFullName;
    public string? ActiveMemberFullName { get => _activeMemberFullName; set => SetProperty(ref _activeMemberFullName, value); }

    private decimal _activeMemberBalance;
    public decimal ActiveMemberBalance { get => _activeMemberBalance; set => SetProperty(ref _activeMemberBalance, value); }

    private bool _isAdminSession;
    public bool IsAdminSession { get => _isAdminSession; set => SetProperty(ref _isAdminSession, value); }

    private bool _isGuestSession;
    public bool IsGuestSession { get => _isGuestSession; set => SetProperty(ref _isGuestSession, value); }

    private string? _activeGuestDisplayName;
    public string? ActiveGuestDisplayName { get => _activeGuestDisplayName; set => SetProperty(ref _activeGuestDisplayName, value); }

    private decimal _activeGuestPrepaidAmount;
    public decimal ActiveGuestPrepaidAmount { get => _activeGuestPrepaidAmount; set => SetProperty(ref _activeGuestPrepaidAmount, value); }

    private bool _isVipSession;
    public bool IsVipSession { get => _isVipSession; set => SetProperty(ref _isVipSession, value); }

    private bool _isUsingPromoPlayTime;
    public bool IsUsingPromoPlayTime { get => _isUsingPromoPlayTime; set => SetProperty(ref _isUsingPromoPlayTime, value); }

    private bool _isVirtual;
    public bool IsVirtual { get => _isVirtual; set => SetProperty(ref _isVirtual, value); }
}

public sealed class MemberListResponse
{
    public List<MemberItem> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class MemberItem
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? IdentityNumber { get; set; }
    public bool HasPassword { get; set; }
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public double PlayHours { get; set; }
    public string Rank { get; set; } = string.Empty;
    public decimal TotalTopup { get; set; }
    public int AvailablePoints { get; set; }
    public string MemberType { get; set; } = "REGULAR";
    public string CreatedAt { get; set; } = string.Empty;
    public string? AvatarId { get; set; }
}

public sealed class MemberTransactionsResponse
{
    public MemberItem Member { get; set; } = new();
    public List<MemberTransactionItem> Items { get; set; } = new();
}

public sealed class MemberUsageSummaryResponse
{
    public string MemberId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? LastLoginAt { get; set; }
    public string? LastLoginPcName { get; set; }
    public string? LastLoginAgentId { get; set; }
    public int TotalUsageSeconds { get; set; }
    public double TotalUsageHours { get; set; }
    public int PlaySeconds { get; set; }
    public int SessionUsageCount { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class LoyaltySettingsResponse
{
    public bool Enabled { get; set; }
    public int MinutesPerPoint { get; set; }
    public int LowestRankMinutesPerPoint { get; set; }
    public int PointsToMinutes { get; set; }
    public double WeekdayMultiplier { get; set; }
    public double WeekendMultiplier { get; set; }
    public decimal HorseRaceMinTopup { get; set; }
    public string HorseRaceMinRankName { get; set; } = string.Empty;
    public int ReferralRewardPoints { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class LoyaltySpinSettingsResponse
{
    public List<LoyaltySpinSettingItem> Items { get; set; } = new();
    public decimal TotalChance { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
}

public sealed class LoyaltySpinSettingItem
{
    public int Minutes { get; set; }
    public decimal Chance { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class ClientRuntimeSettingsResponse
{
    public int ReadyAutoShutdownMinutes { get; set; }
    public string LockScreenBackgroundMode { get; set; } = "none";
    public string LockScreenBackgroundUrl { get; set; } = string.Empty;
    public int LockScreenIntervalSeconds { get; set; } = 5;
    public bool AllowMemberWithdraw { get; set; } = true;
    public bool AllowMemberTopupRequest { get; set; } = true;
    public int AutoCollapseIntervalSeconds { get; set; } = 0;
    public string GameLauncherPath { get; set; } = string.Empty;
    public string? VietQrBankId { get; set; }
    public string? VietQrAccountNo { get; set; }
    public string? VietQrAccountName { get; set; }
    public bool VpnEnabled { get; set; } = true;
    public string? VpnLocalInstallerPath { get; set; }
    public string? VpnDownloadUrl { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class MemberWithdrawRequestItem
{
    public string RequestId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string? PcId { get; set; }
    public string? PcName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberWithdrawPendingListResponse
{
    public List<MemberWithdrawRequestItem> Items { get; set; } = new();
    public int Total { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class MemberWithdrawRequestResponse
{
    public MemberWithdrawRequestItem? Request { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class MemberTopupRequestItem
{
    public string RequestId { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? AgentId { get; set; }
    public string? PcId { get; set; }
    public string? PcName { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberTopupPendingListResponse
{
    public List<MemberTopupRequestItem> Items { get; set; } = new();
    public int Total { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class MemberTopupRequestResponse
{
    public MemberTopupRequestItem? Request { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
}

public sealed class BackupSettingsResponse
{
    public bool Enabled { get; set; }
    public string ScheduleType { get; set; } = "daily";
    public string Time { get; set; } = "02:00";
    public int WeeklyDay { get; set; } = 1;
    public string Directory { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 30;
    public string LastRunAt { get; set; } = string.Empty;
    public string LastStatus { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public string LastFileName { get; set; } = string.Empty;
    public string LastFilePath { get; set; } = string.Empty;
}

public sealed class BackupRunResponse
{
    public bool Ok { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
}

public sealed class WebFilterSettingsResponse
{
    public bool Enabled { get; set; }
    public List<string> BlockedDomains { get; set; } = new();
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class MemberTransactionItem
{
    public string Type { get; set; } = string.Empty;
    public decimal AmountDelta { get; set; }
    public int PlaySecondsDelta { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class MemberRow
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = "-";
    public string IdentityNumber { get; set; } = "-";
    public bool HasPassword { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal BalanceRaw { get; set; }
    public double PlayHoursRaw { get; set; }
    public string BalanceText { get; set; } = "0";
    public string PlayHoursText { get; set; } = "0";
    public string Rank { get; set; } = "S\u1eaft";
    public decimal TotalTopupRaw { get; set; }
    public int AvailablePoints { get; set; }
    public string TotalTopupText { get; set; } = "0";
    public string PasswordState { get; set; } = "Ch\u01b0a \u0111\u1eb7t";
    public string ActiveText { get; set; } = "Ho\u1ea1t \u0111\u1ed9ng";
    public string MemberType { get; set; } = "REGULAR";
    public string MemberTypeText { get; set; } = "Thường";
    public string CreatedAtText { get; set; } = "-";

    // MOCK DATA FIELDS FOR UI BINDING
    public string Email { get; set; } = "-";
    public string Address { get; set; } = "-";
    public string BirthdayText { get; set; } = "-";
    public string LastLoginText { get; set; } = "-";

    // BADGE COLORS
    public string RankBackground => Rank.ToLower().Contains("thách đấu") ? "#9333EA" :
                                     Rank.ToLower().Contains("đại cao thủ") ? "#EA580C" :
                                     Rank.ToLower().Contains("kim cương") ? "#2563EB" :
                                     Rank.ToLower().Contains("vàng") ? "#F59E0B" : "#F1F5F9";
    public string RankForeground => Rank.ToLower().Contains("sắt") ? "#475569" : "White";

    public string TypeBackground => MemberType.ToUpper() == "VIP" ? "#E0E7FF" : "#F0FDF4";
    public string TypeForeground => MemberType.ToUpper() == "VIP" ? "#4F46E5" : "#16A34A";

    public string? AvatarId { get; set; }
    public string AvatarInitial => !string.IsNullOrWhiteSpace(Username) ? Username.Substring(0, 1).ToUpper() : "?";
    public System.Windows.Media.Brush? AvatarBrush
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(AvatarId) && AvatarId.StartsWith("data:image/"))
            {
                try
                {
                    var commaIndex = AvatarId.IndexOf(',');
                    if (commaIndex > 0)
                    {
                        var bytes = Convert.FromBase64String(AvatarId.Substring(commaIndex + 1));
                        using var stream = new System.IO.MemoryStream(bytes);
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 128;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return new System.Windows.Media.ImageBrush(bitmap) { Stretch = System.Windows.Media.Stretch.UniformToFill };
                    }
                }
                catch { }
            }
            return null;
        }
    }
    public System.Windows.Visibility AvatarFallbackVisibility => AvatarBrush == null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility AvatarImageVisibility => AvatarBrush != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public string StatusDotColor => IsActive ? "#10B981" : "#EF4444";
}

public sealed class MemberTransactionRow
{
    public string CreatedAtText { get; set; } = "-";
    public string TypeText { get; set; } = "-";
    public string AmountDeltaText { get; set; } = "0";
    public string PlayMinutesDeltaText { get; set; } = "0";
    public string CreatedBy { get; set; } = "-";
    public string Note { get; set; } = "-";
}

public sealed class SystemEventsResponse
{
    public List<SystemEventItem> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class ClearSystemEventsResponse
{
    public int DeletedCount { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class SystemEventItem
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? PcName { get; set; }
    public string? AgentId { get; set; }
    public object? Payload { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class SystemLogRow
{
    public string CreatedAtText { get; set; } = "-";
    public string Source { get; set; } = "-";
    public string EventType { get; set; } = "-";
    public string PcText { get; set; } = "-";
    public string PayloadText { get; set; } = "-";
}

public sealed class MachineTimelineRow
{
    public string TimeText { get; set; } = "-";
    public string EventText { get; set; } = "-";
    public string DetailsText { get; set; } = "-";
}

public sealed class SessionsListResponse
{
    public List<SessionItem> Items { get; set; } = new();
}

public sealed class SessionItem
{
    public string PcName { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public string? EndedAt { get; set; }
    public int? BillableMinutes { get; set; }
    public decimal? Amount { get; set; }
    public string? ClosedReason { get; set; }
}

public sealed class SessionLogRow
{
    public string PcName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StartedAtText { get; set; } = "-";
    public string EndedAtText { get; set; } = "-";
    public string BillableMinutesText { get; set; } = "-";
    public decimal AmountRaw { get; set; }
    public string AmountText { get; set; } = "0";
    public string Status { get; set; } = "-";
    public string ClosedReason { get; set; } = "-";
}

public sealed class MachineBillingSummaryRow
{
    public string MachineName { get; set; } = "-";
    public string StatusText { get; set; } = "-";
    public string PlayDurationText { get; set; } = "0 phút";
    public string HourlyRateText { get; set; } = "0";
    public decimal PlayAmount { get; set; }
    public decimal ServiceAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string PlayAmountText { get; set; } = "0";
    public string ServiceAmountText { get; set; } = "0";
    public string TotalAmountText { get; set; } = "0";
    public bool HasServiceLoadError { get; set; }
    public string NoteText { get; set; } = "-";
}

public sealed class WebsiteLogSettingsResponse
{
    public bool Enabled { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class WebsiteLogsResponse
{
    public List<WebsiteLogItem> Items { get; set; } = new();
    public int Total { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class ClearWebsiteLogsResponse
{
    public int DeletedCount { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class WebsiteLogItem
{
    public string Id { get; set; } = string.Empty;
    public string? PcId { get; set; }
    public string? PcName { get; set; }
    public string? AgentId { get; set; }
    public string Domain { get; set; } = "-";
    public string Url { get; set; } = "-";
    public string Title { get; set; } = "-";
    public string Browser { get; set; } = "-";
    public string VisitedAt { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class WebsiteLogRow
{
    public string Id { get; set; } = string.Empty;
    public string PcName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string Domain { get; set; } = "-";
    public string Url { get; set; } = "-";
    public string Title { get; set; } = "-";
    public string Browser { get; set; } = "-";
    public string VisitedAtText { get; set; } = "-";
}

public sealed class RevenueSummaryResponse
{
    public string Period { get; set; } = "day";
    public string AnchorDate { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string RangeStart { get; set; } = string.Empty;
    public string RangeEndExclusive { get; set; } = string.Empty;
    public int ClosedSessions { get; set; }
    public int ServiceOrders { get; set; }
    public decimal SessionAmount { get; set; }
    public decimal ServiceAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class GroupSummaryRow
{
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = "M\u1eb7c \u0111\u1ecbnh";
    public decimal HourlyRate { get; set; }
    public decimal MemberHourlyRate { get; set; }
    public bool IsDefault { get; set; }
    public string IsDefaultText => IsDefault ? "Có" : "-";
    public int Total { get; set; }
    public double Percentage { get; set; }
    public int InUse { get; set; }
    public int Locked { get; set; }
    public int Online { get; set; }
    public int Offline { get; set; }
}

public sealed class GroupMachineRow
{
    public string PcId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = "M\u1eb7c \u0111\u1ecbnh";
    public decimal HourlyRate { get; set; }
    public decimal MemberHourlyRate { get; set; }
    public string MachineName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StatusText { get; set; } = "-";
    public string IpAddress { get; set; } = "-";
    public string LastSeenAtText { get; set; } = "-";
}

public sealed class PricingSettingsResponse
{
    public decimal DefaultRatePerHour { get; set; }
    public decimal DefaultMemberRatePerHour { get; set; }
    public string? DefaultGroupId { get; set; }
    public List<PricingGroupItem> Groups { get; set; } = new();
}

public sealed class PricingGroupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public decimal MemberHourlyRate { get; set; }
    public bool IsDefault { get; set; }
    public int MachineCount { get; set; }
}

public sealed class ServiceItemsResponse
{
    public List<ServiceItemDto> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class ServiceItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
    public string? ImageDataUrl { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class ServiceItemRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "-";
    public decimal UnitPrice { get; set; }
    public string UnitPriceText { get; set; } = "0";
    public bool IsActive { get; set; }
    public string? ImageDataUrl { get; set; }
    public ImageSource? ServiceImageSource { get; set; }
    public bool HasImage => ServiceImageSource is not null;
    public string ActiveText => IsActive ? "Đang bán" : "Tạm ngưng";
    public string UpdatedAtText { get; set; } = "-";
    public string DisplayText => $"{Name} - {UnitPrice:N0} VND";
}

public sealed class PcServiceOrdersResponse
{
    public string PcId { get; set; } = string.Empty;
    public List<PcServiceOrderDto> Items { get; set; } = new();
    public int Total { get; set; }
}

public sealed class PcServiceOrderDto
{
    public string Id { get; set; } = string.Empty;
    public string PcId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public bool IsPaid { get; set; }
    public ServiceItemDto ServiceItem { get; set; } = new();
}

public sealed class PayPcServiceOrdersResponse
{
    public string PcId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int PaidOrderCount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal UnpaidAmount { get; set; }
}

public sealed class CancelPcServiceOrdersResponse
{
    public string PcId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int CanceledOrderCount { get; set; }
    public int CanceledQuantity { get; set; }
    public decimal CanceledAmount { get; set; }
    public decimal UnpaidAmount { get; set; }
}

public sealed class CaptureScreenshotRequestResponse
{
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public string? RequestId { get; set; }
    public string? SentAt { get; set; }
}

public sealed class LatestPcScreenshotResponse
{
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public PcScreenshotItem? Screenshot { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class PcScreenshotItem
{
    public string EventId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? CapturedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class LatestPcLiveFrameResponse
{
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public PcLiveFrameItem? Frame { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class PcLiveFrameItem
{
    public string? RequestId { get; set; }
    public string ImageBase64 { get; set; } = string.Empty;
    public string MimeType { get; set; } = "image/jpeg";
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? CapturedAt { get; set; }
    public string UploadedAt { get; set; } = string.Empty;
}

public sealed class LoyaltyRankItem
{
    public string Id { get; set; } = string.Empty;
    public string RankName { get; set; } = string.Empty;
    public decimal MinTopup { get; set; }
    public decimal BonusPercent { get; set; }
    public double MinutesPerPoint { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class LoyaltyRankRow
{
    public string Id { get; set; } = string.Empty;
    public string RankName { get; set; } = string.Empty;
    public decimal MinTopup { get; set; }
    public decimal BonusPercent { get; set; }
    public string MinTopupText { get; set; } = "0";
    public double MinutesPerPoint { get; set; }
    public string MinutesPerPointText 
    { 
        get 
        {
            var totalSeconds = (int)(MinutesPerPoint * 60);
            var mins = totalSeconds / 60;
            var secs = totalSeconds % 60;
            if (secs > 0)
                return $"{mins} phút {secs} giây";
            return $"{mins} phút";
        }
    }
}

public sealed class LoyaltySpinSettingRow
{
    public int Minutes { get; set; }
    public decimal Chance { get; set; }
    public string MinutesText => $"{Minutes}p";
    public string Label { get; set; } = string.Empty;
}

public class ServerUserRow
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class DatabaseStorageStatsResponse
{
    public string DatabaseName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizePretty { get; set; } = string.Empty;
    public long DayDeltaBytes { get; set; }
    public decimal DayDeltaPercent { get; set; }
    public List<DatabaseStorageTableRow> TopTables { get; set; } = new();
    public List<DatabaseStorageHistoryRow> History { get; set; } = new();
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class DatabaseStorageTableRow
{
    public string TableName { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public string TotalPretty { get; set; } = string.Empty;
    public long TableBytes { get; set; }
    public string TablePretty { get; set; } = string.Empty;
    public long IndexToastBytes { get; set; }
    public string IndexToastPretty { get; set; } = string.Empty;
}

public sealed class DatabaseStorageHistoryRow
{
    public string Date { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizePretty { get; set; } = string.Empty;
}

public sealed class TopupPromoTierDto
{
    public decimal MinAmount { get; set; }
    public decimal BonusRate { get; set; }
}

public sealed class TopupPromoTierRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string MinAmountText { get; set; } = "0";
    public string BonusRateText { get; set; } = "0";
}

public sealed class ServiceCategoryViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isSelected;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public int Count { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(BackgroundBrush)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ForegroundBrush)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IconBrush)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CountBackgroundBrush)));
            }
        }
    }

    public Brush BackgroundBrush => IsSelected ? new SolidColorBrush(Color.FromRgb(254, 226, 226)) : Brushes.Transparent;
    public Brush ForegroundBrush => IsSelected ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(71, 85, 105));
    public Brush IconBrush => IsSelected ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(100, 116, 139));
    public Brush CountBackgroundBrush => IsSelected ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(241, 245, 249));

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ServiceTopItemViewModel
{
    public string RankNumber { get; set; } = string.Empty;
    public string RankBackground { get; set; } = "#F3F4F6";
    public string RankForeground { get; set; } = "#374151";
    public string ServiceName { get; set; } = string.Empty;
    public string MetaText { get; set; } = string.Empty;
    public string RevenueText { get; set; } = string.Empty;
}

public sealed class ServiceCategoryRevenueViewModel
{
    public string CategoryName { get; set; } = string.Empty;
    public string RevenueText { get; set; } = string.Empty;
    public string PercentageText { get; set; } = string.Empty;
    public double ProgressValue { get; set; }
    public string BarColor { get; set; } = string.Empty;
    public string DotColor { get; set; } = string.Empty;
}

