using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.IO;
using System.Threading.Tasks;
using System.Threading;

namespace TestServer
{

    public class Program
    {
        /// <summary>
        /// 获取时间戳
        /// </summary>
        public static long TimeToken => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        public static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            //ReferenceHandler = ReferenceHandler.Preserve
            //IgnoreNullValues = true,
            //WriteIndented = true
        };
        public static ConcurrentDictionary<string, VideoInfo> VideoInfoDic = new ConcurrentDictionary<string, VideoInfo>();

        static void Main(string[] args)
        {
            var port = 9101;
            var server = new ServerSocketAsync(port); //监听0.0.0.0:19990

            var timeTicket = DateTime.Now.Ticks;
            AsyncMqtt.UseMqttMessageReceive();//注册mqtt连接
            server.Accepted += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("new connect" + b.Accepts + "-" + b.AcceptSocket.TcpClient.Client.RemoteEndPoint);
            };
            server.Receive += async (a, b) =>
            {
                b.AcceptSocket.Write(new SocketMessager(b.Messager.TimeToken, 1, b.Messager.Sn, new byte[] { 0x00 }));
                ServerSocketAsync._serverLog.Information(b.Messager.ToString());
                VideoInfoDic.TryGetValue(b.Messager.Sn, out VideoInfo videoInfo);
                if (videoInfo == null)
                {
                    videoInfo = new VideoInfo();
                    VideoInfoDic.TryAdd(b.Messager.Sn, videoInfo);
                    await AsyncMqtt.SendStrMsg("searchUser", $"{{\"sn\":\"{b.Messager.Sn}\"}}");
                }

                try
                {
                    if (videoInfo.PicItem == null)
                    {
                        videoInfo.PicItem = new PicItem { Time = b.Messager.TimeToken };
                    }
                    if (videoInfo.PicItem.Pic1 == null)
                    {
                        videoInfo.PicItem.Pic1 = Convert.ToBase64String(b.Messager.PicData);
                        File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-1.jpg", b.Messager.PicData);
                    }
                    else if (videoInfo.PicItem.Pic2 == null)
                    {
                        videoInfo.PicItem.Pic2 = Convert.ToBase64String(b.Messager.PicData);
                        File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-2.jpg", b.Messager.PicData);
                    }
                    else if (videoInfo.PicItem.Pic3 == null)
                    {
                        videoInfo.PicItem.Pic3 = Convert.ToBase64String(b.Messager.PicData);
                        File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-3.jpg", b.Messager.PicData);
                    }
                    else
                    {
                        videoInfo.PicItem.Pic1 = null;
                        videoInfo.PicItem.Pic2 = null;
                        videoInfo.PicItem.Pic3 = null;
                    }
                    if (videoInfo.PicItem.Pic1 != null && videoInfo.PicItem.Pic2 != null && videoInfo.PicItem.Pic3 != null && videoInfo.PicItem.User > 0)
                    {
                        //videoInfo.PicItem.Time = b.Messager.TimeToken;
                        videoInfo.PicItem.Time = TimeToken;//先取服务器时间
                        videoInfo.PicItem.Sn = b.Messager.Sn;
                        await AsyncMqtt.SendStrMsg("sendPic", JsonSerializer.Serialize(videoInfo.PicItem, JsonSerializerOptions));
                        videoInfo.PicItem.Pic1 = null;
                        videoInfo.PicItem.Pic2 = null;
                        videoInfo.PicItem.Pic3 = null;
                    }
                }
                catch (Exception ex)
                {
                    ServerSocketAsync._serverLog.Information(ex?.Message + "\r\n" + ex?.StackTrace);
                    ServerSocketAsync._serverLog.Error(ex, "error occurred");
                }
            };
            server.Closed += (a, b) =>
            {
                //TODO 先屏蔽
                //按sn当key先不处理
                // if (b.Accepts > 0 && VideoInfoDic.TryGetValue(b.AcceptSocketId, out var tmpVideo))
                // {
                //     tmpVideo.VideoStream?.Dispose();
                //     VideoInfoDic.TryRemove(b.AcceptSocketId, out _);
                // }
                // ServerSocketAsync._serverLog.Information("关闭了连接：{0}", b.AcceptSocketId);
            };
            server.Error += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("error occurred ({0})：{1} {2}", b.Errors, b.Exception.Message, b.Exception.StackTrace);
            };
            server.Start();
            ServerSocketAsync._serverLog.Information($"listen {port}");
            //Console.Read();
            while (true)
            {
                Thread.Sleep(1000);
            }
        }

    }

}
