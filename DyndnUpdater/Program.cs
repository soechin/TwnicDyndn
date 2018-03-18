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
        public static void Main(string[] args)
        {
            string xml;
            XmlDocument config;
            DyndnClient client;
            object obj;
            bool exit;

            xml = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TwnicDyndn.xml");
            config = new XmlDocument();
            client = new DyndnClient();

            try
            {
                Console.WriteLine("載入設定檔 {0}", xml);
                config.Load(xml);

                client.Username = config["Login"].GetAttribute("Username");
                client.Password = config["Login"].GetAttribute("Password");
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                return;
            }

            client.DynServers = new DyndnServer[]
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

            obj = new object();
            exit = false;

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                lock (obj)
                {
                    Monitor.Pulse(obj);
                    exit = true;

                    e.Cancel = true;
                }
            };

            while (!exit)
            {
                if (!client.Query())
                {
                    break;
                }

                if (!client.Login())
                {
                    break;
                }

                while (client.Update())
                {
                    lock (obj)
                    {
                        if (Monitor.Wait(obj, Math.Max(client.Interval, 30) * 1000))
                        {
                            break;
                        }
                    }
                }

                client.Logout();
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

        public string Username { get; set; }
        public string Password { get; set; }
        public DyndnServer[] DynServers { get; set; }
        public DyndnServer[] NameServers { get; set; }
        public DyndnServer LoggedServer { get; set; }
        public int Interval { get; set; }
        public int Session { get; set; }

        private string Send(DyndnServer server, string request)
        {
            IPEndPoint endpoint;
            UdpClient client;
            byte[] buffer;

            try
            {
                endpoint = new IPEndPoint(Dns.GetHostAddresses(server.Host)[0], server.Port);
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception.Message);
                return null;
            }

            if (server.Protocol == ProtocolType.Udp)
            {
                client = new UdpClient();

                try
                {
                    buffer = Encoding.Default.GetBytes(request);
                    client.Send(buffer, buffer.Length, endpoint);

                    buffer = client.Receive(ref endpoint);
                    return Encoding.Default.GetString(buffer).TrimEnd('\0');
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
                finally
                {
                    client.Close();
                }
            }

            return null;
        }

        public bool Query()
        {
            List<DyndnServer> servers;
            string[] lines, tokens;
            string request, response;

            NameServers = null;

            request = string.Format("102 {0} {1}\r\n", Username, Password);
            servers = new List<DyndnServer>();

            foreach (DyndnServer server in DynServers)
            {
                Console.WriteLine("送出請求 102 {0}", server);
                response = Send(server, request);

                if (response == null)
                {
                    continue;
                }

                lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    Console.WriteLine("收到回應 {0}", line);
                    tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                    if (tokens[0] == "201") // 伺服器資訊
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
                    else
                    {
                        Console.WriteLine("非預期的回應 {0}", tokens[0]);
                    }
                }

                if (servers.Count > 0)
                {
                    NameServers = servers.ToArray();
                    return true;
                }
            }

            return false;
        }

        public bool Login()
        {
            string[] lines, tokens;
            string request, response;

            LoggedServer = null;
            Session = 0;

            request = string.Format("101 150 {0} {1}.{2} {3} {4}\r\n",
                GetSystemDefaultLangID(), Environment.OSVersion.Version.Major,
                Environment.OSVersion.Version.Minor, Username, Password);

            foreach (DyndnServer server in NameServers)
            {
                Console.WriteLine("送出請求 101 {0}", server);
                response = Send(server, request);

                if (response == null)
                {
                    continue;
                }

                lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string line in lines)
                {
                    Console.WriteLine("收到回應 {0}", line);
                    tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                    if (tokens[0] == "205") // 更新間隔
                    {
                        Interval = int.Parse(tokens[1]);
                    }
                    else if (tokens[0] == "209") // 會話編號
                    {
                        Session = int.Parse(tokens[1]);
                    }
                    else
                    {
                        Console.WriteLine("非預期的回應 {0}", tokens[0]);
                    }
                }

                if (Session != 0)
                {
                    LoggedServer = server;
                    return true;
                }
            }

            return false;
        }

        public bool Logout()
        {
            string[] lines, tokens;
            string request, response;
            bool ok;

            request = string.Format("104 {0}\r\n", Username);

            if (LoggedServer == null)
            {
                return false;
            }

            Console.WriteLine("送出請求 104 {0}", LoggedServer);
            response = Send(LoggedServer, request);

            if (response == null)
            {
                return false;
            }

            lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            ok = false;

            foreach (string line in lines)
            {
                Console.WriteLine("收到回應 {0}", line);
                tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                if (tokens[0] == "200") // OK
                {
                    ok = true;
                }
                else
                {
                    Console.WriteLine("非預期的回應 {0}", tokens[0]);
                }
            }

            if (ok)
            {
                LoggedServer = null;
                Session = 0;
            }

            return ok;
        }

        public bool Update()
        {
            string[] lines, tokens;
            string request, response;
            bool ok;

            request = string.Format("103 {0} {1}\r\n", Session, Username);

            if (LoggedServer == null)
            {
                return false;
            }

            Console.WriteLine("送出請求 103 {0}", LoggedServer);
            response = Send(LoggedServer, request);

            if (response == null)
            {
                return false;
            }

            lines = response.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            ok = false;

            foreach (string line in lines)
            {
                Console.WriteLine("收到回應 {0}", line);
                tokens = line.Split(new string[] { " " }, StringSplitOptions.None);

                if (tokens[0] == "205") // 更新間隔
                {
                    Interval = int.Parse(tokens[1]);
                    ok = true;
                }
                else if (tokens[0] == "208") // 重新連線?
                {
                    ok = true;
                }
                else
                {
                    Console.WriteLine("非預期的回應 {0}", tokens[0]);
                }
            }

            return ok;
        }
    }
}
