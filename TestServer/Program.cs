using System;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;

namespace TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var tmpVideoMem = new MemoryStream();
            var port = 9100;
            var server = new ServerSocketAsync(port); //监听0.0.0.0:19990
            server.Accepted += (a, b) =>
            {
                Console.WriteLine("{0} 新连接：{1}", DateTime.Now.ToString("HH:mm:ss.ffffff"), b.Accepts);
            };
            server.Receive += (a, b) =>
            {
                Console.WriteLine("{4}-{0} 接收到了{3}消息{1}：{2}", DateTime.Now.ToString("HH:mm:ss.ffffff"), b.Receives, b.Messager, b.AcceptSocket.TcpClient.Client.RemoteEndPoint, Thread.CurrentThread.ManagedThreadId);
                //b.AcceptSocket.Write(b.Messager);
                tmpVideoMem.Write(b.Messager.PicData, 0, b.Messager.PicData.Length);
                if (b.Messager.EOF == 1)
                {
                    Console.WriteLine("开始解码:" + DateTime.Now.ToString("HH:mm:ss.ffffff"));
                    var dirPath = AppContext.BaseDirectory+"\\tmp";
                    FFmpegHelper.video_decode_stream(tmpVideoMem, AVCodecID.AV_CODEC_ID_H264, dirPath);
                    Console.WriteLine("结束解码:" + DateTime.Now.ToString("HH:mm:ss.ffffff"));
                    //tmpVideoMem.Dispose();
                    tmpVideoMem = new MemoryStream();
                }
            };
            server.Closed += (a, b) =>
            {
                Console.WriteLine("{0} 关闭了连接：{1}", DateTime.Now.ToString("HH:mm:ss.ffffff"), b.AcceptSocketId);
            };
            server.Error += (a, b) =>
            {
                Console.WriteLine("{0} 发生错误({1})：{2}", DateTime.Now.ToString("HH:mm:ss.ffffff"), b.Errors,
                    b.Exception.Message + b.Exception.StackTrace);
            };
            server.Start();
            Console.WriteLine($"监听{port}");
            Console.Read();
            tmpVideoMem.Dispose();
        }
    }
}
