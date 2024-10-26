using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpsProxyCSharp.Transer
{
    interface ITranser
    {
        public Task Trans(string host, Stream local, Stream remote);
    }
}
