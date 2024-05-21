using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using FFmpegAnalyzer;

namespace TestServer
{
    class Program
    {
        static void Main(string[] args)
        {
            var tmpVideoMem = new MemoryStream();
            var port = 9100;
            var server = new ServerSocketAsync(port); //监听0.0.0.0:19990
            FFmpegWrapper.RegisterFFmpeg();
            var decoderWrapper = new FFmpegWrapper();
            var size = new System.Drawing.Size(640, 480);
            var videoConvert = new VideoFrameConverter(size, AVPixelFormat.AV_PIX_FMT_YUV420P, size, AVPixelFormat.AV_PIX_FMT_BGR24);
            decoderWrapper.CreateDecoder(size, false);
            var timeTicket = DateTime.Now.Ticks;
            // //配置硬件解码器
            // FFmpegHelper.ConfigureHWDecoder(out var deviceType);
            // ServerSocketAsync._serverLog.Information("deviceType{0}", deviceType);

            server.Accepted += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("新连接：{0}", b.Accepts);
            };
            server.Receive += (a, b) =>
            {
                //ServerSocketAsync._serverLog.Information("{3} 接收到了{2}消息{0}：{1}", b.Receives, b.Messager, b.AcceptSocket.TcpClient.Client.RemoteEndPoint, Thread.CurrentThread.ManagedThreadId);
                //b.AcceptSocket.Write(b.Messager);
                tmpVideoMem.Write(b.Messager.PicData, 0, b.Messager.PicData.Length);
                if (b.Messager.EOF == 1)
                {
                    ServerSocketAsync._serverLog.Information("开始解码:" + tmpVideoMem.Length);
                    //BaseSocket.WriteLog(BitConverter.ToString(tmpVideoMem.ToArray()));
                    var dirPath = AppContext.BaseDirectory + "\\tmp";
                    //FFmpegHelper.video_decode_stream(tmpVideoMem, AVCodecID.AV_CODEC_ID_H264, dirPath);
                    var res = decoderWrapper.DecodeFrames(tmpVideoMem.ToArray());
                    using FileStream fsw = new FileStream(dirPath + "\\" + timeTicket + ".yuv", FileMode.Append, FileAccess.Write);
                    fsw.Write(res.Item2, 0, res.Item2.Length);
                    ServerSocketAsync._serverLog.Information("结束解码:" + res.Item2.Length);
                    // //yuv420转码jpg
                    // var jpgAvframe = videoConvert.Convert(res.Item1);
                    
                    // unsafe
                    // {
                    //     var jpgByte = new byte[jpgAvframe.width * jpgAvframe.height * 3];
                    //     using FileStream jpgfsw = new FileStream(dirPath + "\\" + DateTime.Now.Ticks + ".jpg", FileMode.Append, FileAccess.Write);
                    //     Marshal.Copy((IntPtr)jpgAvframe.data[0], jpgByte, 0, jpgByte.Length);
                    //     jpgfsw.Write(jpgByte, 0, jpgByte.Length);
                    // }
                    //tmpVideoMem.Dispose();
                    tmpVideoMem = new MemoryStream();
                }
            };
            server.Closed += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("关闭了连接：{0}", b.AcceptSocketId);
            };
            server.Error += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("发生错误({0})：{1} {2}", b.Errors, b.Exception.Message, b.Exception.StackTrace);
            };
            server.Start();
            ServerSocketAsync._serverLog.Information($"监听{port}");
            Console.Read();
            tmpVideoMem.Dispose();
        }
    }
}
