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

            var cup = new CancellationTokenSource();
            var cdown = new CancellationTokenSource();

            var taskUp = TransNormal(host, local, remote, cup.Token);
            var taskDown = TransNormal(host, remote, local, cdown.Token);

            await Task.WhenAny(taskUp, taskDown);

            //等待数据发完
            await Task.Delay(1000);                 

            //取消另一端
            cup.Cancel(false);
            cdown.Cancel(false);

            await Task.WhenAll(taskUp, taskDown);

            //Console.WriteLine($"[{host}] Trans end");
        }

        async Task TransNormal(string host, Stream from, Stream to, CancellationToken ct)
        {
            try
            {
                byte[] buf = new byte[1024];

                while (!ct.IsCancellationRequested)
                {

                    int n = await from.ReadAsync(buf, 0, buf.Length, ct);
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
