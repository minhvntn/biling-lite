using System;
using System.Windows;
using System.Windows.Threading;

namespace Client.Agent.Wpf
{
    public partial class NotificationWindow : Window
    {
        private DispatcherTimer _timer;
        private int _secondsRemaining = 120;

        public NotificationWindow(string message, string title)
        {
            InitializeComponent();
            MessageTextBlock.Text = message;
            TitleTextBlock.Text = title;
            CloseButton.Content = "\u0110\u00f3ng";
            TimerTextBlock.Text = string.Format("T\u1ef1 \u0111\u1ed9ng \u0111\u00f3ng sau {0} gi\u00e2y", _secondsRemaining);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            _secondsRemaining--;
            TimerTextBlock.Text = string.Format("T\u1ef1 \u0111\u1ed9ng \u0111\u00f3ng sau {0} gi\u00e2y", _secondsRemaining);
            
            if (_secondsRemaining <= 0)
            {
                _timer.Stop();
                this.Close();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            this.Close();
        }
    }
}
