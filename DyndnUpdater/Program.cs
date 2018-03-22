using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace DyndnUpdater
{
    public class Program
    {
        public static DyndnClient Client { get; set; }
        public static string Xml { get; set; }
        public static XmlDocument Config { get; set; }
        public static bool Exiting { get; set; }
        public static ManualResetEvent ExitEvent { get; set; }

        public static void Main(string[] args)
        {
            Client = new DyndnClient();
            Xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TwnicDyndn.xml");
            Config = new XmlDocument();
            Exiting = false;
            ExitEvent = new ManualResetEvent(false);

            try
            {
                Client.Println("載入設定檔 {0}", Xml);
                Config.Load(Xml);

                Client.Username = Config["Login"].GetAttribute("Username");
                Client.Password = Config["Login"].GetAttribute("Password");
            }
            catch (Exception exception)
            {
                Client.Println(exception.Message);
                return;
            }

            Client.Servers = new DyndnServer[]
            {
                new DyndnServer
                {
                    Host = "dyndn1.twnic.net.tw",
                    Port = 1053,
                    Protocol = ProtocolType.Tcp,
                },
                new DyndnServer
                {
                    Host = "dyndn1.twnic.net.tw",
                    Port = 1053,
                    Protocol = ProtocolType.Udp,
                },
            };

            Console.CancelKeyPress += (sender, e) =>
            {
                Exiting = true;
                ExitEvent.Set();

                e.Cancel = true;
            };

            while (!Exiting)
            {
                if (!Client.Query())
                {
                    if (Client.LastQuery && !Exiting)
                    {
                        if (ExitEvent.WaitOne(TimeSpan.FromSeconds(Client.LoginInterval)))
                        {
                            break;
                        }
                    }

                    continue;
                }

                do
                {
                    if (!Client.Login())
                    {
                        continue;
                    }

                    do
                    {
                        if (ExitEvent.WaitOne(TimeSpan.FromSeconds(Client.LoginInterval)))
                        {
                            break;
                        }
                    }
                    while (Client.Update());

                    Client.Logout();
                }
                while (!Client.LastLogin && !Exiting);
            }
        }
    }

    public class DyndnServer
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public ProtocolType Protocol { get; set; }

        public override string ToString()
        {
            return string.Format("{0} {1} {2}", Host, Port, Protocol);
        }
    }

    public class DyndnClient
    {
        [DllImport("kernel32.dll")]
        private static extern int GetSystemDefaultLangID();

        private string _username = null;
        private string _password = null;
        private DyndnServer[] _servers = null;
        private int _serverIndex = -1;
        private TcpClient _tcpClient = null;
        private UdpClient _udpClient = null;
        private DyndnServer[] _loginServers = null;
        private int _loginServerIndex = -1;
        private int _loginInterval = 0;
        private int _loginSession = 0;
        private bool _logoutSuccess = false;
        private bool _updateSuccess = false;

        public string Username { set => _username = value; }
        public string Password { set => _password = value; }
        public DyndnServer[] Servers { set => _servers = value; }
        public int LoginInterval { get => _loginInterval; }
        public bool LastQuery { get => (_serverIndex >= (_servers.Length - 1)); }
        public bool LastLogin { get => (_loginServerIndex >= (_loginServers.Length - 1)); }

        private bool Open(DyndnServer server)
        {
            IAsyncResult result;

            if (server.Protocol == ProtocolType.Tcp)
            {
                _tcpClient = new TcpClient();

                try
                {
                    result = _tcpClient.BeginConnect(server.Host, server.Port, null, null);

                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                    {
                        _tcpClient.EndConnect(result);
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Println(exception.Message);
                }

                _tcpClient.Close();
                _tcpClient = null;
            }

            if (server.Protocol == ProtocolType.Udp)
            {
                _udpClient = new UdpClient();

                try
                {
                    _udpClient.Connect(server.Host, server.Port);
                    return true;
                }
                catch (Exception exception)
                {
                    Println(exception.Message);
                }

                _udpClient.Close();
                _udpClient = null;
            }

            return false;
        }

        private void Close()
        {
            if (_tcpClient != null)
            {
                _tcpClient.Close();
                _tcpClient = null;
            }

            if (_udpClient != null)
            {
                _udpClient.Close();
                _udpClient = null;
            }
        }

        private string Send(string request)
        {
            byte[] buffer;
            int length;
            IPEndPoint endpoint;
            IAsyncResult result;

            if (_tcpClient != null)
            {
                try
                {
                    using (NetworkStream stream = _tcpClient.GetStream())
                    {
                        buffer = Encoding.Default.GetBytes(request);
                        stream.Write(buffer, 0, buffer.Length);

                        buffer = new byte[256];
                        result = stream.BeginRead(buffer, 0, buffer.Length, null, null);

                        if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                        {
                            length = stream.EndRead(result);
                            return Encoding.Default.GetString(buffer, 0, length);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Println(exception.Message);
                }
            }

            if (_udpClient != null)
            {
                try
                {
                    buffer = Encoding.Default.GetBytes(request);
                    _udpClient.Send(buffer, buffer.Length, null);

                    endpoint = null;
                    result = _udpClient.BeginReceive(null, null);

                    if (result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(3)))
                    {
                        buffer = _udpClient.EndReceive(result, ref endpoint);
                        return Encoding.Default.GetString(buffer);
                    }
                }
                catch (Exception exception)
                {
                    Println(exception.Message);
                }
            }

            return null;
        }

        private void Parse(string response)
        {
            string[] lines, tokens;
            List<DyndnServer> servers;

            if (!response.EndsWith("\r\n"))
            {
                return;
            }

            lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            servers = new List<DyndnServer>();

            Println("收到回應 {0}", string.Join(", ", lines));

            foreach (string line in lines)
            {
                tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                if (tokens[0] == "200") // 登出成功
                {
                    _logoutSuccess = true;
                }
                else if (tokens[0] == "201") // 伺服器資訊
                {
                    if (tokens[3] == "tcp")
                    {
                        servers.Add(new DyndnServer
                        {
                            Host = tokens[1],
                            Port = int.Parse(tokens[2]),
                            Protocol = ProtocolType.Tcp,
                        });
                    }
                    else if (tokens[3] == "udp")
                    {
                        servers.Add(new DyndnServer
                        {
                            Host = tokens[1],
                            Port = int.Parse(tokens[2]),
                            Protocol = ProtocolType.Udp,
                        });
                    }
                }
                else if (tokens[0] == "205") // 更新間隔
                {
                    _loginInterval = int.Parse(tokens[1]);
                    _updateSuccess = true;
                }
                else if (tokens[0] == "208") // 重新登入
                {
                    _updateSuccess = false;
                }
                else if (tokens[0] == "209") // 會話編號
                {
                    _loginSession = int.Parse(tokens[1]);
                }
                else
                {
                    Println("非預期的回應 {0}", tokens[0]);
                }
            }

            if (servers.Count > 0)
            {
                _loginServers = servers.ToArray();
            }

            if (_loginInterval < 30)
            {
                _loginInterval = 30;
            }
        }

        public bool Query()
        {
            string request, response;
            DyndnServer server;

            _serverIndex = (_serverIndex + 1) % _servers.Length;
            _loginServerIndex = -1;
            _loginServers = null;

            request = string.Format("102 {0} {1}\r\n", _username, _password);
            server = _servers[_serverIndex];

            Println("送出請求 102 {0}", server);

            if (Open(server))
            {
                response = Send(request);

                if (response != null)
                {
                    Parse(response);
                }

                Close();
            }

            return (_loginServers != null);
        }

        public bool Login()
        {
            string request, response;
            DyndnServer server;

            _loginServerIndex = (_loginServerIndex + 1) % _loginServers.Length;
            _loginInterval = 0;
            _loginSession = 0;

            request = string.Format("101 150 {0} {1}.{2} {3} {4}\r\n",
                GetSystemDefaultLangID(), Environment.OSVersion.Version.Major,
                Environment.OSVersion.Version.Minor, _username, _password);
            server = _loginServers[_loginServerIndex];

            Println("送出請求 101 {0}", server);

            if (Open(server))
            {
                response = Send(request);

                if (response != null)
                {
                    Parse(response);
                }

                Close();
            }

            return (_loginSession > 0);
        }

        public bool Logout()
        {
            string request, response;
            DyndnServer server;

            _logoutSuccess = false;

            request = string.Format("104 {0}\r\n", _username);
            server = _loginServers[_loginServerIndex];

            Println("送出請求 104 {0}", server);

            if (Open(server))
            {
                response = Send(request);

                if (response != null)
                {
                    Parse(response);
                }

                Close();
            }

            return _logoutSuccess;
        }

        public bool Update()
        {
            string request, response;
            DyndnServer server;

            _updateSuccess = false;

            request = string.Format("103 {0} {1}\r\n", _loginSession, _username);
            server = _loginServers[_loginServerIndex];

            Println("送出請求 103 {0}", server);

            if (Open(server))
            {
                response = Send(request);

                if (response != null)
                {
                    Parse(response);
                }

                Close();
            }

            return _updateSuccess;
        }

        public void Println(string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", DateTime.Now, string.Format(format, args));
        }
    }
}
