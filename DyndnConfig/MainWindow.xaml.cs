using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml;

namespace DyndnConfig
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel Model { get; set; }
        private string ConfigFile { get; set; }
        private XmlDocument ConfigXml { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            Model = DataContext as MainViewModel;
            ConfigFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TwnicDyndn.xml");
            ConfigXml = new XmlDocument();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ConfigXml.Load(ConfigFile);
            }
            catch
            {
                ConfigXml.AppendChild(ConfigXml.CreateXmlDeclaration("1.0", "UTF-8", null));
            }

            if (ConfigXml["Login"] == null)
            {
                ConfigXml.AppendChild(ConfigXml.CreateElement("Login"));
            }

            Model.Username = ConfigXml["Login"].GetAttribute("Username");
            Model.Password = ConfigXml["Login"].GetAttribute("Password");
            Model.Modified = false;
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
            ConfigXml["Login"].SetAttribute("Username", Model.Username);
            ConfigXml["Login"].SetAttribute("Password", Model.Password);
            ConfigXml.Save(ConfigFile);

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
