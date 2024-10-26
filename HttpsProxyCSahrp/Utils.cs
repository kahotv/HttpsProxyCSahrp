using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpsProxyCSharp
{
    internal class Utils
    {
        public static async Task<string> ReadUtil(Socket sock, byte[] pattern, bool peek)
        {
            //可以peek，就批量读，然后搜索。搜到了就真实读到那个位置。

            byte[] ms = new byte[0];
            byte[] buf = new byte[1024];

            while (true)
            {
                int n = await sock.ReceiveAsync(buf, SocketFlags.Peek);

                //Console.WriteLine("{0}\n", HexDump.HexDump.Format(buf.Take(n).ToArray()));

                ms = ms.Concat(buf.Take(n).ToArray()).ToArray();
                n = PatternAt(ms, pattern).First();
                if (n != -1)
                {
                    //ms是返回的
                    ms = ms.Take(n + pattern.Length).ToArray();

                    if (!peek)
                    {
                        //下面的是不要的
                        int total = ms.Length;
                        int now = 0;
                        while (now < total)
                        {
                            //每次只接收这么多
                            int need = Math.Min(buf.Length, total - now);
                            n = await sock.ReceiveAsync(new Memory<byte>(buf, 0, need), SocketFlags.None);
                            now += n;
                        }
                    }

                    break;
                }
            }

            return Encoding.UTF8.GetString(ms);

            //bool succ = false;
            //StringBuilder header = new StringBuilder();

            //var stream = new NetworkStream(sock);
            //StreamReader sr = new StreamReader(stream);

            //{
            //    while (true)
            //    {
            //        string? tmp = await sr.ReadLineAsync();
            //        if (tmp == null) {
            //            break;
            //        }
            //        header.AppendLine(tmp);

            //        if (string.IsNullOrWhiteSpace(tmp)) {
            //            succ = true;
            //            break;
            //        }

            //    }
            //}
            //return succ ? header.ToString() : "";

        }
        public static async Task<byte[]> ReadUtil(Stream sock, byte[] pattern,uint max)
        {
            //PS 不知道怎么peek，所以就每次只读一个字节，读完后判断一下末尾

            byte[] ret = new byte[0];

            int pos = 0;
            int step = 1024;
            while (true)
            {
                if(pos >= ret.Length)
                {
                    if(pos >= max)
                    {
                        return new byte[0];
                    }

                    ret = ret.Concat(new byte[step]).ToArray();
                }

                await sock.ReadAsync(ret, pos, 1);

                pos += 1;

                //直接判断末尾
                if (pos < pattern.Length) 
                {
                    continue;
                }

                bool find = true;
                int spos = pos - pattern.Length;
                for (int i = 0; i < pattern.Length; i++) 
                {
                    if(ret[spos + i] != pattern[i])
                    {
                        find = false;
                        break;
                    }
                }

                if (!find)
                {
                    continue;
                }

                return ret.Take(pos).ToArray();
            }
        }
        public static IEnumerable<int> PatternAt(byte[] source, byte[] pattern)
        {
            for (int i = 0; i < source.Length; i++)
            {
                if (source.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                {
                    yield return i;
                }
            }
        }

        public static string ReplaceHttpHeader(string headers,string name,string nameAndValue)
        {
            name = name.ToLower();
            var tmp = headers.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var item in tmp)
            {
                if (item.ToLower().StartsWith(name))
                {
                    headers = headers.Replace(item, nameAndValue);
                    break;
                }
            }

            return headers;
        }

        public static async Task<string> ConsumeHeaderAndProcessProxyConnection(Stream local,Stream remote)
        {

            byte[] pattern = new byte[] { 0x0d, 0x0a, 0x0d, 0x0a };
            var headers_bytes = await Utils.ReadUtil(local, pattern,0x1000);
            //Console.WriteLine(Encoding.UTF8.GetString(headers_bytes));

            //处理http的Proxy-Connection
            if (local.GetType() == typeof(NetworkStream))
            {
                string headers = Utils.ReplaceHttpHeader(
                    Encoding.UTF8.GetString(headers_bytes),
                    "proxy-connection", "Connection: close");
                headers_bytes = Encoding.UTF8.GetBytes(headers);
            }
            else if (local.GetType() == typeof(SslStream))
            {
                //https不需要处理？
            }


            await remote.WriteAsync(headers_bytes, 0, headers_bytes.Length);

            return Encoding.UTF8.GetString(headers_bytes, 0, headers_bytes.Length);
        }

        public static string Bin2Hex(byte[] data, int start = 0, int len = -1, string delimiter = "")
        {
            if (len == -1)
            {
                len = data.Length;
            }

            string ret = String.Join(delimiter, data.Skip(start).Take(len).Select(b => b.ToString("X2")).ToArray());
            return ret;
        }
    }
}
