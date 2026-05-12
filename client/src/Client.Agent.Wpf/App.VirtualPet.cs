using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    private static readonly IReadOnlyList<VirtualPetCatalogItem> VirtualPetCatalog = new[]
    {
        new VirtualPetCatalogItem
        {
            Id = "cat",
            Emoji = "🐱",
            Name = "Mèo Pixel",
            CostPoints = 6,
            Description = "Dễ nuôi, thưởng ổn định cho người mới.",
        },
        new VirtualPetCatalogItem
        {
            Id = "dog",
            Emoji = "🐶",
            Name = "Cún Turbo",
            CostPoints = 9,
            Description = "Tăng cấp nhanh, cần chơi cùng thường xuyên.",
        },
        new VirtualPetCatalogItem
        {
            Id = "dragon",
            Emoji = "🐲",
            Name = "Rồng Mini",
            CostPoints = 14,
            Description = "Khó nuôi hơn, nhưng thưởng điểm cao hơn.",
        },
    };

    private void ShowVirtualPetDialog(
        ActiveMemberSession activeSession,
        MemberLoyaltyResponse loyaltyResponse)
    {
        var store = LoadVirtualPetStore();
        var memberState = store.Members.FirstOrDefault(x =>
            string.Equals(x.MemberId, activeSession.MemberId, StringComparison.OrdinalIgnoreCase));
        if (memberState is null)
        {
            memberState = new VirtualPetMemberState
            {
                MemberId = activeSession.MemberId,
            };
            store.Members.Add(memberState);
        }

        var availablePoints = loyaltyResponse.Loyalty.AvailablePoints;
        var isBusy = false;
        var nowUtc = DateTime.UtcNow;
        if (memberState.ActivePet is not null)
        {
            ApplyVirtualPetDecay(memberState.ActivePet, nowUtc);
            SaveVirtualPetStore(store);
        }

        var dialog = new Window
        {
            Title = $"Nuôi thú ảo - {activeSession.Username}",
            Width = 620,
            Height = 640,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Nông trại thú ảo",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(titleText, 0);
        root.Children.Add(titleText);

        var statusText = new TextBlock
        {
            Text = string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 12),
        };
        Grid.SetRow(statusText, 1);
        root.Children.Add(statusText);

        var bodyScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var bodyPanel = new StackPanel();
        bodyScroll.Content = bodyPanel;
        Grid.SetRow(bodyScroll, 2);
        root.Children.Add(bodyScroll);

        var errorText = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Grid.SetRow(errorText, 3);
        root.Children.Add(errorText);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var closeButton = new Button
        {
            Content = "Đóng",
            Width = 110,
            Height = 34,
            IsCancel = true,
        };
        closeButton.Click += (_, _) => dialog.Close();
        actionPanel.Children.Add(closeButton);
        Grid.SetRow(actionPanel, 4);
        root.Children.Add(actionPanel);

        void SetBusy(bool busy)
        {
            isBusy = busy;
            closeButton.IsEnabled = !busy;
        }

        void SaveAndRefresh()
        {
            SaveVirtualPetStore(store);
            Render();
        }

        void RenderStoreCards()
        {
            var storeTitle = new TextBlock
            {
                Text = "Cửa hàng thú ảo",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 10),
            };
            bodyPanel.Children.Add(storeTitle);

            var storeHint = new TextBlock
            {
                Text = "Dùng điểm tích lũy để mua 1 thú. Nuôi tốt để thú tạo điểm thưởng ngược lại.",
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap,
            };
            bodyPanel.Children.Add(storeHint);

            foreach (var item in VirtualPetCatalog)
            {
                var card = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8),
                };

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var emoji = new TextBlock
                {
                    Text = item.Emoji,
                    FontSize = 28,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                Grid.SetColumn(emoji, 0);
                cardGrid.Children.Add(emoji);

                var infoPanel = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                };
                infoPanel.Children.Add(new TextBlock
                {
                    Text = $"{item.Name} ({item.CostPoints} điểm)",
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                });
                infoPanel.Children.Add(new TextBlock
                {
                    Text = item.Description,
                    Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                    TextWrapping = TextWrapping.Wrap,
                });
                Grid.SetColumn(infoPanel, 1);
                cardGrid.Children.Add(infoPanel);

                var buyButton = new Button
                {
                    Content = "Mua",
                    Width = 88,
                    Height = 32,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsEnabled = !isBusy && availablePoints >= item.CostPoints,
                    Background = new SolidColorBrush(Color.FromRgb(147, 197, 253)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                };
                buyButton.Click += async (_, _) =>
                {
                    if (isBusy)
                    {
                        return;
                    }

                    errorText.Text = string.Empty;
                    SetBusy(true);
                    buyButton.IsEnabled = false;
                    try
                    {
                        var (ok, error, payload) = await TryApplyVirtualPetPointsAsync(
                            activeSession.MemberId,
                            "SPEND",
                            item.CostPoints,
                            $"LOYALTY_REDEEM_PET: buy {item.Id}");
                        if (!ok || payload is null)
                        {
                            errorText.Text = string.IsNullOrWhiteSpace(error)
                                ? "Mua thú thất bại."
                                : error;
                            return;
                        }

                        availablePoints = payload.Loyalty.AvailablePoints;
                        memberState.ActivePet = CreateVirtualPetState(item, DateTime.UtcNow);
                        memberState.TotalSpentPoints += item.CostPoints;
                        SaveAndRefresh();
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                };
                Grid.SetColumn(buyButton, 2);
                cardGrid.Children.Add(buyButton);

                card.Child = cardGrid;
                bodyPanel.Children.Add(card);
            }
        }

        void RenderPetDashboard(VirtualPetState pet)
        {
            ApplyVirtualPetDecay(pet, DateTime.UtcNow);
            SaveVirtualPetStore(store);

            var petCard = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 12),
            };

            var petPanel = new StackPanel();
            petPanel.Children.Add(new TextBlock
            {
                Text = $"{pet.Emoji}  {pet.Name}",
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            petPanel.Children.Add(new TextBlock
            {
                Text = $"Lv.{pet.Level} - EXP {pet.Experience}/{GetVirtualPetExpGoal(pet.Level)}",
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 2, 0, 10),
            });

            petPanel.Children.Add(BuildStatBar("No", pet.Hunger, Color.FromRgb(59, 130, 246)));
            petPanel.Children.Add(BuildStatBar("Sach", pet.Cleanliness, Color.FromRgb(16, 185, 129)));
            petPanel.Children.Add(BuildStatBar("Vui", pet.Happiness, Color.FromRgb(234, 179, 8)));

            var careRow = new UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 12, 0, 8),
            };

            var feedButton = new Button
            {
                Content = "Cho ăn",
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = !isBusy,
            };
            feedButton.Click += (_, _) =>
            {
                ApplyVirtualPetCareAction(pet, "feed");
                SaveAndRefresh();
            };
            careRow.Children.Add(feedButton);

            var playButton = new Button
            {
                Content = "Chơi cùng",
                Margin = new Thickness(0, 0, 6, 0),
                IsEnabled = !isBusy,
            };
            playButton.Click += (_, _) =>
            {
                ApplyVirtualPetCareAction(pet, "play");
                SaveAndRefresh();
            };
            careRow.Children.Add(playButton);

            var cleanButton = new Button
            {
                Content = "Tắm rửa",
                IsEnabled = !isBusy,
            };
            cleanButton.Click += (_, _) =>
            {
                ApplyVirtualPetCareAction(pet, "clean");
                SaveAndRefresh();
            };
            careRow.Children.Add(cleanButton);

            petPanel.Children.Add(careRow);

            var rewardReady = TryGetVirtualPetRewardStatus(
                pet,
                DateTime.UtcNow,
                out var rewardPoints,
                out var rewardStatusMessage);

            petPanel.Children.Add(new TextBlock
            {
                Text = rewardStatusMessage,
                Foreground = rewardReady
                    ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
                    : new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });

            var rewardRow = new Grid();
            rewardRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rewardRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            rewardRow.Children.Add(new TextBlock
            {
                Text = $"Đã tạo tổng: {pet.TotalRewardedPoints} điểm",
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var claimButton = new Button
            {
                Content = $"Nhận +{rewardPoints} điểm",
                Width = 140,
                Height = 32,
                IsEnabled = rewardReady && !isBusy,
                Background = new SolidColorBrush(Color.FromRgb(134, 239, 172)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
            };
            claimButton.Click += async (_, _) =>
            {
                if (!TryGetVirtualPetRewardStatus(
                    pet,
                    DateTime.UtcNow,
                    out var claimPoints,
                    out var claimStatus) || claimPoints <= 0)
                {
                    errorText.Text = claimStatus;
                    return;
                }

                errorText.Text = string.Empty;
                SetBusy(true);
                claimButton.IsEnabled = false;
                try
                {
                    var (ok, error, payload) = await TryApplyVirtualPetPointsAsync(
                        activeSession.MemberId,
                        "REWARD",
                        claimPoints,
                        $"PET_REWARD: level {pet.Level} claim");
                    if (!ok || payload is null)
                    {
                        errorText.Text = string.IsNullOrWhiteSpace(error)
                            ? "Nhận điểm thất bại."
                            : error;
                        return;
                    }

                    availablePoints = payload.Loyalty.AvailablePoints;
                    pet.TotalRewardedPoints += claimPoints;
                    pet.NextRewardAtUtc = DateTime.UtcNow.AddMinutes(25).ToString("o");
                    pet.Hunger = Math.Max(0, pet.Hunger - 10);
                    pet.Cleanliness = Math.Max(0, pet.Cleanliness - 8);
                    pet.Happiness = Math.Max(0, pet.Happiness - 6);
                    pet.LastUpdatedAtUtc = DateTime.UtcNow.ToString("o");
                    SaveAndRefresh();

                    MessageBox.Show(
                        $"Thú của bạn đã tạo ra {claimPoints} điểm cho hội viên.",
                        "Nuôi thú ảo",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                finally
                {
                    SetBusy(false);
                }
            };
            Grid.SetColumn(claimButton, 1);
            rewardRow.Children.Add(claimButton);
            petPanel.Children.Add(rewardRow);

            var replaceHint = new TextBlock
            {
                Text = "Mẹo: giữ cả 3 chỉ số >= 60 và level >= 2 để mở thưởng đều.",
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            petPanel.Children.Add(replaceHint);

            petCard.Child = petPanel;
            bodyPanel.Children.Add(petCard);

            var switchPetLabel = new TextBlock
            {
                Text = "Mua thú mới (sẽ thay thú hiện tại):",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            };
            bodyPanel.Children.Add(switchPetLabel);

            foreach (var item in VirtualPetCatalog.Where(x => !string.Equals(x.Id, pet.PetTypeId, StringComparison.OrdinalIgnoreCase)))
            {
                var switchButton = new Button
                {
                    Content = $"{item.Emoji} {item.Name} - {item.CostPoints} điểm",
                    Height = 34,
                    Margin = new Thickness(0, 0, 0, 6),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = !isBusy && availablePoints >= item.CostPoints,
                };
                switchButton.Click += async (_, _) =>
                {
                    errorText.Text = string.Empty;
                    SetBusy(true);
                    try
                    {
                        var (ok, error, payload) = await TryApplyVirtualPetPointsAsync(
                            activeSession.MemberId,
                            "SPEND",
                            item.CostPoints,
                            $"LOYALTY_REDEEM_PET: switch {item.Id}");
                        if (!ok || payload is null)
                        {
                            errorText.Text = string.IsNullOrWhiteSpace(error)
                                ? "Mua thú mới thất bại."
                                : error;
                            return;
                        }

                        availablePoints = payload.Loyalty.AvailablePoints;
                        memberState.ActivePet = CreateVirtualPetState(item, DateTime.UtcNow);
                        memberState.TotalSpentPoints += item.CostPoints;
                        SaveAndRefresh();
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                };
                bodyPanel.Children.Add(switchButton);
            }
        }

        void Render()
        {
            bodyPanel.Children.Clear();
            errorText.Text = string.Empty;
            statusText.Text = $"Điểm hội viên: {availablePoints:N0} điểm";

            if (memberState.ActivePet is null)
            {
                RenderStoreCards();
                return;
            }

            RenderPetDashboard(memberState.ActivePet);
        }

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20),
        };
        timer.Tick += (_, _) =>
        {
            if (!isBusy)
            {
                Render();
            }
        };

        dialog.Closed += (_, _) =>
        {
            timer.Stop();
            SaveVirtualPetStore(store);
        };

        dialog.Content = root;
        Render();
        timer.Start();
        dialog.ShowDialog();
    }

    private async Task<(bool Ok, string? Error, MemberLoyaltyResponse? Payload)>
        TryApplyVirtualPetPointsAsync(
            string memberId,
            string action,
            int points,
            string note)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                BuildApiUrl($"/members/{memberId}/loyalty/pet-points"),
                new
                {
                    action,
                    points,
                    note,
                    createdBy = "client.virtual_pet",
                });

            if (!response.IsSuccessStatusCode)
            {
                var error = await ReadErrorMessageAsync(response);
                return (false, string.IsNullOrWhiteSpace(error) ? "Yêu cầu thất bại." : error, null);
            }

            var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltyResponse>(
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });

            if (payload is null)
            {
                return (false, "Không đọc được dữ liệu loyalty mới.", null);
            }

            return (true, null, payload);
        }
        catch (Exception ex)
        {
            return (false, $"Lỗi kết nối: {ex.Message}", null);
        }
    }

    private static Border BuildStatBar(string label, int value, Color color)
    {
        var container = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Width = 44,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);

        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = value,
            Height = 16,
            Margin = new Thickness(8, 0, 8, 0),
            Foreground = new SolidColorBrush(color),
        };
        Grid.SetColumn(progressBar, 1);
        row.Children.Add(progressBar);

        var valueBlock = new TextBlock
        {
            Text = $"{value}%",
            Width = 48,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        };
        Grid.SetColumn(valueBlock, 2);
        row.Children.Add(valueBlock);

        container.Child = row;
        return container;
    }

    private static VirtualPetState CreateVirtualPetState(VirtualPetCatalogItem item, DateTime nowUtc)
    {
        return new VirtualPetState
        {
            PetTypeId = item.Id,
            Name = item.Name,
            Emoji = item.Emoji,
            Level = 1,
            Experience = 0,
            Hunger = 82,
            Cleanliness = 80,
            Happiness = 78,
            LastUpdatedAtUtc = nowUtc.ToString("o"),
            NextRewardAtUtc = nowUtc.AddMinutes(20).ToString("o"),
            TotalRewardedPoints = 0,
        };
    }

    private static void ApplyVirtualPetCareAction(VirtualPetState pet, string action)
    {
        ApplyVirtualPetDecay(pet, DateTime.UtcNow);
        switch (action)
        {
            case "feed":
                pet.Hunger = ClampPetStat(pet.Hunger + 24);
                pet.Happiness = ClampPetStat(pet.Happiness + 4);
                pet.Cleanliness = ClampPetStat(pet.Cleanliness - 2);
                pet.Experience += 12;
                break;
            case "play":
                pet.Happiness = ClampPetStat(pet.Happiness + 24);
                pet.Hunger = ClampPetStat(pet.Hunger - 10);
                pet.Cleanliness = ClampPetStat(pet.Cleanliness - 6);
                pet.Experience += 18;
                break;
            case "clean":
                pet.Cleanliness = ClampPetStat(pet.Cleanliness + 30);
                pet.Happiness = ClampPetStat(pet.Happiness + 6);
                pet.Hunger = ClampPetStat(pet.Hunger - 4);
                pet.Experience += 10;
                break;
        }

        while (pet.Experience >= GetVirtualPetExpGoal(pet.Level))
        {
            pet.Experience -= GetVirtualPetExpGoal(pet.Level);
            pet.Level = Math.Min(20, pet.Level + 1);
            pet.Happiness = ClampPetStat(pet.Happiness + 6);
            pet.Hunger = ClampPetStat(pet.Hunger + 5);
            pet.Cleanliness = ClampPetStat(pet.Cleanliness + 5);
            if (pet.Level >= 20)
            {
                pet.Experience = 0;
                break;
            }
        }

        pet.LastUpdatedAtUtc = DateTime.UtcNow.ToString("o");
    }

    private static void ApplyVirtualPetDecay(VirtualPetState pet, DateTime nowUtc)
    {
        var lastUpdate = ParseVirtualPetUtcOrDefault(pet.LastUpdatedAtUtc, nowUtc);
        if (nowUtc <= lastUpdate)
        {
            return;
        }

        var elapsedMinutes = (int)Math.Floor((nowUtc - lastUpdate).TotalMinutes);
        if (elapsedMinutes <= 0)
        {
            return;
        }

        var decayTicks = Math.Max(1, elapsedMinutes / 2);
        pet.Hunger = ClampPetStat(pet.Hunger - decayTicks);
        pet.Cleanliness = ClampPetStat(pet.Cleanliness - decayTicks);
        pet.Happiness = ClampPetStat(pet.Happiness - Math.Max(1, decayTicks / 2));
        pet.LastUpdatedAtUtc = nowUtc.ToString("o");
    }

    private static bool TryGetVirtualPetRewardStatus(
        VirtualPetState pet,
        DateTime nowUtc,
        out int rewardPoints,
        out string statusMessage)
    {
        rewardPoints = Math.Clamp(2 + (pet.Level / 3), 2, 8);
        var minStat = Math.Min(pet.Hunger, Math.Min(pet.Cleanliness, pet.Happiness));
        if (pet.Level < 2)
        {
            statusMessage = "Thú cần đạt level 2 để bắt đầu tạo điểm.";
            return false;
        }

        if (minStat < 60)
        {
            statusMessage = "Giữ các chỉ số No/Sạch/Vui >= 60 để thú tạo điểm.";
            return false;
        }

        var nextRewardAt = ParseVirtualPetUtcOrDefault(pet.NextRewardAtUtc, nowUtc);
        if (nowUtc < nextRewardAt)
        {
            var remain = nextRewardAt - nowUtc;
            var remainText = remain.TotalMinutes >= 1
                ? $"{Math.Ceiling(remain.TotalMinutes)} phút"
                : $"{Math.Max(1, Math.Ceiling(remain.TotalSeconds))} giây";
            statusMessage = $"Thú đang tạo điểm... còn {remainText} nữa.";
            return false;
        }

        statusMessage = $"Sẵn sàng nhận thưởng: +{rewardPoints} điểm";
        return true;
    }

    private static int ClampPetStat(int value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private static int GetVirtualPetExpGoal(int level)
    {
        return Math.Clamp(90 + (level * 18), 90, 450);
    }

    private static DateTime ParseVirtualPetUtcOrDefault(string? value, DateTime fallbackUtc)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return fallbackUtc;
    }

    private static string GetVirtualPetStorePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(root, "ServerManagerBilling");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "client-virtual-pets.json");
    }

    private static VirtualPetStore LoadVirtualPetStore()
    {
        var path = GetVirtualPetStorePath();
        if (!File.Exists(path))
        {
            return new VirtualPetStore();
        }

        try
        {
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<VirtualPetStore>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            return payload ?? new VirtualPetStore();
        }
        catch
        {
            return new VirtualPetStore();
        }
    }

    private static void SaveVirtualPetStore(VirtualPetStore store)
    {
        try
        {
            var path = GetVirtualPetStorePath();
            var json = JsonSerializer.Serialize(
                store,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Ignore local cache write errors.
        }
    }
}

internal sealed class VirtualPetCatalogItem
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Emoji { get; set; } = "🐾";

    public int CostPoints { get; set; }

    public string Description { get; set; } = string.Empty;
}

internal sealed class VirtualPetStore
{
    public List<VirtualPetMemberState> Members { get; set; } = new();
}

internal sealed class VirtualPetMemberState
{
    public string MemberId { get; set; } = string.Empty;

    public int TotalSpentPoints { get; set; }

    public VirtualPetState? ActivePet { get; set; }
}

internal sealed class VirtualPetState
{
    public string PetTypeId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Emoji { get; set; } = "🐾";

    public int Level { get; set; } = 1;

    public int Experience { get; set; }

    public int Hunger { get; set; } = 80;

    public int Cleanliness { get; set; } = 80;

    public int Happiness { get; set; } = 80;

    public string LastUpdatedAtUtc { get; set; } = DateTime.UtcNow.ToString("o");

    public string NextRewardAtUtc { get; set; } = DateTime.UtcNow.AddMinutes(20).ToString("o");

    public int TotalRewardedPoints { get; set; }
}
