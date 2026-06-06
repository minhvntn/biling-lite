using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Client.Agent.Wpf;

public partial class App : Application
{
    private void ShowTransferBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        var dialog = new Window
        {
            Title = $"Chuyển tiền - {activeSession.Username}",
            Width = 460,
            Height = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 11; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Chuyển tiền cho hội viên khác",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản gửi: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var targetLabel = new TextBlock
        {
            Text = "Tài khoản nhận:",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(targetLabel, 3);
        root.Children.Add(targetLabel);

        var targetUsernameBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(targetUsernameBox, 4);
        root.Children.Add(targetUsernameBox);

        var amountLabel = new TextBlock
        {
            Text = "Số tiền chuyển (VND):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(amountLabel, 5);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 6);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 7);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 8);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "Tối thiểu 1.000 VND cho mỗi lần chuyển.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 9);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 10);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var transferButton = new Button
        {
            Content = "Chuyển tiền",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        transferButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            var targetUsername = targetUsernameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(targetUsername))
            {
                errorTextBlock.Text = "Vui lòng nhập tài khoản nhận.";
                return;
            }

            if (string.Equals(targetUsername, sourceMember.Username, StringComparison.OrdinalIgnoreCase))
            {
                errorTextBlock.Text = "Không thể chuyển tiền cho chính mình.";
                return;
            }

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền chuyển không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền chuyển tối thiểu là 1.000 VND.";
                return;
            }

            transferButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/transfer"),
                    new
                    {
                        targetUsername,
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.transfer",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Chuyển tiền thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTransferBalanceResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var nextBalance = payload?.SourceMember?.Balance ?? Math.Max(0, sourceMember.Balance - amount);
                _mainWindow?.SetLastCommand(
                    $"CHUYỂN TIỀN {amount:N0} -> {targetUsername} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Chuyển tiền thành công.\n\nĐã chuyển: {amount:N0} VND\nĐến: {targetUsername}\nSố dư còn lại: {nextBalance:N0} VND",
                    "Chuyển tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                transferButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(transferButton);
        Grid.SetRow(actionPanel, 12);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            targetUsernameBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowWithdrawBalanceDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        if (!_adminOnline)
        {
            MessageBox.Show("Hiện tại không có thu ngân (admin) online, bạn không thể tạo yêu cầu rút tiền lúc này. Vui lòng thử lại sau.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Window
        {
            Title = $"Rút tiền - {activeSession.Username}",
            Width = 430,
            Height = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.SingleBorderWindow,
        };

        var root = new Grid
        {
            Margin = new Thickness(16),
        };
        for (var i = 0; i < 9; i++)
        {
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto,
            });
        }
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });
        root.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var titleBlock = new TextBlock
        {
            Text = "Rút tiền từ tài khoản hội viên",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        Grid.SetRow(titleBlock, 0);
        root.Children.Add(titleBlock);

        var sourceBlock = new TextBlock
        {
            Text = $"Tài khoản: {sourceMember.Username}",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(sourceBlock, 1);
        root.Children.Add(sourceBlock);

        var balanceBlock = new TextBlock
        {
            Text = $"Số dư hiện tại: {sourceMember.Balance:N0} VND",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetRow(balanceBlock, 2);
        root.Children.Add(balanceBlock);

        var amountLabel = new TextBlock
        {
            Text = "Số tiền rút (VND):",
            Margin = new Thickness(0, 0, 0, 4),
        };
        Grid.SetRow(amountLabel, 3);
        root.Children.Add(amountLabel);

        var amountBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
            Text = "1000",
        };
        Grid.SetRow(amountBox, 4);
        root.Children.Add(amountBox);

        var noteLabel = new TextBlock
        {
            Text = "Ghi chú (không bắt buộc):",
            Margin = new Thickness(0, 10, 0, 4),
        };
        Grid.SetRow(noteLabel, 5);
        root.Children.Add(noteLabel);

        var noteBox = new TextBox
        {
            Height = 32,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(noteBox, 6);
        root.Children.Add(noteBox);

        var hintBlock = new TextBlock
        {
            Text = "Tối thiểu 1.000 VND cho mỗi lần rút.",
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(hintBlock, 7);
        root.Children.Add(hintBlock);

        var errorTextBlock = new TextBlock
        {
            Foreground = Brushes.Firebrick,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        };
        Grid.SetRow(errorTextBlock, 8);
        root.Children.Add(errorTextBlock);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var cancelButton = new Button
        {
            Content = "Hủy",
            Width = 90,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        cancelButton.Click += (_, _) => dialog.Close();

        var withdrawButton = new Button
        {
            Content = "Rút tiền",
            Width = 100,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(121, 201, 89)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 138, 46)),
        };

        withdrawButton.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;

            if (!TryParsePositiveMoney(amountBox.Text.Trim(), out var amount))
            {
                errorTextBlock.Text = "Số tiền rút không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền rút tối thiểu là 1.000 VND.";
                return;
            }

            if (amount > sourceMember.Balance)
            {
                errorTextBlock.Text = "Số dư hiện tại không đủ để rút.";
                return;
            }

            withdrawButton.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/withdraw"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.withdraw",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Rút tiền thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberWithdrawRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU RUT TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Đã gửi yêu cầu rút tiền.\n\nSố tiền: {amount:N0} VND\nMã yêu cầu: {requestId}\nBên app server sẽ hiện popup có nút Chấp nhận/Hủy.",
                    "Rút tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                withdrawButton.IsEnabled = true;
            }
        };

        actionPanel.Children.Add(cancelButton);
        actionPanel.Children.Add(withdrawButton);
        Grid.SetRow(actionPanel, 10);
        root.Children.Add(actionPanel);

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
        };

        dialog.ShowDialog();
    }

    private void ShowTopupRequestDialog(
        ActiveMemberSession activeSession,
        MemberLoginItem sourceMember)
    {
        if (!_adminOnline)
        {
            MessageBox.Show("Hiện tại không có thu ngân (admin) online, bạn không thể tạo yêu cầu nạp tiền lúc này. Vui lòng thử lại sau.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mainPurple = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6")); // Purple-500
        var mainPurpleHover = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED")); // Purple-600
        var lightPurple = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F3FF")); // Purple-50
        var borderPurple = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DDD6FE")); // Purple-200
        var textDark = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")); // Slate-800
        var textGray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")); // Slate-500
        var borderGray = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")); // Slate-200

        var dialog = new Window
        {
            Title = $"Nạp tiền - {activeSession.Username}",
            Width = 850,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _mainWindow,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent
        };

        var rootBorder = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            BorderBrush = borderGray,
            BorderThickness = new Thickness(1)
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) }); // Header
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // Header
        var headerBorder = new Border
        {
            Background = lightPurple,
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            BorderBrush = borderGray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
        headerTitlePanel.Children.Add(new TextBlock { Text = "💳", FontSize = 20, Foreground = mainPurple, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        headerTitlePanel.Children.Add(new TextBlock { Text = $"Nạp tiền - {activeSession.Username}", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = textDark, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(headerTitlePanel, 0);
        headerGrid.Children.Add(headerTitlePanel);

        var closeBtn = new Button
        {
            Content = "✕",
            FontSize = 16,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = textGray,
            Margin = new Thickness(0, 0, 15, 0),
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (_, _) => dialog.Close();
        Grid.SetColumn(closeBtn, 1);
        headerGrid.Children.Add(closeBtn);
        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        rootGrid.Children.Add(headerBorder);

        // Main Content
        var contentGrid = new Grid { Margin = new Thickness(24) };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) }); // Spacer
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        Grid.SetRow(contentGrid, 1);
        rootGrid.Children.Add(contentGrid);

        // --- Left Column ---
        var leftPanel = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(leftPanel, 0);
        contentGrid.Children.Add(leftPanel);

        leftPanel.Children.Add(new TextBlock { Text = "Gửi yêu cầu nạp tiền hội viên", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = textDark, Margin = new Thickness(0, 0, 0, 20) });

        // Info Block
        var infoBorder = new Border { CornerRadius = new CornerRadius(8), BorderBrush = borderPurple, BorderThickness = new Thickness(1), Background = Brushes.White, Margin = new Thickness(0, 0, 0, 20) };
        var infoGrid = new Grid { Margin = new Thickness(16) };
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) }); // spacer
        infoGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        
        // Row 0
        var accGrid = new Grid();
        accGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        accGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var accLabel = new StackPanel { Orientation = Orientation.Horizontal };
        accLabel.Children.Add(new TextBlock { Text = "👤", Foreground = mainPurple, Margin = new Thickness(0, 0, 8, 0) });
        accLabel.Children.Add(new TextBlock { Text = "Tài khoản:", Foreground = textGray });
        Grid.SetColumn(accLabel, 0); accGrid.Children.Add(accLabel);
        var accValue = new TextBlock { Text = sourceMember.Username, FontWeight = FontWeights.Bold, Foreground = mainPurple };
        Grid.SetColumn(accValue, 1); accGrid.Children.Add(accValue);
        Grid.SetRow(accGrid, 0); infoGrid.Children.Add(accGrid);

        // Row 2
        var balGrid = new Grid();
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var balLabel = new StackPanel { Orientation = Orientation.Horizontal };
        balLabel.Children.Add(new TextBlock { Text = "💳", Foreground = mainPurple, Margin = new Thickness(0, 0, 8, 0) });
        balLabel.Children.Add(new TextBlock { Text = "Số dư hiện tại:", Foreground = textGray });
        Grid.SetColumn(balLabel, 0); balGrid.Children.Add(balLabel);
        var balValue = new TextBlock { Text = $"{sourceMember.Balance:N0} VND", FontWeight = FontWeights.Bold, Foreground = mainPurple };
        Grid.SetColumn(balValue, 1); balGrid.Children.Add(balValue);
        Grid.SetRow(balGrid, 2); infoGrid.Children.Add(balGrid);

        infoBorder.Child = infoGrid;
        leftPanel.Children.Add(infoBorder);

        // Amount Input
        leftPanel.Children.Add(new TextBlock { Text = "Số tiền cần nạp (VND)", Foreground = textDark, Margin = new Thickness(0, 0, 0, 8) });
        var amountBorder = new Border { CornerRadius = new CornerRadius(8), BorderBrush = mainPurple, BorderThickness = new Thickness(1), Background = Brushes.White, Height = 45, Margin = new Thickness(0, 0, 0, 20) };
        var amountGrid = new Grid();
        amountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        amountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        amountGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        var amountIcon = new TextBlock { Text = "💰", Foreground = mainPurple, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(amountIcon, 0); amountGrid.Children.Add(amountIcon);

        var amountBox = new TextBox { Text = "20,000", BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = textDark };
        Grid.SetColumn(amountBox, 1); amountGrid.Children.Add(amountBox);

        var vndText = new TextBlock { Text = "VND", Foreground = textGray, Margin = new Thickness(8, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(vndText, 2); amountGrid.Children.Add(vndText);
        amountBorder.Child = amountGrid;
        leftPanel.Children.Add(amountBorder);

        var promoBonusText = new TextBlock { Text = "", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#16A34A")), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, -12, 0, 16), Visibility = Visibility.Collapsed };
        leftPanel.Children.Add(promoBonusText);

        // Note Input
        leftPanel.Children.Add(new TextBlock { Text = "Ghi chú (không bắt buộc)", Foreground = textDark, Margin = new Thickness(0, 0, 0, 8) });
        var noteBorder = new Border { CornerRadius = new CornerRadius(8), BorderBrush = borderGray, BorderThickness = new Thickness(1), Background = Brushes.White, Height = 45, Margin = new Thickness(0, 0, 0, 20) };
        var noteGrid = new Grid();
        noteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        noteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var noteIcon = new TextBlock { Text = "📝", Foreground = textGray, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(noteIcon, 0); noteGrid.Children.Add(noteIcon);
        var noteBox = new TextBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 14, Foreground = textDark };
        Grid.SetColumn(noteBox, 1); noteGrid.Children.Add(noteBox);
        noteBorder.Child = noteGrid;
        leftPanel.Children.Add(noteBorder);

        // Hint
        var hintBorder = new Border { CornerRadius = new CornerRadius(8), Background = lightPurple, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 20) };
        var hintStack = new StackPanel { Orientation = Orientation.Horizontal };
        hintStack.Children.Add(new TextBlock { Text = "ℹ️", Foreground = mainPurple, Margin = new Thickness(0, 0, 8, 0) });
        hintStack.Children.Add(new TextBlock { Text = "Tối thiểu 1.000 VND cho mỗi yêu cầu nạp tiền.", Foreground = textGray });
        hintBorder.Child = hintStack;
        leftPanel.Children.Add(hintBorder);

        var errorTextBlock = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
        leftPanel.Children.Add(errorTextBlock);

        // Buttons
        var btnGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var cancelBtn = new Button { Content = "✕ Hủy", Height = 45, Background = Brushes.White, BorderBrush = borderGray, BorderThickness = new Thickness(1), Foreground = textDark, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        cancelBtn.Click += (_, _) => dialog.Close();
        cancelBtn.Template = CreateRoundedButtonTemplate(8);
        Grid.SetColumn(cancelBtn, 0); btnGrid.Children.Add(cancelBtn);

        var requestBtn = new Button { Content = "🚀 Gửi yêu cầu", Height = 45, Background = mainPurple, Foreground = Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand, BorderThickness = new Thickness(0) };
        requestBtn.Template = CreateRoundedButtonTemplate(8);
        Grid.SetColumn(requestBtn, 2); btnGrid.Children.Add(requestBtn);
        leftPanel.Children.Add(btnGrid);


        // --- Right Column (QR) ---
        var rightBorder = new Border { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAFAFF")), BorderBrush = lightPurple, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(24) };
        Grid.SetColumn(rightBorder, 2); contentGrid.Children.Add(rightBorder);

        var qrPanel = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        rightBorder.Child = qrPanel;

        var qrTitleStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        qrTitleStack.Children.Add(new TextBlock { Text = "📱", Foreground = mainPurple, Margin = new Thickness(0, 0, 8, 0) });
        qrTitleStack.Children.Add(new TextBlock { Text = "Quét mã để nạp tiền", Foreground = mainPurple, FontWeight = FontWeights.Bold, FontSize = 16 });
        qrPanel.Children.Add(qrTitleStack);

        var qrImageBorder = new Border { Background = Brushes.White, Padding = new Thickness(10), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 16) };
        var qrImage = new Image { Width = 200, Height = 200, Stretch = Stretch.Uniform };
        qrImageBorder.Child = qrImage;
        qrPanel.Children.Add(qrImageBorder);

        qrPanel.Children.Add(new TextBlock { Text = _vietQrAccountName, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = textDark, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
        
        var bankStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 20) };
        bankStack.Children.Add(new TextBlock { Text = $"{_vietQrBankId} - {_vietQrAccountNo}", FontSize = 14, Foreground = textGray, Margin = new Thickness(0, 0, 8, 0) });
        var copyIcon = new TextBlock { Text = "📋", Foreground = mainPurple, Cursor = Cursors.Hand };
        copyIcon.MouseLeftButtonDown += (_, _) => { Clipboard.SetText(_vietQrAccountNo); MessageBox.Show("Đã copy số tài khoản!"); };
        bankStack.Children.Add(copyIcon);
        qrPanel.Children.Add(bankStack);

        var highlightBorder = new Border { Background = lightPurple, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 24) };
        var highlightGrid = new Grid();
        highlightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        highlightGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var hlIcon = new TextBlock { Text = "💳", FontSize = 24, Foreground = mainPurple, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(hlIcon, 0); highlightGrid.Children.Add(hlIcon);

        var hlStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        hlStack.Children.Add(new TextBlock { Text = "Số tiền:", Foreground = mainPurple, FontSize = 12 });
        var qrAmountBlock = new TextBlock { Text = "20,000 VND", Foreground = mainPurple, FontSize = 18, FontWeight = FontWeights.Bold };
        hlStack.Children.Add(qrAmountBlock);
        Grid.SetColumn(hlStack, 1); highlightGrid.Children.Add(hlStack);
        highlightBorder.Child = highlightGrid;
        qrPanel.Children.Add(highlightBorder);

        var footerStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        footerStack.Children.Add(new TextBlock { Text = "🛡️", Foreground = new SolidColorBrush(Colors.Green), Margin = new Thickness(0, 0, 8, 0) });
        footerStack.Children.Add(new TextBlock { Text = "Mã QR sẽ tự động cập nhật số tiền\ntheo yêu cầu bạn nhập bên trái.", Foreground = textGray, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        qrPanel.Children.Add(footerStack);

        rootBorder.Child = rootGrid;
        dialog.Content = rootBorder;

        decimal CalculateBonus(decimal currentAmount)
        {
            if (!_topupPromoEnabled || string.IsNullOrWhiteSpace(_topupPromoTiers)) return 0m;
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(_topupPromoTiers);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return 0m;
                decimal bestMin = 0m;
                decimal bestRate = 0m;
                foreach (var t in doc.RootElement.EnumerateArray()) {
                    decimal m = 0m;
                    if (t.TryGetProperty("minAmount", out var p1) || t.TryGetProperty("MinAmount", out p1)) {
                        if (p1.ValueKind == System.Text.Json.JsonValueKind.Number) m = p1.GetDecimal();
                        else decimal.TryParse(p1.ToString(), out m);
                    }
                    decimal b = 0m;
                    if (t.TryGetProperty("bonusRate", out var p2) || t.TryGetProperty("BonusRate", out p2)) {
                        if (p2.ValueKind == System.Text.Json.JsonValueKind.Number) b = p2.GetDecimal();
                        else decimal.TryParse(p2.ToString(), out b);
                    }
                    if (m > 0 && b > 0 && currentAmount >= m && m >= bestMin) {
                        bestMin = m;
                        bestRate = b;
                    }
                }
                return currentAmount * (bestRate / 100m);
            } catch { return 0m; }
        }

        // --- Logic ---
        Action updateQrImage = () =>
        {
            var cleanText = amountBox.Text.Replace(",", "").Replace(".", "").Trim();
            if (TryParsePositiveMoney(cleanText, out var amount) && amount >= 1000)
            {
                var memo = $"NAP {sourceMember.Username}";
                var url = $"https://img.vietqr.io/image/{_vietQrBankId}-{_vietQrAccountNo}-qr_only.png?amount={(int)amount}&addInfo={Uri.EscapeDataString(memo)}&accountName={Uri.EscapeDataString(_vietQrAccountName)}";
                
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                qrImage.Source = bitmap;

                qrAmountBlock.Text = $"{amount:N0} VND";

                var bonus = CalculateBonus(amount);
                if (bonus > 0) {
                    promoBonusText.Text = $"+ Khuyến mãi: {bonus:N0} VND";
                    promoBonusText.Visibility = Visibility.Visible;
                } else {
                    promoBonusText.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                qrImage.Source = null;
                qrAmountBlock.Text = "0 VND";
                promoBonusText.Visibility = Visibility.Collapsed;
            }
        };

        amountBox.TextChanged += (_, _) => {
            // Auto format with commas
            var currentText = amountBox.Text.Replace(",", "").Replace(".", "");
            if (decimal.TryParse(currentText, out var val)) {
                var caret = amountBox.SelectionStart;
                var formatted = val.ToString("N0", CultureInfo.InvariantCulture);
                if (amountBox.Text != formatted) {
                    amountBox.Text = formatted;
                    amountBox.SelectionStart = formatted.Length;
                }
            }
            updateQrImage();
        };

        requestBtn.Click += async (_, _) =>
        {
            errorTextBlock.Text = string.Empty;
            var cleanText = amountBox.Text.Replace(",", "").Replace(".", "").Trim();
            if (!TryParsePositiveMoney(cleanText, out var amount))
            {
                errorTextBlock.Text = "Số tiền nạp không hợp lệ.";
                return;
            }

            if (amount < 1000)
            {
                errorTextBlock.Text = "Số tiền nạp tối thiểu là 1.000 VND.";
                return;
            }

            requestBtn.IsEnabled = false;
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    BuildApiUrl($"/members/{activeSession.MemberId}/topup-request"),
                    new
                    {
                        amount = Convert.ToDouble(amount, CultureInfo.InvariantCulture),
                        note = string.IsNullOrWhiteSpace(noteBox.Text) ? null : noteBox.Text.Trim(),
                        createdBy = "client.member.topup.request",
                        agentId = _settings.AgentId,
                    });

                if (!response.IsSuccessStatusCode)
                {
                    var message = await ReadErrorMessageAsync(response);
                    errorTextBlock.Text = string.IsNullOrWhiteSpace(message)
                        ? $"Gửi yêu cầu thất bại ({(int)response.StatusCode})"
                        : message;
                    return;
                }

                var payload = await response.Content.ReadFromJsonAsync<MemberTopupRequestResponse>(
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    });

                var requestId = payload?.Request?.RequestId ?? "-";

                _mainWindow?.SetLastCommand(
                    $"GUI YEU CAU NAP TIEN {amount:N0} @ {DateTime.Now:HH:mm:ss}");

                MessageBox.Show(
                    $"Đã gửi yêu cầu nạp tiền.\n\nSố tiền: {amount:N0} VND\nMã yêu cầu: {requestId}\nBên app server sẽ hiện popup có nút Chấp nhận/Hủy.",
                    "Nạp tiền hội viên",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                dialog.DialogResult = true;
                dialog.Close();
            }
            finally
            {
                requestBtn.IsEnabled = true;
            }
        };

        dialog.Loaded += (_, _) =>
        {
            amountBox.Focus();
            amountBox.SelectAll();
            updateQrImage();
        };

        headerBorder.MouseLeftButtonDown += (s, e) => { dialog.DragMove(); };

        dialog.ShowDialog();
    }

    private ControlTemplate CreateRoundedButtonTemplate(int cornerRadius)
    {
        var template = new ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        factory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        factory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);

        template.VisualTree = factory;
        return template;
    }

    private static bool TryParsePositiveMoney(string value, out decimal amount)
    {
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount) && amount > 0)
        {
            return true;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0)
        {
            return true;
        }

        amount = 0;
        return false;
    }

}


