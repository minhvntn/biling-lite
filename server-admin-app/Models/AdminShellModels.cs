using System.Windows.Media;

namespace Server.Admin.App;

public sealed class AdminShellSettings
{
    public string AdminBaseUrl { get; set; } = "http://localhost:5173";

    public string BackendApiBaseUrl { get; set; } = "http://localhost:9000/api/v1";

    public string StartPath { get; set; } = "/pcs";

    public int MachineRefreshSeconds { get; set; } = 3;

    public double UiFontSize { get; set; } = 13;

    public double MachineTableFontSize { get; set; } = 14;

    public double MachineContextMenuItemPadding { get; set; } = 12;

    public double MachineContextMenuFontSize { get; set; } = 14;
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
    public string? LastSeenAt { get; set; }
    public ActiveSessionInfo? ActiveSession { get; set; }
    public ActiveMemberInfo? ActiveMember { get; set; }
    public ActiveGuestInfo? ActiveGuest { get; set; }
}

public sealed class ActiveMemberInfo
{
    public string MemberId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}

public sealed class ActiveGuestInfo
{
    public string DisplayName { get; set; } = string.Empty;
    public decimal PrepaidAmount { get; set; }
}

public sealed class ActiveSessionInfo
{
    public string Id { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public int ElapsedSeconds { get; set; }
    public decimal EstimatedAmount { get; set; }
}

public sealed class MachineRow
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? GroupId { get; set; }
    public decimal HourlyRate { get; set; }
    public string IpAddress { get; set; } = "-";
    public string LastSeenAtText { get; set; } = "-";
    public string StatusCode { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Gray;
    public Brush StatusIconBrush { get; set; } = Brushes.Gray;
    public string StatusIconToolTip { get; set; } = string.Empty;
    public string UserName { get; set; } = "-";
    public string StartedAtText { get; set; } = "-";
    public string UsedText { get; set; } = "-";
    public string RemainingText { get; set; } = "-";
    public string MoneyText { get; set; } = "-";
    public string DateText { get; set; } = "-";
    public string VersionText { get; set; } = "0.1.0";
    public string GroupName { get; set; } = "M\u1eb7c \u0111\u1ecbnh";
    public string? ActiveSessionId { get; set; }
    public int ActiveSessionElapsedSeconds { get; set; }
    public decimal ActiveSessionEstimatedAmount { get; set; }
    public string? ActiveMemberId { get; set; }
    public string? ActiveMemberUsername { get; set; }
    public string? ActiveMemberFullName { get; set; }
    public bool IsGuestSession { get; set; }
    public string? ActiveGuestDisplayName { get; set; }
    public decimal ActiveGuestPrepaidAmount { get; set; }
}

public sealed class MemberListResponse
{
    public List<MemberItem> Items { get; set; } = new();
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
    public string CreatedAt { get; set; } = string.Empty;
}

public sealed class MemberTransactionsResponse
{
    public MemberItem Member { get; set; } = new();
    public List<MemberTransactionItem> Items { get; set; } = new();
}

public sealed class LoyaltySettingsResponse
{
    public bool Enabled { get; set; }
    public int MinutesPerPoint { get; set; }
    public int PointsToMinutes { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class ClientRuntimeSettingsResponse
{
    public int ReadyAutoShutdownMinutes { get; set; }
    public string LockScreenBackgroundMode { get; set; } = "none";
    public string LockScreenBackgroundUrl { get; set; } = string.Empty;
    public string ServerTime { get; set; } = string.Empty;
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
    public string CreatedAtText { get; set; } = "-";
}

public sealed class MemberTransactionRow
{
    public string CreatedAtText { get; set; } = "-";
    public string TypeText { get; set; } = "-";
    public string AmountDeltaText { get; set; } = "0";
    public string PlayHoursDeltaText { get; set; } = "0";
    public string CreatedBy { get; set; } = "-";
    public string Note { get; set; } = "-";
}

public sealed class SystemEventsResponse
{
    public List<SystemEventItem> Items { get; set; } = new();
    public int Total { get; set; }
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
    public bool IsDefault { get; set; }
    public string IsDefaultText => IsDefault ? "Có" : "-";
    public int Total { get; set; }
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
    public string MachineName { get; set; } = "-";
    public string AgentId { get; set; } = "-";
    public string StatusText { get; set; } = "-";
    public string IpAddress { get; set; } = "-";
    public string LastSeenAtText { get; set; } = "-";
}

public sealed class PricingSettingsResponse
{
    public decimal DefaultRatePerHour { get; set; }
    public string? DefaultGroupId { get; set; }
    public List<PricingGroupItem> Groups { get; set; } = new();
}

public sealed class PricingGroupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal HourlyRate { get; set; }
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
    public ServiceItemDto ServiceItem { get; set; } = new();
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

public sealed class LoyaltyRankItem
{
    public string Id { get; set; } = string.Empty;
    public string RankName { get; set; } = string.Empty;
    public decimal MinTopup { get; set; }
    public decimal BonusPercent { get; set; }
    public int MinutesPerPoint { get; set; }
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
    public int MinutesPerPoint { get; set; }
}
