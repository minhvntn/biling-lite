using System.Windows;

namespace Server.Admin.App;

public partial class ReplySupportDialog : Window
{
    public string ReplyMessage => ReplyTextBox.Text;

    public ReplySupportDialog(string pcName, string originalMessage)
    {
        InitializeComponent();
        PcNameTextBlock.Text = $"Máy: {pcName}";
        OriginalMessageTextBlock.Text = $"Tin nhắn cũ:\n{originalMessage}";
        ReplyTextBox.Focus();
    }

    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ReplyTextBox.Text))
        {
            MessageBox.Show(this, "Vui lòng nhập nội dung phản hồi.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
