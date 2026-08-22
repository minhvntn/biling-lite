using System.Windows;
using System.Windows.Input;

namespace Client.Agent.Wpf;

public partial class LuckyEnvelopeResultWindow : Window
{
    public LuckyEnvelopeResultWindow(int wonMinutes, string blessingMessage)
    {
        InitializeComponent();

        WonMinutesText.Text = $"+{wonMinutes} Phút Chơi";
        BlessingMessageText.Text = string.IsNullOrWhiteSpace(blessingMessage)
            ? "Chúc mừng năm mới! Vạn sự như ý - Tỉ sự như mơ!"
            : blessingMessage;

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
