using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpsProxyCSharp.Transer
{
    internal class WebSocketTranser : ITranser
    {
        public async Task Trans(string host, Stream local, Stream remote)
        {
            Console.WriteLine($"[{host}] WebSocket Trans begin");

            //读取HTTP Response
            byte[] pattern = new byte[] { 0x0d, 0x0a, 0x0d, 0x0a };
            var headers_bytes = await Utils.ReadUtil(remote, pattern, 0x1000);
            Console.WriteLine(Encoding.UTF8.GetString(headers_bytes));
            //写入HTTP Response
            await local.WriteAsync(headers_bytes);

            //接下来都是WebSocket数据
            var taskUp = TransTlsWebsocketSafe(host, local, remote, true);
            var taskDown = TransTlsWebsocketSafe(host, remote, local, false);

            await taskUp;
            await taskDown;
            Console.WriteLine($"[{host}] WebSocket Trans end");
        }

        async Task TransTlsWebsocketSafe(string host, Stream from, Stream to, bool up)
        {
            try
            {
                await TransTlsWebsocket(host, from, to, up);
            }
            catch (Exception e)
            {
            }
        }

        async Task TransTlsWebsocket(string host, Stream from, Stream to, bool up)
        {
            /*
                 0               1               2               3            
                 0 1 2 3 4 5 6 7 0 1 2 3 4 5 6 7 0 1 2 3 4 5 6 7 0 1 2 3 4 5 6 7
                +-+-+-+-+-------+-+-------------+-------------------------------+
                |F|R|R|R| opcode|M| Payload len |    Extended payload length    |
                |I|S|S|S|  (4)  |A|     (7)     |             (16/64)           |
                |N|V|V|V|       |S|             |   (if payload len==126/127)   |
                | |1|2|3|       |K|             |                               |
                +-+-+-+-+-------+-+-------------+ - - - - - - - - - - - - - - - +
                |     Extended payload length continued, if payload len == 127  |
                + - - - - - - - - - - - - - - - +-------------------------------+
                |                               |Masking-key, if MASK set to 1  |
                +-------------------------------+-------------------------------+
                | Masking-key (continued)       |          Payload Data         |
                +-------------------------------- - - - - - - - - - - - - - - - +
                :                     Payload Data continued ...                :
                + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
                |                     Payload Data continued ...                |
                +---------------------------------------------------------------+
            */
            byte[] buf = new byte[1024];
            while (true)
            {
                await from.ReadExactlyAsync(buf, 0, 1 + 1);

                byte wsFlagAndOpcode = buf[0];
                byte wsMaskAndLen = buf[1];
                ulong len = 0;
                if ((wsMaskAndLen & 0x7F) == 126)
                {
                    await from.ReadExactlyAsync(buf, 0, 2);
                    len = BinaryPrimitives.ReadUInt16BigEndian(new ReadOnlySpan<byte>(buf, 0, 2));
                }
                else if ((wsMaskAndLen & 0x7F) == 127)
                {
                    await from.ReadExactlyAsync(buf, 0, 8);
                    len = BinaryPrimitives.ReadUInt64BigEndian(new ReadOnlySpan<byte>(buf, 0, 8));
                }
                else
                {
                    len = (ushort)(wsMaskAndLen & 0x7F);
                }

                bool havePassword = (wsMaskAndLen & 0x80) == 0x80;
                uint pwd = 0;
                if (havePassword)
                {
                    await from.ReadExactlyAsync(buf, 0, 4);
                    pwd = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(buf, 0, 4));
                }

                ulong tmplen = len + (4 - (len % 4));     //扩展成4的倍数，方便解密
                byte[] tmp = new byte[tmplen];

                await from.ReadExactlyAsync(tmp, 0, (int)len); //TODO 不支持超过int.MaxValue的长度

                //解密
                if (havePassword)
                {
                    for (int i = 0; i < tmp.Length / 4; i++)
                    {
                        uint val = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(tmp, i * 4, 4));
                        val ^= pwd;
                        BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(tmp, i * 4, 4), val);
                    }
                }

                tmp = await TransWebsocketData(host, tmp.Take((int)len).ToArray(), up);


                //写入remote
                int n = 0;
                buf[n++] = wsFlagAndOpcode;
                buf[n++] = (byte)(wsMaskAndLen & 0x7F); //set pwd=0
                if ((wsMaskAndLen & 0x7F) == 126)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(new Span<byte>(buf, n, 2), (ushort)len); n += 2;
                }
                else if ((wsMaskAndLen & 0x7F) == 127)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(new Span<byte>(buf, n, 8), (ushort)len); n += 8;

                }
                // set pwd=0
                //if ((wsMaskAndLen & 0x80) == 0x80)
                //{
                //    BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(buf, n, 4), 0); n += 4; 
                //}

                await to.WriteAsync(buf, 0, n);
                await to.WriteAsync(tmp, 0, tmp.Length);
            }
        }

        async Task<byte[]> TransWebsocketData(string host, byte[] data, bool up)
        {
            Console.WriteLine((up ? "WSRequest:" : "WSResponse: ") + Utils.Bin2Hex(data, 0, -1, " "));
            return data;
        }




    }
}
