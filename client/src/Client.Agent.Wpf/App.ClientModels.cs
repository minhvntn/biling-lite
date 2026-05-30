using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;

namespace Client.Agent.Wpf;

public sealed class BrowserVisitEntry
{
    public string Domain { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Browser { get; set; } = "unknown";

    public DateTime VisitedAtUtc { get; set; }
}

public sealed class CapturedScreenshot
{
    public string Base64 { get; set; } = string.Empty;

    public int Width { get; set; }

    public int Height { get; set; }
}

public sealed class LoginAttemptResult
{
    public LoginAttemptResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }

    public string Message { get; }
}

public sealed class MemberLoginResponse
{
    public MemberLoginItem? Member { get; set; }
}

public sealed class MemberLoginItem
{
    public string Id { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public decimal Balance { get; set; }

    public int PlaySeconds { get; set; }

    public double PlayHours { get; set; }

    public string? Rank { get; set; }
    public string MemberType { get; set; } = "REGULAR";
}

public sealed class LoyaltySettingsResponse
{
    public bool Enabled { get; set; }

    public double MinutesPerPoint { get; set; }

    public int PointsToMinutes { get; set; }

    public double WeekdayMultiplier { get; set; }

    public double WeekendMultiplier { get; set; }

    public double CurrentMultiplier { get; set; }
}

public sealed class ClientRuntimeSettingsResponse
{
    public int ReadyAutoShutdownMinutes { get; set; }
    public string LockScreenBackgroundMode { get; set; } = "none";
    public string LockScreenBackgroundUrl { get; set; } = string.Empty;
    public int LockScreenIntervalSeconds { get; set; } = 5;
    public decimal PricingStep { get; set; } = 1000m;
    public decimal MinimumCharge { get; set; } = 1000m;
    public bool AllowMemberWithdraw { get; set; } = true;
    public bool AllowMemberTopupRequest { get; set; } = true;
    public int AutoCollapseIntervalSeconds { get; set; } = 0;

    public string ServerTime { get; set; } = string.Empty;
}

public sealed class WebFilterSettingsResponse
{
    public bool Enabled { get; set; }

    public List<string> BlockedDomains { get; set; } = new();

    public string UpdatedAt { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class WebsiteLogSettingsResponse
{
    public bool Enabled { get; set; }

    public string UpdatedAt { get; set; } = string.Empty;

    public string UpdatedBy { get; set; } = string.Empty;
}

public sealed class MemberLoyaltyResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public MemberDailyCheckinStatus? DailyCheckin { get; set; }
}

public sealed class MemberLoyaltyRedeemResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public int RedeemedPoints { get; set; }

    public int GrantedMinutes { get; set; }
}

public sealed class MemberLoyaltySpinResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public int WonMinutes { get; set; }

    public string PrizeLabel { get; set; } = string.Empty;

    public int CostPoints { get; set; }

    public string? SpunAt { get; set; }
}

public sealed class MemberLoyaltyDailyCheckinResponse
{
    public MemberLoginItem Member { get; set; } = new();

    public MemberLoyaltyItem Loyalty { get; set; } = new();

    public MemberDailyCheckinStatus? DailyCheckin { get; set; }

    public int GainedPoints { get; set; }

    public int BonusPoints { get; set; }

    public int StreakDays { get; set; }

    public string? CheckedInAt { get; set; }
}

public sealed class MemberLoyaltyHorseRaceRequest
{
    public int BetPoints { get; set; }
    public int SelectedHorse { get; set; }
    public string CreatedBy { get; set; } = "client.horse_race";
}

public sealed class MemberLoyaltyHorseRaceResponse
{
    public int WinnerHorse { get; set; }
    public List<int> FinishOrder { get; set; } = new();
    public int Rank { get; set; }
    public bool IsWin { get; set; }
    public int WonPoints { get; set; }
    public MemberLoyaltyItem Loyalty { get; set; } = new();
    public string PlayedAt { get; set; } = string.Empty;
}

public sealed class MemberUsageSyncResponse
{
    public MemberLoginItem? Member { get; set; }

    public int ConsumedSeconds { get; set; }

    public int RequestedSeconds { get; set; }
}

public sealed class MemberTransferBalanceResponse
{
    public MemberLoginItem? SourceMember { get; set; }

    public MemberLoginItem? TargetMember { get; set; }

    public decimal Amount { get; set; }

    public string? TransferredAt { get; set; }
}

public sealed class MemberWithdrawRequestResponse
{
    public MemberWithdrawRequestItem? Request { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }
}

public sealed class MemberWithdrawRequestItem
{
    public string RequestId { get; set; } = string.Empty;

    public string MemberId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string? Note { get; set; }

    public string Status { get; set; } = string.Empty;

    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberTopupRequestResponse
{
    public MemberTopupRequestItem? Request { get; set; }

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

    public string Status { get; set; } = string.Empty;

    public string RequestedAt { get; set; } = string.Empty;
}

public sealed class MemberLoyaltyItem
{
    public int AvailablePoints { get; set; }

    public int EarnedPoints { get; set; }

    public int RedeemedPoints { get; set; }

    public double ProgressSeconds { get; set; }

    public double ProgressMinutes { get; set; }

    public double MinutesPerPoint { get; set; }
}

public sealed class MemberDailyCheckinStatus
{
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";

    public int PointsPerCheckin { get; set; } = 1;

    public int BonusEveryDays { get; set; } = 7;

    public int BonusPoints { get; set; } = 3;

    public string Today { get; set; } = string.Empty;

    public bool CheckedInToday { get; set; }

    public string? CheckedInAt { get; set; }

    public int CurrentStreakDays { get; set; }

    public int DaysUntilNextBonus { get; set; }

    public bool BonusReadyToday { get; set; }

    public string? LastCheckinDate { get; set; }

    public string? LastCheckinAt { get; set; }
}

public sealed class ActiveMemberSession
{
    public string MemberId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Rank { get; set; }
    public string MemberType { get; set; } = "REGULAR";
}

public sealed class ClientPcListResponse
{
    public List<ClientPcItemDto> Items { get; set; } = new();
}

public sealed class ClientPcItemDto
{
    public string Id { get; set; } = string.Empty;
    public string AgentId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ClientPcActiveSessionDto? ActiveSession { get; set; }
}

public sealed class ClientPcActiveSessionDto
{
    public string Id { get; set; } = string.Empty;
}

public sealed class ClientServiceItemsResponse
{
    public List<ClientServiceItemDto> Items { get; set; } = new();
}

public sealed class ClientServiceItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "-";
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; }
    public string? ImageDataUrl { get; set; }
}

public sealed class ClientPcServiceOrdersResponse
{
    public string PcId { get; set; } = string.Empty;
    public List<ClientPcServiceOrderItem> Items { get; set; } = new();
}

public sealed class ClientPcServiceOrderItem
{
    public string Id { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public bool IsPaid { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public ClientServiceItemDto? ServiceItem { get; set; }
}

public sealed class ClientExistingServiceSummary
{
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
}

public sealed class ClientServiceOrderSelectionRow : INotifyPropertyChanged
{
    private const int MaxAddQuantity = 99;
    private int _quantity;

    public string ServiceItemId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Category { get; init; } = "-";
    public string? ImageDataUrl { get; init; }
    public ImageSource? ServiceImageSource { get; set; }
    public bool HasImage => ServiceImageSource is not null;
    public decimal UnitPrice { get; init; }
    public int ExistingQuantity { get; init; }
    public int CancelableQuantity { get; init; }
    public decimal ExistingAmount { get; init; }
    public string UnitPriceText => UnitPrice.ToString("N0", CultureInfo.InvariantCulture);
    public string ExistingText => ExistingQuantity <= 0 ? "-" : $"{ExistingQuantity:N0} ({ExistingAmount:N0})";
    public int ServerOwnedQuantity => Math.Max(0, ExistingQuantity - Math.Max(0, CancelableQuantity));
    public string SourceText
    {
        get
        {
            var clientOwnedQuantity = Math.Max(0, CancelableQuantity);
            if (ExistingQuantity <= 0)
            {
                return "-";
            }

            if (clientOwnedQuantity > 0 && ServerOwnedQuantity > 0)
            {
                return "Hon hop";
            }

            if (ServerOwnedQuantity > 0)
            {
                return "Server";
            }

            return "May tram";
        }
    }
    public bool CanDecrease => Quantity > -Math.Max(0, CancelableQuantity);

    public int Quantity
    {
        get => _quantity;
        set
        {
            var minCancelableQuantity = -Math.Max(0, CancelableQuantity);
            var clamped = Math.Clamp(value, minCancelableQuantity, MaxAddQuantity);
            if (_quantity == clamped)
            {
                return;
            }

            _quantity = clamped;
            NotifyQuantityChanged();
        }
    }

    public decimal LineTotal => UnitPrice * Quantity;
    public string LineTotalText => LineTotal.ToString("N0", CultureInfo.InvariantCulture);

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ClientServiceOrderSelectionRow FromServiceItem(
        ClientServiceItemDto item,
        ClientExistingServiceSummary? existingSummary = null,
        ClientExistingServiceSummary? clientOwnedSummary = null)
    {
        var existingQuantity = existingSummary?.Quantity ?? 0;
        var existingAmount = existingSummary?.Amount ?? 0;
        var cancelableQuantity = clientOwnedSummary?.Quantity ?? 0;
        return new ClientServiceOrderSelectionRow
        {
            ServiceItemId = item.Id,
            ServiceName = item.Name,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "-" : item.Category,
            ImageDataUrl = item.ImageDataUrl,
            UnitPrice = item.UnitPrice,
            ExistingQuantity = existingQuantity,
            CancelableQuantity = cancelableQuantity,
            ExistingAmount = existingAmount,
            Quantity = 0,
        };
    }

    public void IncreaseQuantity()
    {
        Quantity = Math.Min(MaxAddQuantity, Quantity + 1);
    }

    public void DecreaseQuantity()
    {
        var minCancelableQuantity = -Math.Max(0, CancelableQuantity);
        Quantity = Math.Max(minCancelableQuantity, Quantity - 1);
    }

    private void NotifyQuantityChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Quantity)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotal)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDecrease)));
    }
}
