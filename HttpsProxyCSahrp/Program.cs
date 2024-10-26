using HttpsProxyCSharp.Transer;
using HttpsProxyCSharp;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;

/*
	HTTP 其实就是http连接 但是要处理一下"Proxy-Connection"，一般是替换成"Connection: close"

	Request
		GET http://c.pki.goog/r/r1.crl HTTP/1.1\r\n
		Cache-Control: max-age = 3000\r\n
		Proxy-Connection: Keep-Alive\r\n
		Accept: * / *\r\n
		If-Modified-Since: Thu, 25 Jul 2024 14:48:00 GMT\r\n
		User-Agent: Microsoft-CryptoAPI/10.0\r\n
		Host: c.pki.goog\r\n
		\r\n
	Response
		...
*/

/*
	HTTPS 开头明文的HTTP请求，并且是CONNECT。然后才是建立SSL，成功后基于SSL进行真正的HTTP通信。

	Request
		CONNECT accounts.google.com:443 HTTP/1.1\r\n
		Host: accounts.google.com:443\r\n
		Proxy-Connection: keep-alive\r\n
		User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36\r\n
		\r\n
	Response
		HTTP/1.1 200 Connection established\r\n
		\r\n

*/

namespace HttpsProxyCSahrp
{
    internal class Program
    {
        static CertHelper _certHelper = null;

        static async Task Main(string[] args)
        {
            var pathCaCert = FixPath("../../../../ca.crt");
            var pathCaPrivateKey = FixPath("../../../../ca.key");
            var pathServerPrivateKey = FixPath("../../../../server.key");

            _certHelper = CertHelper.Create(pathCaCert, pathCaPrivateKey, pathServerPrivateKey);

            _ = StartServer(8000);

            while (true)
            {
                await Task.Delay(2000); // 等待一段时间
            }

        }

        static string FixPath(string path)
        {
            if (File.Exists(path))
            {
                return path;
            }
            if (File.Exists(Path.GetFileName(path)))
            {
                return Path.GetFileName(path);
            }
            return "";
        }

        private static RSA LoadPrivateKey(string path)
        {
            var keyPem = File.ReadAllText(path);
            var rsa = RSA.Create();
            rsa.ImportFromPem(keyPem);
            return rsa;
        }

        static async Task StartServer(ushort port)
        {
            var ep = new IPEndPoint(System.Net.IPAddress.Any, port);
            try
            {

                TcpListener listen = new TcpListener(ep);
                listen.Start();

                Console.WriteLine("listening on {0}", listen.LocalEndpoint.ToString());

                while (true)
                {
                    Socket sockLocal = await listen.AcceptSocketAsync();
                    //Console.WriteLine("client connected {0}", cli.Client.RemoteEndPoint?.ToString());

                    _ = HandleClientAsync(sockLocal);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("listening on port{0} error: {1}", ep.ToString(), e.Message);
            }
        }

        static async Task HandleClientAsync(Socket sockLocal)
        {
            string host = "";
            byte[] buf = new byte[1024];

            try
            {
                byte[] pattern = new byte[] { 0x0d, 0x0a, 0x0d, 0x0a };
                var headers = await Utils.ReadUtil(sockLocal, pattern, true);
                if (string.IsNullOrEmpty(headers))
                {
                    throw new Exception("未知的协议:" + headers);
                }

                bool isHttps = headers.StartsWith("CONNECT");

                //1. 解析出host
                var (host_, port) = GetHostByHeaders(headers); host = host_;
                if (string.IsNullOrWhiteSpace(host))
                    return;
                if (port == 0)
                    port = (ushort)(isHttps ? 443 : 80);

                //2. 连接真正的远端
                Socket sockRemote = new Socket(SocketType.Stream, ProtocolType.Tcp);
                bool succ = await ConnectAsync(sockRemote, host, port);
                if (!succ)
                {
                    Console.WriteLine($"connect {(isHttps ? "https" : "http")} {host}:{port} error");
                    return;
                }
                Console.WriteLine($"connect {(isHttps ? "https" : "http")} {host}:{port} succ");

                Stream streamLocal = new NetworkStream(sockLocal);
                Stream streamRemote = new NetworkStream(sockRemote);

                //3. https额外处理
                if (isHttps)
                {
                    //3.1 消耗代理rquest header，没有data
                    await Utils.ReadUtil(sockLocal, new byte[] { 0x0d, 0x0a, 0x0d, 0x0a }, false);

                    //3.2 返回代理成功
                    var respFirst = "HTTP/1.1 200 Connection established\r\n\r\n";
                    await sockLocal.SendAsync(Encoding.UTF8.GetBytes(respFirst));

                    //3.3 建立ssl
                    var sslStreamLocal = new SslStream(streamLocal, false);
                    var sslStreamRemote = new SslStream(streamRemote, false);

                    //3.3.1 与remote建立ssl
                    await sslStreamRemote.AuthenticateAsClientAsync(host);

                    //3.3.2 与local建立ssl
                    var serverCert = _certHelper.CreateServerCert(host);
                    serverCert = new X509Certificate2(serverCert.Export(X509ContentType.Pkcs12));
                    await sslStreamLocal.AuthenticateAsServerAsync(serverCert, false, SslProtocols.Tls12, true);

                    streamLocal = sslStreamLocal;
                    streamRemote = sslStreamRemote;
                }

                //4. 转发
                await PreTransactAsync(host, port, streamLocal, streamRemote);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{host}] HandleClientAsync error {0}", e.Message);
            }
        }

        static async Task PreTransactAsync(string host, ushort port, Stream streamLocal, Stream streamRemote)
        {
            string headers = await Utils.ConsumeHeaderAndProcessProxyConnection(streamLocal, streamRemote);

            // 判断是不是WebSocket
            ITranser transer = IsUpgradeWebSocket(headers)
                ? new WebSocketTranser()
                : new SimpleTranser();

            await transer.Trans(host, streamLocal, streamRemote);
        }

        static (string, ushort) GetHostByHeaders(string headers)
        {
            //有限取host字段，如果没有再说
            string host = "";
            ushort port = 0;

            var v = headers.ToLower().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < v.Length; i++)
            {
                var vv = v[i].Split(':', StringSplitOptions.TrimEntries);

                if (vv[0] != "host")
                {
                    continue;
                }

                if (vv.Length == 3)
                {
                    host = vv[1];
                    port = ushort.Parse(vv[2]);
                    break;
                }
                else if (vv.Length == 2)
                {
                    //没有端口
                    host = vv[1];
                    break;
                }
            }

            //分析第一行拿到端口
            if (port == 0 && v.Length > 0)
            {
                //GET http://c.pki.goog:80/r/r1.crl HTTP/1.1
                var vv = v[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (vv.Length == 3)
                {
                    var vvv = vv[1].Split(':', StringSplitOptions.RemoveEmptyEntries);

                    if (vvv.Length >= 2)
                    {
                        if (vvv[0].StartsWith("http") || vvv[0].StartsWith("https"))
                        {
                            //http(s)://www.baidu.com:8888/asdaxzczx
                            if (vvv.Length >= 3)
                            {
                                var vvvv = vvv[2].Split('/', 2);
                                port = ushort.Parse(vvvv[0]);
                            }
                        }
                        else
                        {
                            //www.baidu.com:8888/asdaxzczx
                            var vvvv = vvv[1].Split('/', 2);
                            port = ushort.Parse(vvvv[0]);
                        }

                    }
                    
			        if(port == 0)
                    {
                        //没有端口，由协议决定
                        if (vv[1].StartsWith("http://"))
                        {
                            port = 80;
                        }
                        else if (vv[1].StartsWith("https://"))
                        {
                            port = 443;
                        }
                    }
                }
            }

            return (host, port);
        }

        static async Task<bool> ConnectAsync(Socket sock, string host, int port)
        {
            try
            {
                await sock.ConnectAsync(host, port);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        static bool IsUpgradeWebSocket(string headers)
        {
            string header_connection = "";
            string header_upgrade = "";
            var list = headers.ToLower().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string item in list)
            {
                var kv = item.Split(':', 2, StringSplitOptions.TrimEntries);
                if (kv.Length == 2)
                {
                    if (kv[0] == "connection")
                    {
                        header_connection = kv[1];
                    }
                    else if (kv[0] == "upgrade")
                    {
                        header_upgrade = kv[1];
                    }
                }
            }

            if (header_connection == "upgrade"
                && header_upgrade == "websocket")
            {
                return true;
            }
            return false;
        }
    }
}
