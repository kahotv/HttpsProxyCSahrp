using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace HttpsProxyCSharp
{
    internal class CertHelper
    {
        private X509Certificate2 _caCert;
        private RSA _caPriKey;
        private RSA _serverPriKey;

        public static CertHelper Create(string pathCa, string pathCaKey, string pathServerKey)
        {
            var obj = new CertHelper();

            //载入CA证书、CA私钥、Server私钥
            obj._caCert = new X509Certificate2(pathCa);
            obj._caPriKey = LoadPrivateKey(pathCaKey);
            obj._serverPriKey = LoadPrivateKey(pathServerKey);

            obj._caCert = obj._caCert.CopyWithPrivateKey(obj._caPriKey);

            return obj;
        }

        /// <summary>
        /// 创建一个服务器证书
        /// </summary>
        /// <param name="subjectName"></param>
        /// <returns></returns>
        public X509Certificate2 CreateServerCert(string host)
        {
            string subjectName = "CN=" + host;
            var request = new CertificateRequest(subjectName, _serverPriKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var builder = new SubjectAlternativeNameBuilder();
            foreach (var name in CreateDnsList(host))
            {
                builder.AddDnsName(name);
            }
            var ext = builder.Build();

            request.CertificateExtensions.Add(new X509Extension(ext.Oid, ext.RawData, false));

            var serverCert = request.Create(_caCert, _caCert.NotBefore, _caCert.NotAfter, Guid.NewGuid().ToByteArray());
            serverCert = serverCert.CopyWithPrivateKey(_serverPriKey);
            /*
                验证刚签好的证书（好像用的系统证书库里的CA，所以必须要先安装CA）
                Console.WriteLine(certServer);
                var z = certServer.ExportCertificatePem();
                File.WriteAllText("d:\\111.crt", z);    //顺便写出来看看

                using(var chain = new X509Chain())
                {
                    chain.ChainPolicy.ExtraStore.Add(certCA);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                    bool isValid = chain.Build(certServer);
                }

             */

            return serverCert;
        }

        private static List<string> CreateDnsList(string host)
        {
            /*

	            正常情况：

	            input:
	            		"baidu.com"
	            output:
	            		"baidu.com"
	            	  "*.baidu.com"

	            input:
	            		"www.baidu.com"
	            output:
	            		"www.baidu.com"
	            		  "*.baidu.com"

	            input:
	            		"123.api.baidu.com"
	            output:
	            		"123.api.baidu.com"
	            		  "*.api.baidu.com"
	            			  "*.baidu.com"
	            ...

	            异常情况：
	            input:
	            		""
	            output:
	            		empty

	            input:
	            		"com"
	            output:
	            		empty
	        */


            var ret = new List<string>();

            var tmp = host.Split('.');

            if(tmp.Length < 2)
            {
                return ret;
            }
            if (tmp.Length == 2)
            {
                ret.Add("*." + host);
            }
            else
            {
                var tmp2 = new List<string>();
                for (int i = tmp.Length - 1; 0 <= i && i < tmp.Length; i--)
                {
                    tmp2.Insert(0, tmp[i]);
                    string dns = "*." + string.Join('.', tmp2);
                    ret.Insert(0, dns);
                }

                //移除首尾
                ret.RemoveAt(0);
                ret.RemoveAt(ret.Count - 1);
            }

            //首部添加原身
            ret.Insert(0, host);

            return ret;
        }

        private static RSA LoadPrivateKey(string path)
        {
            var keyPem = File.ReadAllText(path);
            var rsa = RSA.Create();
            rsa.ImportFromPem(keyPem);
            return rsa;
        }
    }
}
