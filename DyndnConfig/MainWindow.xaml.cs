using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DyndnConfig
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel Model
        {
            get { return DataContext as MainViewModel; }
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void Username_TextChanged(object sender, TextChangedEventArgs e)
        {
            Model.Modified = true;
        }

        private void Password_PasswordChanged(object sender, RoutedEventArgs e)
        {
            const string www = @"http://www.twnic.net.tw/List/List_Regi.htm";
            string password;
            char[] buf;
            int len, salt;

            password = (sender as PasswordBox).Password;
            buf = new char[password.Length * 2 + 6];
            len = Math.Min(password.Length + 2, www.Length);
            salt = DateTime.Now.Millisecond % 90 + 10;

            buf[0] = buf[2] = (char)(salt / 10 + 0x30);
            buf[1] = buf[3] = (char)(salt % 10 + 0x30);

            for (int i = 0; i < password.Length; i++)
            {
                buf[i + 4] = password[i];
            }

            for (int i = 0; i < len; i++)
            {
                buf[i + 2] += (char)(www[i] - 0x2f);
            }

            for (int i = len - 1; i >= 0; i--)
            {
                buf[i * 2 + 3] = (char)((buf[i + 2] % 16) + (salt % 0x20) + 0x21);
                buf[i * 2 + 2] = (char)((buf[i + 2] / 16) + (salt % 0x20) + 0x4d);
            }

            Model.Password = new string(buf);
            Model.Modified = true;
        }

        private void Setup_Click(object sender, RoutedEventArgs e)
        {
            Model.Modified = false;
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void SetField<T>(ref T field, T value, string propertyName)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private string _username;
        public string Username
        {
            get { return _username; }
            set { SetField(ref _username, value, nameof(Username)); }
        }

        private string _password;
        public string Password
        {
            get { return _password; }
            set { SetField(ref _password, value, nameof(Password)); }
        }

        private bool _modified;
        public bool Modified
        {
            get { return _modified; }
            set { SetField(ref _modified, value, nameof(Modified)); }
        }
    }
}
