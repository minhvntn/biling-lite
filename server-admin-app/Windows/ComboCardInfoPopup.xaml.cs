using System;
using System.Windows;

namespace Server.Admin.App.Windows
{
    public partial class ComboCardInfoPopup : Window
    {
        public ComboCardInfoPopup(string comboName, string username, string password, DateTime expiresAt)
        {
            InitializeComponent();
            TxtComboName.Text = $"Gói: {comboName}";
            TxtUsername.Text = username;
            TxtPassword.Text = password;
            TxtExpiresAt.Text = expiresAt.ToString("dd/MM/yyyy HH:mm");
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
