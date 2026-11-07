using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;

namespace Server.Admin.App;

public sealed class AdminShellSettings
{
    public string AdminBaseUrl { get; set; } = "http://127.0.0.1:5400";

    public string BackendApiBaseUrl { get; set; } = "http://localhost:39090/api/v1";

    public string StartPath { get; set; } = "/pcs";

    public int MachineRefreshSeconds { get; set; } = 3;

    public double UiFontSize { get; set; } = 13;

    public string UiFontFamily { get; set; } = "Segoe UI";

    public double MainMenuTabIconSize { get; set; } = 18;

    public double MachineTableFontSize { get; set; } = 14;

    public double MachineTableIconSize { get; set; } = 32;

    public double MachineTableItemSpacing { get; set; } = 10;

    public double MachineContextMenuItemPadding { get; set; } = 12;

    public double MachineContextMenuFontSize { get; set; } = 14;

    public bool HideDisabledContextMenuItems { get; set; } = true;

    public bool ShowOfflineMachines { get; set; } = true;

    public Dictionary<string, WakeLanProfile> WakeLanProfiles { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public bool EnableMembersPagination { get; set; } = false;

    public bool AutoBackupOnExit { get; set; } = true;

    public bool IsLiteMode { get; set; } = false;

    public string ColorInUseBg { get; set; } = "#EFF6FF";
    public string ColorInUseFg { get; set; } = "#2563EB";
    public string ColorAdminBg { get; set; } = "#FEF9C3";
    public string ColorAdminFg { get; set; } = "#B45309";
    public string ColorOfflineBg { get; set; } = "#FEF2F2";
    public string ColorOfflineFg { get; set; } = "#DC2626";
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

    public string TaxBusinessOwner { get; set; } = "Nguyễn Văn A";
    public string TaxIdNumber { get; set; } = "0312345678";
    public string TaxAddress { get; set; } = "12 Đường Mẫu, Quận 1, TP. Hồ Chí Minh";
    public string TaxStoreLocation { get; set; } = "Phòng máy CyberS";
    public string TaxCircularReference { get; set; } = "Kèm theo Thông tư số 152/2025/TT-BTC ngày 31 tháng 12 năm 2025 của Bộ trưởng Bộ Tài chính";
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
    public int? TotalAll { get; set; }
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
    public string? LastLoginAt { get; set; }
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
    public decimal MaxMemberTopupAmount { get; set; } = 100000m;
    public bool VpnEnabled { get; set; } = true;
    public string? VpnLocalInstallerPath { get; set; }
    public string? VpnDownloadUrl { get; set; }
    public bool DesktopWallpaperEnabled { get; set; }
    public string? DesktopWallpaperUrl { get; set; }
    public string? DesktopWallpaperStyle { get; set; }
    public string ServerTime { get; set; } = string.Empty;
}

public sealed class DesktopWallpaperUploadResponse
{
    public bool Success { get; set; }
    public string? Url { get; set; }
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
    public bool BackupOnExit { get; set; } = true;
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

public sealed class MemberRow : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string prop) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(prop));
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
    private string _lastLoginText = "Chưa đăng nhập";
    public string LastLoginText
    {
        get => _lastLoginText;
        set { _lastLoginText = value; OnPropertyChanged(nameof(LastLoginText)); }
    }

    // BADGE COLORS
    private static string NormalizeRank(string? rank)
    {
        if (string.IsNullOrWhiteSpace(rank)) return string.Empty;
        var lower = rank.ToLowerInvariant();
        return lower
            .Replace("đ", "d")
            .Replace("á", "a").Replace("à", "a").Replace("ả", "a").Replace("ã", "a").Replace("ạ", "a")
            .Replace("ă", "a").Replace("ắ", "a").Replace("ằ", "a").Replace("ẳ", "a").Replace("ẵ", "a").Replace("ặ", "a")
            .Replace("â", "a").Replace("ấ", "a").Replace("ầ", "a").Replace("ẩ", "a").Replace("ẫ", "a").Replace("ậ", "a")
            .Replace("é", "e").Replace("è", "e").Replace("ẻ", "e").Replace("ẽ", "e").Replace("ẹ", "e")
            .Replace("ê", "e").Replace("ế", "e").Replace("ề", "e").Replace("ể", "e").Replace("ễ", "e").Replace("ệ", "e")
            .Replace("í", "i").Replace("ì", "i").Replace("ỉ", "i").Replace("ĩ", "i").Replace("ị", "i")
            .Replace("ó", "o").Replace("ò", "o").Replace("ỏ", "o").Replace("õ", "o").Replace("ọ", "o")
            .Replace("ô", "o").Replace("ố", "o").Replace("ồ", "o").Replace("ổ", "o").Replace("ỗ", "o").Replace("ộ", "o")
            .Replace("ơ", "o").Replace("ớ", "o").Replace("ờ", "o").Replace("ở", "o").Replace("ỡ", "o").Replace("ợ", "o")
            .Replace("ú", "u").Replace("ù", "u").Replace("ủ", "u").Replace("ũ", "u").Replace("ụ", "u")
            .Replace("ư", "u").Replace("ứ", "u").Replace("ừ", "u").Replace("ử", "u").Replace("ữ", "u").Replace("ự", "u")
            .Replace("ý", "y").Replace("ỳ", "y").Replace("ỷ", "y").Replace("ỹ", "y").Replace("ỵ", "y");
    }

    public string RankBackground
    {
        get
        {
            var r = NormalizeRank(Rank);
            if (r.Contains("thach dau") || r.Contains("challenger")) return "#9333EA"; // Tím rực rỡ
            if (r.Contains("dai cao thu") || r.Contains("grandmaster")) return "#DC2626"; // Đỏ thẫm
            if (r.Contains("cao thu") || r.Contains("master")) return "#C026D3"; // Tím cánh sen
            if (r.Contains("kim cuong") || r.Contains("diamond")) return "#2563EB"; // Xanh hoàng gia
            if (r.Contains("tinh anh") || r.Contains("emerald")) return "#059669"; // Xanh lục bảo
            if (r.Contains("bach kim") || r.Contains("platinum")) return "#0D9488"; // Xanh mòng két
            if (r.Contains("vang") || r.Contains("gold")) return "#F59E0B"; // Vàng hổ phách
            if (r.Contains("bac") || r.Contains("silver")) return "#64748B"; // Bạc ánh kim slate
            if (r.Contains("dong") || r.Contains("bronze")) return "#B45309"; // Đồng ánh cam đậm
            if (r.Contains("sat") || r.Contains("iron")) return "#F1F5F9"; // Sắt xám nhạt
            return "#F1F5F9";
        }
    }

    public string RankForeground
    {
        get
        {
            var r = NormalizeRank(Rank);
            if (r.Contains("sat") || r.Contains("iron"))
                return "#334155"; // Xám đậm rõ nét trên nền #F1F5F9
            if (r.Contains("thach dau") || r.Contains("challenger") ||
                r.Contains("dai cao thu") || r.Contains("grandmaster") ||
                r.Contains("cao thu") || r.Contains("master") ||
                r.Contains("kim cuong") || r.Contains("diamond") ||
                r.Contains("tinh anh") || r.Contains("emerald") ||
                r.Contains("bach kim") || r.Contains("platinum") ||
                r.Contains("vang") || r.Contains("gold") ||
                r.Contains("bac") || r.Contains("silver") ||
                r.Contains("dong") || r.Contains("bronze"))
            {
                return "#FFFFFF"; // Chữ trắng tương phản cao trên nền màu
            }
            return "#334155"; // Mặc định an toàn: luôn rõ nét, không bao giờ bị trắng mờ
        }
    }

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

    public string SourceBg => Source?.ToUpperInvariant() switch
    {
        "SERVER" => "#F5F3FF",
        "CLIENT" => "#EFF6FF",
        "ADMIN" => "#FFFBEB",
        _ => "#F1F5F9"
    };

    public string SourceFg => Source?.ToUpperInvariant() switch
    {
        "SERVER" => "#7C3AED",
        "CLIENT" => "#2563EB",
        "ADMIN" => "#D97706",
        _ => "#475569"
    };

    public string SourceBorder => Source?.ToUpperInvariant() switch
    {
        "SERVER" => "#DDD6FE",
        "CLIENT" => "#BFDBFE",
        "ADMIN" => "#FDE68A",
        _ => "#E2E8F0"
    };

    public bool IsAlertEvent => (EventType?.Contains("Mất kết nối", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (EventType?.Contains("Lỗi", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (EventType?.Contains("Warning", StringComparison.OrdinalIgnoreCase) ?? false) ||
                                (EventType?.Contains("Khóa máy", StringComparison.OrdinalIgnoreCase) ?? false);

    public string EventFg => IsAlertEvent ? "#DC2626" : "#0F172A";

    public string EventIcon => EventType switch
    {
        var e when string.IsNullOrEmpty(e) => "📝",
        var e when e.Contains("Mất kết nối", StringComparison.OrdinalIgnoreCase) => "⚠️",
        var e when e.Contains("nạp tiền", StringComparison.OrdinalIgnoreCase) => "💳",
        var e when e.Contains("Chuyển tiền", StringComparison.OrdinalIgnoreCase) => "💸",
        var e when e.Contains("đăng nhập", StringComparison.OrdinalIgnoreCase) => "🔑",
        var e when e.Contains("đăng xuất", StringComparison.OrdinalIgnoreCase) => "🚪",
        var e when e.Contains("Trạng thái", StringComparison.OrdinalIgnoreCase) => "🖥️",
        var e when e.Contains("web", StringComparison.OrdinalIgnoreCase) => "🌐",
        var e when e.Contains("số dư", StringComparison.OrdinalIgnoreCase) => "💰",
        _ => "📝"
    };
}

public sealed class MachineTimelineRow
{
    public string TimeText { get; set; } = "-";
    public string EventText { get; set; } = "-";
    public string DetailsText { get; set; } = "-";

    public string DatePart => TimeText.Contains(' ') ? TimeText.Split(' ')[0] : TimeText;
    public string TimePart => TimeText.Contains(' ') ? TimeText.Split(' ')[1] : "";

    public string EventIcon => EventText switch
    {
        var e when string.IsNullOrEmpty(e) => "📝",
        var e when e.Contains("Mất kết nối", StringComparison.OrdinalIgnoreCase) => "⚠️",
        var e when e.Contains("nạp tiền", StringComparison.OrdinalIgnoreCase) => "💳",
        var e when e.Contains("Chuyển tiền", StringComparison.OrdinalIgnoreCase) => "💸",
        var e when e.Contains("đăng nhập", StringComparison.OrdinalIgnoreCase) => "🔑",
        var e when e.Contains("đăng xuất", StringComparison.OrdinalIgnoreCase) => "🚪",
        var e when e.Contains("Trạng thái", StringComparison.OrdinalIgnoreCase) => "🖥️",
        var e when e.Contains("số dư", StringComparison.OrdinalIgnoreCase) => "💰",
        _ => "🔹"
    };
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
    public string TransactionType { get; set; } = "SESSION";
    public string TypeLabel { get; set; } = "Phiên máy";
    public string TypeIcon { get; set; } = "🖥";
    public string TypeBg { get; set; } = "#EFF6FF";
    public string TypeFg { get; set; } = "#2563EB";
    public string TypeBorder { get; set; } = "#BFDBFE";

    public string PcName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StartedAtText { get; set; } = "-";
    public string EndedAtText { get; set; } = "-";
    public string BillableMinutesText { get; set; } = "-";
    public decimal AmountRaw { get; set; }
    public string AmountText { get; set; } = "0";
    public string AmountDisplay => AmountRaw > 0 ? $"+{AmountText}" : AmountText;
    public string AmountFg => TransactionType.StartsWith("TOPUP") && AmountRaw > 0 ? "#059669" : (AmountRaw < 0 ? "#DC2626" : (AmountRaw == 0 ? "#64748B" : "#2563EB"));
    public string Status { get; set; } = "-";
    public string ClosedReason { get; set; } = "-";
    public DateTime CreatedAt { get; set; } = DateTime.MinValue;

    public string StatusBg => Status?.ToUpperInvariant() switch
    {
        "ACTIVE" => "#ECFDF5",
        "RUNNING" => "#ECFDF5",
        "COMPLETED" => "#ECFDF5",
        "APPROVED" => "#ECFDF5",
        "PENDING" => "#FEF3C7",
        "REJECTED" => "#FEF2F2",
        "CLOSED" => "#F8FAFC",
        _ => "#F1F5F9"
    };

    public string StatusFg => Status?.ToUpperInvariant() switch
    {
        "ACTIVE" => "#15803D",
        "RUNNING" => "#15803D",
        "COMPLETED" => "#059669",
        "APPROVED" => "#059669",
        "PENDING" => "#D97706",
        "REJECTED" => "#DC2626",
        "CLOSED" => "#475569",
        _ => "#64748B"
    };

    public string StatusBorder => Status?.ToUpperInvariant() switch
    {
        "ACTIVE" => "#A7F3D0",
        "RUNNING" => "#A7F3D0",
        "COMPLETED" => "#A7F3D0",
        "APPROVED" => "#A7F3D0",
        "PENDING" => "#FDE68A",
        "REJECTED" => "#FECACA",
        "CLOSED" => "#E2E8F0",
        _ => "#CBD5E1"
    };
}

public sealed class MachineBillingSummaryRow
{
    public MachineRow? Machine { get; set; }
    public string MachineName { get; set; } = "-";
    public string CustomerName { get; set; } = "-";
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

    public string TierGroup
    {
        get
        {
            var name = RankName?.Trim() ?? string.Empty;
            if (name.StartsWith("Bạch Kim", StringComparison.OrdinalIgnoreCase)) return "Bạch Kim";
            if (name.StartsWith("Đại Cao Thủ", StringComparison.OrdinalIgnoreCase)) return "Đại Cao Thủ";
            if (name.StartsWith("Sắt", StringComparison.OrdinalIgnoreCase)) return "Sắt";
            if (name.StartsWith("Đồng", StringComparison.OrdinalIgnoreCase)) return "Đồng";
            if (name.StartsWith("Bạc", StringComparison.OrdinalIgnoreCase)) return "Bạc";
            if (name.StartsWith("Vàng", StringComparison.OrdinalIgnoreCase)) return "Vàng";
            if (name.StartsWith("Tinh Anh", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Tinh Vân", StringComparison.OrdinalIgnoreCase)) return "Tinh Anh";
            if (name.StartsWith("Kim Cương", StringComparison.OrdinalIgnoreCase)) return "Kim Cương";
            if (name.StartsWith("Cao Thủ", StringComparison.OrdinalIgnoreCase)) return "Cao Thủ";
            if (name.StartsWith("Thách Đấu", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Thần Thoại", StringComparison.OrdinalIgnoreCase)) return "Thách Đấu";
            return "Khác";
        }
    }

    public string TierIcon
    {
        get => TierGroup switch
        {
            "Sắt" => "\U0001F6E1",
            "Đồng" => "\U0001F949",
            "Bạc" => "\U0001F948",
            "Vàng" => "\U0001F947",
            "Bạch Kim" => "\U0001F48E",
            "Tinh Anh" => "\U0001F52E",
            "Kim Cương" => "\U0001F539",
            "Cao Thủ" => "\U0001F451",
            "Đại Cao Thủ" => "\U0001F396",
            "Thách Đấu" => "\U0001F525",
            _ => "\u2B50"
        };
    }

    public string TierBgColor
    {
        get => TierGroup switch
        {
            "Sắt" => "#F1F5F9",
            "Đồng" => "#FEF3C7",
            "Bạc" => "#F1F5F9",
            "Vàng" => "#FEF9C3",
            "Bạch Kim" => "#CCFBF1",
            "Tinh Anh" => "#EDE9FE",
            "Kim Cương" => "#DBEAFE",
            "Cao Thủ" => "#FEE2E2",
            "Đại Cao Thủ" => "#FFEDD5",
            "Thách Đấu" => "#FCE7F3",
            _ => "#F3F4F6"
        };
    }

    public string TierBorderColor
    {
        get => TierGroup switch
        {
            "Sắt" => "#CBD5E1",
            "Đồng" => "#FDE68A",
            "Bạc" => "#CBD5E1",
            "Vàng" => "#FDE047",
            "Bạch Kim" => "#99F6E4",
            "Tinh Anh" => "#DDD6FE",
            "Kim Cương" => "#BFDBFE",
            "Cao Thủ" => "#FECACA",
            "Đại Cao Thủ" => "#FED7AA",
            "Thách Đấu" => "#FBCFE8",
            _ => "#E5E7EB"
        };
    }

    public string TierTextColor
    {
        get => TierGroup switch
        {
            "Sắt" => "#475569",
            "Đồng" => "#B45309",
            "Bạc" => "#334155",
            "Vàng" => "#B45309",
            "Bạch Kim" => "#0F766E",
            "Tinh Anh" => "#6D28D9",
            "Kim Cương" => "#1D4ED8",
            "Cao Thủ" => "#B91C1C",
            "Đại Cao Thủ" => "#C2410C",
            "Thách Đấu" => "#BE185D",
            _ => "#374151"
        };
    }

    public string BonusPercentText => $"+{BonusPercent:0.#}%";
    public string MinTopupFormatted => $"{MinTopup:N0} VNĐ";
}

public sealed class LoyaltySpinSettingRow : System.ComponentModel.INotifyPropertyChanged
{
    private int _minutes;
    private decimal _chance;
    private string _label = string.Empty;

    public int Minutes
    {
        get => _minutes;
        set
        {
            if (_minutes != value)
            {
                _minutes = value;
                OnPropertyChanged(nameof(Minutes));
                OnPropertyChanged(nameof(MinutesText));
                OnPropertyChanged(nameof(RarityBadge));
                OnPropertyChanged(nameof(RarityBackground));
                OnPropertyChanged(nameof(RarityForeground));
                OnPropertyChanged(nameof(BarBrush));
            }
        }
    }

    public decimal Chance
    {
        get => _chance;
        set
        {
            if (_chance != value)
            {
                _chance = value;
                OnPropertyChanged(nameof(Chance));
                OnPropertyChanged(nameof(ChanceFormatted));
                OnPropertyChanged(nameof(ProgressValue));
            }
        }
    }

    public string MinutesText
    {
        get => $"{Minutes} phút";
        set { }
    }

    public string ChanceFormatted
    {
        get => $"{Chance:0.####}%";
        set { }
    }

    public double ProgressValue
    {
        get => (double)Math.Clamp(Chance, 0m, 100m);
        set { }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (_label != value)
            {
                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }
    }

    public string RarityBadge
    {
        get => Minutes switch
        {
            0 => "Không trúng",
            <= 2 => "Phổ biến",
            <= 6 => "Thường",
            <= 10 => "Hiếm",
            <= 20 => "Rất hiếm",
            _ => "Jackpot ⭐"
        };
        set { }
    }

    public string RarityBackground
    {
        get => Minutes switch
        {
            0 => "#F1F5F9",
            <= 2 => "#ECFDF5",
            <= 6 => "#EFF6FF",
            <= 10 => "#FEF3C7",
            <= 20 => "#FAF5FF",
            _ => "#FEF2F2"
        };
        set { }
    }

    public string RarityForeground
    {
        get => Minutes switch
        {
            0 => "#64748B",
            <= 2 => "#059669",
            <= 6 => "#2563EB",
            <= 10 => "#D97706",
            <= 20 => "#7C3AED",
            _ => "#DC2626"
        };
        set { }
    }

    public string BarBrush
    {
        get => Minutes switch
        {
            0 => "#94A3B8",
            <= 2 => "#10B981",
            <= 6 => "#3B82F6",
            <= 10 => "#F59E0B",
            <= 20 => "#8B5CF6",
            _ => "#EF4444"
        };
        set { }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
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

public sealed class TransferredBillItem
{
    public string Id { get; set; } = string.Empty;
    public string FromPcId { get; set; } = string.Empty;
    public string FromPcName { get; set; } = string.Empty;
    public string FromSessionId { get; set; } = string.Empty;
    public decimal PlayAmount { get; set; }
    public int PlayDurationSeconds { get; set; }
    public string PlayDurationText { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
    public decimal ServiceAmount { get; set; }
    public string ServiceItemsSummary { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public string TransferredAt { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed class TransferredBillsResponse
{
    public string PcId { get; set; } = string.Empty;
    public List<TransferredBillItem> Bills { get; set; } = new();
    public decimal TotalTransferredPlayAmount { get; set; }
    public decimal TotalTransferredServiceAmount { get; set; }
    public decimal TotalTransferredAmount { get; set; }
}

public sealed class TransferBillingRequest
{
    public string TargetPcId { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? RequestedBy { get; set; } = "admin.desktop";
}

public sealed class TransferBillingResponse
{
    public bool Success { get; set; }
    public string FromPcName { get; set; } = string.Empty;
    public string TargetPcName { get; set; } = string.Empty;
    public TransferredBillItem? TransferredBill { get; set; }
    public List<TransferredBillItem> AllTargetBills { get; set; } = new();
    public decimal TotalTransferredToTarget { get; set; }
}

public sealed class ServiceOrderBatchInput
{
    public List<ServiceOrderLineInput> Lines { get; init; } = new();
    public string? Note { get; init; }
    public decimal Total { get; init; }
    public List<string> AcknowledgedClientOrderIds { get; init; } = new();
    public List<string> CanceledClientOrderIds { get; init; } = new();
}

public sealed class ServiceOrderLineInput
{
    public string ServiceItemId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal LineTotal { get; init; }
}

public sealed class ExistingServiceOrderSummary
{
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public int ClientQuantity { get; set; }
    public decimal ClientAmount { get; set; }
    public int ServerQuantity { get; set; }
    public decimal ServerAmount { get; set; }

    public void AddFromOrder(int quantity, decimal amount, bool fromClient)
    {
        var safeQuantity = Math.Max(0, quantity);
        var safeAmount = Math.Max(0, amount);
        Quantity += safeQuantity;
        Amount += safeAmount;

        if (fromClient)
        {
            ClientQuantity += safeQuantity;
            ClientAmount += safeAmount;
            return;
        }

        ServerQuantity += safeQuantity;
        ServerAmount += safeAmount;
    }
}

public sealed class PreviouslyOrderedDisplayItem
{
    public string ServiceItemId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Category { get; init; } = "-";
    public ImageSource? ServiceImageSource { get; init; }
    public bool HasImage => ServiceImageSource is not null;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public int ClientQuantity { get; init; }
    public decimal ClientAmount { get; init; }
    public int ServerQuantity { get; init; }
    public decimal ServerAmount { get; init; }
    
    public string QuantityText => $"x{Quantity:N0}";
    public string AmountText => $"{Amount:N0} đ";
    public string SourceDetailText => $"Khách: {ClientQuantity:N0} ({ClientAmount:N0} đ) • Server: {ServerQuantity:N0} ({ServerAmount:N0} đ)";
}

public sealed class CategoryFilterChip : INotifyPropertyChanged
{
    private bool _isSelected;
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
    public string DisplayText => Count > 0 ? $"{Name} ({Count})" : Name;
    
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BorderBrush)));
        }
    }
    
    public Brush Background => IsSelected
        ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) // #2563EB
        : new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9

    public Brush Foreground => IsSelected
        ? Brushes.White
        : new SolidColorBrush(Color.FromRgb(51, 65, 85)); // #334155

    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(29, 78, 216)) // #1D4ED8
        : new SolidColorBrush(Color.FromRgb(226, 232, 240)); // #E2E8F0

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ServiceOrderSelectionRow : INotifyPropertyChanged
{
    private int _quantity;
    public int MinQuantity => -Math.Max(0, PreviouslyOrderedQuantity);

    public string ServiceItemId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Category { get; init; } = "-";
    public ImageSource? ServiceImageSource { get; init; }
    public bool HasImage => ServiceImageSource is not null;
    public decimal UnitPrice { get; init; }
    public int PreviouslyOrderedQuantity { get; init; }
    public decimal PreviouslyOrderedAmount { get; init; }
    public string UnitPriceText => UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
    public string PreviouslyOrderedText =>
        PreviouslyOrderedQuantity <= 0
            ? "-"
            : $"{PreviouslyOrderedQuantity:N0} ({PreviouslyOrderedAmount:N0} đ)";

    public int Quantity
    {
        get => _quantity;
        set
        {
            var clamped = Math.Clamp(value, MinQuantity, 999);
            if (_quantity == clamped) return;
            _quantity = clamped;
            NotifyQuantityChanged();
        }
    }

    public decimal LineTotal => UnitPrice * Quantity;
    public string LineTotalText => LineTotal == 0 ? "0 đ" : (LineTotal > 0 ? $"+{LineTotal:N0} đ" : $"{LineTotal:N0} đ");

    public bool IsSelectedForAdd => Quantity > 0;
    public bool IsSelectedForCancel => Quantity < 0;
    public bool HasPreviousOrders => PreviouslyOrderedQuantity > 0;

    public Brush QuantityForeground
    {
        get
        {
            if (Quantity > 0) return new SolidColorBrush(Color.FromRgb(22, 163, 74)); // Green-600
            if (Quantity < 0) return new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red-600
            return new SolidColorBrush(Color.FromRgb(148, 163, 184)); // Slate-400
        }
    }

    public Brush LineTotalForeground
    {
        get
        {
            if (Quantity > 0) return new SolidColorBrush(Color.FromRgb(22, 163, 74));
            if (Quantity < 0) return new SolidColorBrush(Color.FromRgb(220, 38, 38));
            return new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
    }

    public Brush CategoryBgBrush
    {
        get
        {
            var cat = Category.ToLowerInvariant();
            if (cat.Contains("nước") || cat.Contains("nuoc") || cat.Contains("uống")) return new SolidColorBrush(Color.FromRgb(239, 246, 255)); // #EFF6FF
            if (cat.Contains("ăn") || cat.Contains("cơm") || cat.Contains("mì") || cat.Contains("đồ ăn")) return new SolidColorBrush(Color.FromRgb(254, 243, 199)); // #FEF3C7
            if (cat.Contains("vặt") || cat.Contains("snack")) return new SolidColorBrush(Color.FromRgb(236, 253, 245)); // #ECFDF5
            return new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9
        }
    }

    public Brush CategoryFgBrush
    {
        get
        {
            var cat = Category.ToLowerInvariant();
            if (cat.Contains("nước") || cat.Contains("nuoc") || cat.Contains("uống")) return new SolidColorBrush(Color.FromRgb(37, 99, 235)); // #2563EB
            if (cat.Contains("ăn") || cat.Contains("cơm") || cat.Contains("mì") || cat.Contains("đồ ăn")) return new SolidColorBrush(Color.FromRgb(180, 83, 9)); // #B45309
            if (cat.Contains("vặt") || cat.Contains("snack")) return new SolidColorBrush(Color.FromRgb(5, 150, 105)); // #059669
            return new SolidColorBrush(Color.FromRgb(71, 85, 105)); // #475569
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ServiceOrderSelectionRow FromServiceItem(
        ServiceItemRow item,
        ExistingServiceOrderSummary? existingSummary = null)
    {
        var previouslyOrderedQuantity = existingSummary?.Quantity ?? 0;
        var previouslyOrderedAmount = existingSummary?.Amount ?? 0;
        return new ServiceOrderSelectionRow
        {
            ServiceItemId = item.Id,
            ServiceName = item.Name,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category,
            ServiceImageSource = item.ServiceImageSource,
            UnitPrice = item.UnitPrice,
            PreviouslyOrderedQuantity = previouslyOrderedQuantity,
            PreviouslyOrderedAmount = previouslyOrderedAmount,
            Quantity = 0,
        };
    }

    public void IncreaseQuantity()
    {
        Quantity = Math.Min(999, Quantity + 1);
    }

    public void DecreaseQuantity()
    {
        Quantity = Math.Max(MinQuantity, Quantity - 1);
    }

    private void NotifyQuantityChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForAdd)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedForCancel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityForeground)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalForeground)));
    }
}

public sealed class PendingClientServiceOrderRow : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string OrderId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal Amount { get; init; }
    public string CreatedAtText { get; init; } = "-";
    public string QuantityText => $"x{Quantity:N0}";
    public string AmountText => $"{Amount:N0} đ";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static PendingClientServiceOrderRow FromOrder(PcServiceOrderDto order)
    {
        var timeStr = "-";
        if (!string.IsNullOrWhiteSpace(order.CreatedAt))
        {
            if (DateTime.TryParse(order.CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                timeStr = parsed.ToLocalTime().ToString("HH:mm:ss");
            }
            else
            {
                timeStr = order.CreatedAt;
            }
        }

        return new PendingClientServiceOrderRow
        {
            OrderId = order.Id,
            ServiceName = string.IsNullOrWhiteSpace(order.ServiceItem?.Name) ? "Dịch vụ" : order.ServiceItem.Name,
            Quantity = Math.Max(0, order.Quantity),
            Amount = Math.Max(0, order.LineTotal),
            CreatedAtText = timeStr,
            IsSelected = true,
        };
    }
}

public sealed class TaxReportResponse
{
    public string PeriodType { get; set; } = "month";
    public string PeriodLabel { get; set; } = "";
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public double TaxRatio { get; set; } = 100;
    public double VatRate { get; set; } = 5;
    public double PitRate { get; set; } = 2;
    public decimal TotalActualPlaytime { get; set; }
    public decimal TotalActualService { get; set; }
    public decimal TotalActualRevenue { get; set; }
    public decimal TotalTaxablePlaytime { get; set; }
    public decimal TotalTaxableService { get; set; }
    public decimal TotalTaxableRevenue { get; set; }
    public decimal TotalVatAmount { get; set; }
    public decimal TotalPitAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public List<TaxReportDailyItemResponse> DailyBreakdown { get; set; } = new();
    public string ServerTime { get; set; } = "";
}

public sealed class TaxReportDailyItemResponse
{
    public string Date { get; set; } = "";
    public string DayOfWeek { get; set; } = "";
    public int SessionCount { get; set; }
    public decimal ActualPlaytime { get; set; }
    public decimal ActualService { get; set; }
    public decimal ActualTotal { get; set; }
    public decimal TaxablePlaytime { get; set; }
    public decimal TaxableService { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal PitAmount { get; set; }
    public decimal TotalTax { get; set; }
}

public sealed class TaxReportDailyRow
{
    public int Index { get; set; }
    public string DateText { get; set; } = "-";
    public string DayOfWeekText { get; set; } = "-";
    public int SessionCount { get; set; }
    public decimal ActualPlaytimeRevenue { get; set; }
    public decimal ActualServiceRevenue { get; set; }
    public decimal ActualTotalRevenue => ActualPlaytimeRevenue + ActualServiceRevenue;
    public decimal TaxablePlaytimeRevenue { get; set; }
    public decimal TaxableServiceRevenue { get; set; }
    public decimal TaxableTotalRevenue => TaxablePlaytimeRevenue + TaxableServiceRevenue;
    public decimal VatAmount { get; set; }
    public decimal PitAmount { get; set; }
    public decimal TotalTaxAmount => VatAmount + PitAmount;

    public string ActualPlaytimeText => ActualPlaytimeRevenue.ToString("N0", CultureInfo.InvariantCulture);
    public string ActualServiceText => ActualServiceRevenue.ToString("N0", CultureInfo.InvariantCulture);
    public string ActualTotalText => ActualTotalRevenue.ToString("N0", CultureInfo.InvariantCulture);
    public string TaxableTotalText => TaxableTotalRevenue.ToString("N0", CultureInfo.InvariantCulture);
    public string VatAmountText => VatAmount.ToString("N0", CultureInfo.InvariantCulture);
    public string PitAmountText => PitAmount.ToString("N0", CultureInfo.InvariantCulture);
    public string TotalTaxText => TotalTaxAmount.ToString("N0", CultureInfo.InvariantCulture);
    public DateTime Date { get; set; }
}

