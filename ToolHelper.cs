using System;
using System.IO;
using System.Net;
using System.Security.Policy;
using System.Text;

namespace 美团充电仓CCD
{
    public class WebHelper
    {

        public static string Post(string Url, string jsonStr = "")
        {
            try
            {
                //创建一个HTTP请求  
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                //Post请求方式  
                request.Method = "POST";
                //内容类型
                request.ContentType = "application/json";
                request.Timeout = 1000;
                //设置参数，并进行URL编码 
                string paraUrlCoded = jsonStr;//System.Web.HttpUtility.UrlEncode(jsonParas);   
                byte[] payload;
                //将Json字符串转化为字节  
                payload = System.Text.Encoding.UTF8.GetBytes(paraUrlCoded);
                //设置请求的ContentLength   
                request.ContentLength = payload.Length;
                //发送请求，获得请求流 
                Stream writer;
                try
                {
                    writer = request.GetRequestStream();//获取用于写入请求数据的Stream对象
                }
                catch (Exception ex)
                {
                    writer = null;
                    throw ex;
                }
                //将请求参数写入流
                writer.Write(payload, 0, payload.Length);
                writer.Close();//关闭请求流
                               // String strValue = "";//strValue为http响应所返回的字符流
                HttpWebResponse response;
                try
                {
                    //获得响应流
                    response = (HttpWebResponse)request.GetResponse();
                }
                catch (WebException ex)
                {
                    response = ex.Response as HttpWebResponse;
                }
                Stream s = response.GetResponseStream();
                //  Stream postData = Request.InputStream;
                StreamReader sRead = new StreamReader(s);
                string postContent = sRead.ReadToEnd();
                sRead.Close();
                return postContent;//返回Json数据
            }
            catch(Exception ex)
            {
                LogHelper.WriteLogs("Post请求异常："+ex.Message);
            }
            return null;
        }


        public static string Get(string Url)
        {
            HttpWebResponse response = null;
            Stream s = null;
            StreamReader sRead = null;

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.Method = "GET";
                request.ContentType = "application/json; charset=utf-8";
                request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                request.Timeout = 5000;

                response = (HttpWebResponse)request.GetResponse();
                s = response.GetResponseStream();
                sRead = new StreamReader(s, Encoding.UTF8);

                return sRead.ReadToEnd();
            }
            catch (WebException ex)
            {
                LogHelper.WriteLogs("Get请求WebException：" + ex.Message);

                response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    s = response.GetResponseStream();
                    sRead = new StreamReader(s, Encoding.UTF8);
                    return sRead.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogs("Get请求异常：" + ex.Message);
            }
            finally
            {
                if (sRead != null)
                    sRead.Close();
                if (s != null)
                    s.Close();
                if (response != null)
                    response.Close();
            }

            return null;
        }
    }

    public class LogHelper
    {
        private static readonly object fileLock = new object();

        public static void WriteLogs(string logInfo)
        {
            lock (fileLock)
            {
                string FloderPath = AppDomain.CurrentDomain.BaseDirectory;
                string FilePath = FloderPath.Replace("/", "\\") + "Logs\\";
                if (!Directory.Exists(FilePath))
                {
                    Directory.CreateDirectory(FilePath);
                }

                FilePath += DateTime.Now.ToString("yyyyMMddHH") + "log.txt";
                StreamWriter writer = new StreamWriter(FilePath, true, System.Text.Encoding.UTF8);
                writer.WriteLine(logInfo);
                writer.Close();
            }
        }
    }
}
