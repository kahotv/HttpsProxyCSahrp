using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpsProxyCSharp.Transer
{
    internal class SimpleTranser : ITranser
    {
        public async Task Trans(string host, Stream local, Stream remote)
        {
            //Console.WriteLine($"[{host}] Trans begin");

            var taskUp = TransNormal(host, local, remote);
            var taskDown = TransNormal(host, remote, local);

            await taskUp;
            await taskDown;
            //Console.WriteLine($"[{host}] Trans end");
        }

        async Task TransNormal(string host, Stream from, Stream to)
        {
            try
            {
                byte[] buf = new byte[1024];

                while (true)
                {

                    int n = await from.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        throw new Exception("cancelled");
                    }

                    //Console.WriteLine($"e[{strFrom} -> {strTo}]: read  end  , " + n + "\n{0}\n", HexDump.HexDump.Format(buf.Take(n).ToArray()));
                    await to.WriteAsync(new ReadOnlyMemory<byte>(buf, 0, n));
                }

            }
            catch (Exception e)
            {
            }
        }
    }
}
