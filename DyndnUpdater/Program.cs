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

        public static void Main(string[] args)
        {
            Client = new DyndnClient();
            Xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TwnicDyndn.xml");
            Config = new XmlDocument();

            try
            {
                Console.WriteLine("載入設定檔 {0}", Xml);
                Config.Load(Xml);

                Client.Username = Config["Login"].GetAttribute("Username");
                Client.Password = Config["Login"].GetAttribute("Password");
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
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

            Client.Query();
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

        public string Username { get; set; }
        public string Password { get; set; }
        public DyndnServer[] Servers { get; set; }

        private DyndnServer[] _nameServers;
        private TcpClient _tcpClient;
        private UdpClient _udpClient;

        private bool Open(DyndnServer server)
        {
            IAsyncResult result;

            if (server.Protocol == ProtocolType.Tcp)
            {
                _tcpClient = new TcpClient();

                try
                {
                    result = _tcpClient.BeginConnect(server.Host, server.Port, null, null);

                    if (result.AsyncWaitHandle.WaitOne(3000))
                    {
                        _tcpClient.EndConnect(result);
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
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
                    Console.WriteLine(exception.Message);
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

                        if (result.AsyncWaitHandle.WaitOne(3000))
                        {
                            length = stream.EndRead(result);
                            return Encoding.Default.GetString(buffer, 0, length);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
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

                    if (result.AsyncWaitHandle.WaitOne(3000))
                    {
                        buffer = _udpClient.EndReceive(result, ref endpoint);
                        return Encoding.Default.GetString(buffer);
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            }

            return null;
        }

        private void Parse(string response)
        {
            string[] lines, tokens;
            List<DyndnServer> servers;

            lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            servers = new List<DyndnServer>();

            foreach (string line in lines)
            {
                tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                if (tokens[0] == "201")
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
                else if (tokens[0] == "202")
                {
                }
            }

            if (servers.Count > 0)
            {
                _nameServers = servers.ToArray();
            }
        }

        public void Query()
        {
            string request, response;

            request = string.Format("102 {0} {1}\r\n", Username, Password);

            foreach (DyndnServer server in Servers)
            {
                if (Open(server))
                {
                    response = Send(request);

                    if (response != null)
                    {
                        Parse(response);
                    }

                    Close();
                }
            }
        }
    }
}
