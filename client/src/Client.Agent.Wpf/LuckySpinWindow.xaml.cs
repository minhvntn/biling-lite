using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Client.Agent.Wpf;

public partial class LuckySpinWindow : Window
{
    private readonly App _app;
    private readonly HttpClient _httpClient;
    private readonly ActiveMemberSession _activeSession;
    private readonly LoyaltySettingsResponse _settings;
    private MemberLoyaltyResponse _loyaltyResponse;

    private RotateTransform _wheelRotation = new RotateTransform();

    public LuckySpinWindow(
        App app,
        HttpClient httpClient,
        ActiveMemberSession activeSession,
        LoyaltySettingsResponse settings,
        MemberLoyaltyResponse loyaltyResponse)
    {
        InitializeComponent();

        _app = app;
        _httpClient = httpClient;
        _activeSession = activeSession;
        _settings = settings;
        _loyaltyResponse = loyaltyResponse;

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        BuildWheel();
        RefreshUi();
    }

    private void RefreshUi()
    {
        AvailablePointsText.Text = _loyaltyResponse.Loyalty.AvailablePoints.ToString("N0");
        SpinButton.IsEnabled = _loyaltyResponse.Loyalty.AvailablePoints >= 5;
    }

    private void BuildWheel()
    {
        WheelHost.Children.Clear();

        double wheelSize = 260;
        
        var wheelAndPointer = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var wheelContainer = new Grid
        {
            Width = wheelSize, Height = wheelSize,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _wheelRotation,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };

        var wheelItems = new[]
        {
            new { Label = "0p", Minutes = 0, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "1p", Minutes = 1, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "2p", Minutes = 2, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "4p", Minutes = 4, Color = new SolidColorBrush(Color.FromRgb(249, 115, 22)) },
            new { Label = "6p", Minutes = 6, Color = new SolidColorBrush(Color.FromRgb(37, 99, 235)) },
            new { Label = "8p", Minutes = 8, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) },
            new { Label = "10p", Minutes = 10, Color = new SolidColorBrush(Color.FromRgb(100, 116, 139)) },
            new { Label = "15p", Minutes = 15, Color = new SolidColorBrush(Color.FromRgb(234, 179, 8)) },
            new { Label = "20p", Minutes = 20, Color = new SolidColorBrush(Color.FromRgb(22, 163, 74)) },
            new { Label = "30p", Minutes = 30, Color = new SolidColorBrush(Color.FromRgb(220, 38, 38)) }
        };

        double radius = wheelSize / 2.0;
        double angleStep = 360.0 / wheelItems.Length;

        // Outer Rim
        var outerRim = new System.Windows.Shapes.Ellipse
        {
            Width = wheelSize + 12, Height = wheelSize + 12,
            Stroke = new LinearGradientBrush(Colors.Gold, Colors.DarkGoldenrod, 45),
            StrokeThickness = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        
        for (int i = 0; i < wheelItems.Length; i++)
        {
            var item = wheelItems[i];
            double startAngle = i * angleStep;
            double endAngle = (i + 1) * angleStep;
            
            // Draw Slice
            double radStart = (startAngle - 90) * Math.PI / 180.0;
            double radEnd = (endAngle - 90) * Math.PI / 180.0;

            Point p1 = new Point(radius, radius);
            Point p2 = new Point(radius + radius * Math.Cos(radStart), radius + radius * Math.Sin(radStart));
            Point p3 = new Point(radius + radius * Math.Cos(radEnd), radius + radius * Math.Sin(radEnd));

            var path = new System.Windows.Shapes.Path
            {
                Fill = item.Color,
                Stroke = Brushes.White,
                StrokeThickness = 1.2,
                Data = new PathGeometry(new[] { 
                    new PathFigure(p1, new PathSegment[] {
                        new LineSegment(p2, true),
                        new ArcSegment(p3, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true),
                        new LineSegment(p1, true)
                    }, true) 
                })
            };
            wheelContainer.Children.Add(path);

            // Add Label
            var label = new TextBlock
            {
                Text = item.Label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            
            var labelGroup = new TransformGroup();
            labelGroup.Children.Add(new TranslateTransform(0, -radius * 0.72));
            labelGroup.Children.Add(new RotateTransform(startAngle + angleStep / 2.0));
            label.RenderTransform = labelGroup;
            
            wheelContainer.Children.Add(label);
        }

        // Center hub
        var hub = new System.Windows.Shapes.Ellipse
        {
            Width = 56, Height = 56,
            Fill = Brushes.White,
            Stroke = Brushes.Gold,
            StrokeThickness = 4,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hub);

        var hubText = new TextBlock
        {
            Text = "QUAY",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 58, 138)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        wheelContainer.Children.Add(hubText);

        wheelAndPointer.Children.Add(outerRim);
        wheelAndPointer.Children.Add(wheelContainer);

        // Pointer (Needle)
        var pointerLayer = new Grid
        {
            Width = wheelSize + 12,
            Height = wheelSize + 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        var pointer = new System.Windows.Shapes.Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(139, 92, 246)), // Purple to match theme
            Stroke = Brushes.WhiteSmoke,
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -18, 0, 0),
            Points = new PointCollection
            {
                new Point(0, 0),
                new Point(24, 0),
                new Point(12, 32),
            },
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.4
            }
        };

        var pointerCap = new System.Windows.Shapes.Ellipse
        {
            Width = 14,
            Height = 14,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(139, 92, 246)),
            StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -5, 0, 0),
        };

        pointerLayer.Children.Add(pointer);
        pointerLayer.Children.Add(pointerCap);
        wheelAndPointer.Children.Add(pointerLayer);

        WheelHost.Children.Add(wheelAndPointer);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void SpinButton_Click(object sender, RoutedEventArgs e)
    {
        SpinButton.IsEnabled = false;
        CloseActionButton.IsEnabled = false;
        StatusText.Text = "Đang quay...";
        StatusText.Foreground = Brushes.DimGray;

        // Start fake fast spin while waiting for API
        var fastSpinAnimation = new DoubleAnimation
        {
            From = _wheelRotation.Angle,
            To = _wheelRotation.Angle + 3600, // 10 rotations
            Duration = TimeSpan.FromSeconds(10),
            RepeatBehavior = RepeatBehavior.Forever
        };
        _wheelRotation.BeginAnimation(RotateTransform.AngleProperty, fastSpinAnimation);

        try
        {
            var startTime = DateTime.Now;
            using var response = await _httpClient.PostAsJsonAsync(
                _app.BuildApiUrl($"/members/{_activeSession.MemberId}/loyalty/spin"),
                new { createdBy = "client.loyalty.spin" });

            // Ensure at least 1.5s spin for effect
            var elapsed = DateTime.Now - startTime;
            if (elapsed < TimeSpan.FromSeconds(1.5)) await Task.Delay(TimeSpan.FromSeconds(1.5) - elapsed);

            if (!response.IsSuccessStatusCode)
            {
                _wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                var message = await _app.ReadErrorMessageAsync(response);
                StatusText.Text = string.IsNullOrWhiteSpace(message)
                    ? $"Lỗi quay ({(int)response.StatusCode})"
                    : message;
                StatusText.Foreground = Brushes.Firebrick;
            }
            else
            {
                var payload = await response.Content.ReadFromJsonAsync<MemberLoyaltySpinResponse>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload == null || payload.Loyalty == null)
                {
                    _wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    StatusText.Text = "Dữ liệu trả về không hợp lệ.";
                    StatusText.Foreground = Brushes.Firebrick;
                    return;
                }

                _loyaltyResponse.Loyalty = payload.Loyalty;
                RefreshUi();

                // Compute exact angle for the result
                var wheelItems = new[] { 0, 1, 2, 4, 6, 8, 10, 15, 20, 30 };
                double angleStep = 360.0 / wheelItems.Length;

                var possibleIndices = wheelItems
                    .Select((val, idx) => new { val, idx })
                    .Where(x => x.val == payload.WonMinutes)
                    .Select(x => x.idx)
                    .ToList();

                var targetIndex = possibleIndices.Count > 0
                    ? possibleIndices[new Random().Next(possibleIndices.Count)]
                    : 1;

                var targetAngleOffset = -((targetIndex + 0.5) * angleStep);
                var currentAngle = _wheelRotation.Angle % 360;
                var finalAngle = _wheelRotation.Angle + (360 * 6) - currentAngle + targetAngleOffset;

                var stopAnimation = new DoubleAnimation
                {
                    From = _wheelRotation.Angle,
                    To = finalAngle,
                    Duration = TimeSpan.FromSeconds(4.8),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var tcs = new TaskCompletionSource<bool>();
                stopAnimation.Completed += (_, _) => tcs.SetResult(true);
                _wheelRotation.BeginAnimation(RotateTransform.AngleProperty, stopAnimation);
                await tcs.Task;

                StatusText.Text = payload.WonMinutes > 0
                    ? $"CHÚC MỪNG! Bạn trúng {payload.WonMinutes} phút chơi!"
                    : "Chúc bạn may mắn lần sau!";
                StatusText.Foreground = payload.WonMinutes > 0 ? Brushes.DarkGreen : Brushes.OrangeRed;

                if (payload.Member is not null)
                {
                    _app.SynchronizeMemberBillingFromServerProxy(payload.Member);
                    _app.UpdateLastCommandProxy($"QUAY THƯỞNG: +{payload.WonMinutes}m @ {DateTime.Now:HH:mm:ss}");
                }

                if (payload.Loyalty != null)
                {
                    _app.SynchronizeLoyaltyFromServerProxy(payload.Loyalty);
                }
            }
        }
        catch (Exception ex)
        {
            _wheelRotation.BeginAnimation(RotateTransform.AngleProperty, null);
            StatusText.Text = "Lỗi: " + ex.Message;
            StatusText.Foreground = Brushes.Firebrick;
        }
        finally
        {
            CloseActionButton.IsEnabled = true;
            RefreshUi();
        }
    }
}
