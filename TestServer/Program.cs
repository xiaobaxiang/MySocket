using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using FFmpegAnalyzer;
using static OpenCvSharp.XImgProc.CvXImgProc;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Encodings.Web;


namespace TestServer
{
    class PicItem
    {
        public long TimeTicket { get; set; }

        /// <summary>
        /// 0-300毫秒图片
        /// </summary>
        public string Pic1 { get; set; }
        /// <summary>
        /// 300-600毫秒图片
        /// </summary>
        public string Pic2 { get; set; }
        /// <summary>
        /// 600-900毫秒图片
        /// </summary>
        public string Pic3 { get; set; }
    }
    class Program
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
        private static Timer timerSendPic;
        private static ConcurrentDictionary<int, PicItem> jpegDic = new ConcurrentDictionary<int, PicItem>();
        private static ConcurrentDictionary<int, MemoryStream> videoStreamDic = new ConcurrentDictionary<int, MemoryStream>();
        //本地
        //public const string rtmpUrl = "rtmp://192.168.1.6/live/";
        //远程
        public const string rtmpUrl = "rtmp://47.92.37.224/live/";
        static void Main(string[] args)
        {

            //存放RTMP推送通道 key是SN, value是Streamer
            Dictionary<String, RtmpStreamer> rtmpPusher = new Dictionary<String, RtmpStreamer>();

            var port = 9100;
            var server = new ServerSocketAsync(port); //监听0.0.0.0:19990
            FFmpegWrapper.RegisterFFmpeg();
            var decoderWrapper = new FFmpegWrapper();
            var size = new System.Drawing.Size(640, 480);
            var videoConvert = new VideoFrameConverter(size, AVPixelFormat.AV_PIX_FMT_YUV420P, size, AVPixelFormat.AV_PIX_FMT_BGR24);
            decoderWrapper.CreateDecoder(size, false);
            List<Byte[]> bytes = new List<byte[]>();

            var timeTicket = DateTime.Now.Ticks;
            AsyncMqtt.UseMqttMessageReceive();//注册mqtt连接
            timerSendPic = new Timer(async (object state) => await SendPic(null), null, 900, 1000);//1秒执行一次

            server.Accepted += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("新连接：{0}", b.Accepts);
            };
            server.Receive += (a, b) =>
            {
                //ServerSocketAsync._serverLog.Information("{3} 接收到了{2}消息{0}：{1}", b.AcceptSocket.Id, b.Messager, b.AcceptSocket.TcpClient.Client.RemoteEndPoint, Thread.CurrentThread.ManagedThreadId);
                //b.AcceptSocket.Write(b.Messager);
                videoStreamDic.TryGetValue(b.AcceptSocket.Id, out MemoryStream tmpVideoMem);
                if (tmpVideoMem == null)
                {
                    tmpVideoMem = new MemoryStream();
                    videoStreamDic.TryAdd(b.AcceptSocket.Id, tmpVideoMem);
                }
                tmpVideoMem.Write(b.Messager.PicData, 0, b.Messager.PicData.Length);
                if (b.Messager.EOF == 1)
                {
                    ServerSocketAsync._serverLog.Information("开始解码:" + tmpVideoMem.Length);
                    var dirPath = AppContext.BaseDirectory + "\\tmp";
                    var res = decoderWrapper.DecodeFrames(tmpVideoMem.ToArray());

                    //视频不用存了
                    //using FileStream fsw = new FileStream(dirPath +"/"+ timeTicket + ".yuv", FileMode.Append, FileAccess.Write);
                    //fsw.Write(res.item2.ToArray(), 0, tmpVideoMem.ToArray().Length);
                    ServerSocketAsync._serverLog.Information("结束解码:" + res.Item2.Length);

                    //存个图，应该转Base64发MQTT
                    using var jpegStream = videoConvert.SaveJpg(res.Item1, timeTicket.ToString(), dirPath);
                    if (!jpegDic.TryGetValue(b.AcceptSocket.Id, out PicItem picItem))
                    {
                        picItem = new PicItem { TimeTicket = TimeToken };
                        jpegDic.TryAdd(b.AcceptSocket.Id, picItem);
                    }
                    if (DateTime.Now.Millisecond < 300)
                    {
                        //picItem.Pic1 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                        picItem.Pic1 = Convert.ToBase64String(jpegStream.ToArray());
                    }
                    else if (DateTime.Now.Millisecond < 600)
                    {
                        //picItem.Pic2 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                        picItem.Pic2 = Convert.ToBase64String(jpegStream.ToArray());
                    }
                    else if (DateTime.Now.Millisecond < 900)
                    {
                        //picItem.Pic3 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                        picItem.Pic3 = Convert.ToBase64String(jpegStream.ToArray());
                    }
                    picItem.TimeTicket = TimeToken;

                    //推流
                    if (rtmpPusher.ContainsKey(b.Messager.Sn))
                    {
                        rtmpPusher[b.Messager.Sn].Stream(res.Item1);
                    }
                    else
                    {
                        var streamer = new RtmpStreamer();
                        streamer.Initialize(rtmpUrl + b.Messager.Sn);
                        rtmpPusher.Add(b.Messager.Sn, streamer);
                        streamer.Stream(res.Item1);
                    }

                    //tmpVideoMem.Dispose();
                    var newtmpVideoMem = new MemoryStream();
                    videoStreamDic.TryUpdate(b.AcceptSocket.Id, newtmpVideoMem, tmpVideoMem);
                }

            };
            server.Closed += (a, b) =>
            {
                if (b.Accepts > 0 && videoStreamDic.TryGetValue(b.AcceptSocketId, out var tmpVideoMem))
                {
                    tmpVideoMem.Dispose();
                    videoStreamDic.TryRemove(b.AcceptSocketId, out _);
                }
                ServerSocketAsync._serverLog.Information("关闭了连接：{0}", b.AcceptSocketId);
            };
            server.Error += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("发生错误({0})：{1} {2}", b.Errors, b.Exception.Message, b.Exception.StackTrace);
            };
            server.Start();
            ServerSocketAsync._serverLog.Information($"监听{port}");
            Console.Read();
        }


        private static async Task SendPic(object state)
        {
            foreach (var key in jpegDic.Keys)
            {
                if (jpegDic.TryGetValue(key, out var picItem))
                {
                    await AsyncMqtt.SendStrMsg("sendPic", JsonSerializer.Serialize(picItem, JsonSerializerOptions));
                }
                jpegDic.TryRemove(key, out var data);
            }
        }
        //static int count = -1;
        // static unsafe void publish(byte[] data)
        // {
        //     // Initialize FFmpeg library

        //     ffmpeg.avformat_network_init();
        //     string rtmpUrl = "rtmp://127.0.0.1/live/sg";
        //     // Open RTMP output
        //     var outputFormatContext = ffmpeg.avformat_alloc_context();
        //     ffmpeg.avformat_alloc_output_context2(&outputFormatContext, null, "flv", rtmpUrl);
        //     if (outputFormatContext == null)
        //     {
        //         Console.WriteLine("Failed to allocate output context.");
        //         return;
        //     }

        //     var outputFormat = outputFormatContext->oformat;
        //     var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
        //     var videoStream = ffmpeg.avformat_new_stream(outputFormatContext, codec);
        //     if (videoStream == null)
        //     {
        //         Console.WriteLine("Failed to create new video stream.");
        //         return;
        //     }

        //     videoStream->codecpar->codec_id = AVCodecID.AV_CODEC_ID_H264;
        //     videoStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        //     videoStream->codecpar->width = 640;
        //     videoStream->codecpar->height = 480;
        //     videoStream->codecpar->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        //     videoStream->time_base = new AVRational { num = 1, den = 25 }; // 设置帧率
        //     if ((outputFormat->flags & ffmpeg.AVFMT_NOFILE) == 0)
        //     {
        //         AVIOContext* pb;
        //         if (ffmpeg.avio_open(&pb, rtmpUrl, ffmpeg.AVIO_FLAG_WRITE) < 0)
        //         {
        //             Console.WriteLine("Failed to open output file.");
        //             return;
        //         }
        //         outputFormatContext->pb = pb;
        //     }
        //     AVDictionary* aVDictionary = null;
        //     ffmpeg.av_dict_set(&aVDictionary, "flvflags", "no_duration_filesize", 0);
        //     if (ffmpeg.avformat_write_header(outputFormatContext, &aVDictionary) < 0)
        //     {
        //         Console.WriteLine("Error occurred when writing output file header.");
        //         return;
        //     }

        //     // Simulate receiving byte[] frame data (replace this with your actual data receiving logic)
        //     byte[] frameData = data;

        //     // Write frame data
        //     var packet = ffmpeg.av_packet_alloc();
        //     ffmpeg.av_init_packet(packet);

        //     packet->pts = count++;
        //     packet->dts = packet->pts;
        //     packet->stream_index = 0;
        //     fixed (byte* waitDecodeData = frameData)
        //     {
        //         packet->data = waitDecodeData;
        //         packet->size = frameData.Length;

        //         ffmpeg.av_write_frame(outputFormatContext, packet);
        //         ffmpeg.av_packet_free(&packet);

        //         // Write trailer
        //         ffmpeg.av_write_trailer(outputFormatContext);

        //         // Close RTMP output
        //         ffmpeg.avio_closep(&outputFormatContext->pb);
        //         ffmpeg.avformat_free_context(outputFormatContext);
        //     }

        // }

        // // Simulated method to receive frame data (replace this with your actual data receiving logic)


        // static unsafe void publish(AVFrame frame)
        // {
        //     // 获取输入YUV参数
        //     int width = 640;
        //     int height = 480;
        //     AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;

        //     // 设置输出RTMP地址
        //     string rtmpUrl = "rtmp://47.92.37.224/live/sg";

        //     // 创建输出AVFormatContext
        //     AVFormatContext* outputFormatContext;
        //     ffmpeg.avformat_alloc_output_context2(&outputFormatContext, null, "flv", rtmpUrl);


        //     //// 获取视频流
        //     //var videoStream = outputFormatContext->streams[0];


        //     // 打开输出流
        //     if (ffmpeg.avio_open2(&outputFormatContext->pb, rtmpUrl, ffmpeg.AVIO_FLAG_WRITE, null, null) < 0)
        //     {
        //         Console.WriteLine("Failed to open output IO context.");
        //         return;
        //     }

        //     // 创建输出视频流
        //     AVStream* outputStream = ffmpeg.avformat_new_stream(outputFormatContext, null);
        //     if (outputStream == null)
        //     {
        //         Console.WriteLine("Failed to create output stream.");
        //         return;
        //     }

        //     // 设置视频流参数
        //     outputStream->codecpar->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        //     outputStream->codecpar->codec_id = AVCodecID.AV_CODEC_ID_H264;
        //     outputStream->codecpar->width = width;
        //     outputStream->codecpar->height = height;
        //     outputStream->codecpar->format = (int)pixelFormat;
        //     //outputStream->codecpar->bit_rate = 400000;
        //     outputStream->time_base = new AVRational { num = 1, den = 30 }; // 设置帧率

        //     // 写入输出文件头
        //     AVDictionary* aVDictionary = null;
        //     ffmpeg.av_dict_set(&aVDictionary, "flvflags", "no_duration_filesize", 0);
        //     if (ffmpeg.avformat_write_header(outputFormatContext, &aVDictionary) < 0)
        //     {
        //         Console.WriteLine("Error occurred when writing output file header.");
        //         return;
        //     }

        //     AVCodecContext* aVCodecContext = null;

        //     // 推送YUV数据到RTMP服务器
        //     // 创建 AVPacket
        //     AVPacket packet;
        //     ffmpeg.av_init_packet(&packet);
        //     packet.data = null;
        //     packet.size = 0;

        //     // 将 AVFrame 转换为 AVPacket
        //     int ret = ffmpeg.avcodec_send_frame(aVCodecContext, &frame);
        //     if (ret < 0)
        //     {
        //         Console.WriteLine("Error sending frame to codec context");
        //         return;
        //     }

        //     while (true)
        //     {
        //         // 从YUV数据流中读取数据并填充AVFrame
        //         // 这里需要实现读取YUV数据的逻辑

        //         // 发送YUV数据到输出流
        //         if (ffmpeg.av_interleaved_write_frame(outputFormatContext, &packet) < 0)
        //         {
        //             Console.WriteLine("Error occurred when sending YUV data to output stream.");
        //             break;
        //         }
        //     }

        //     // 写入输出文件尾
        //     ffmpeg.av_write_trailer(outputFormatContext);

        //     // 清理资源
        //     ffmpeg.avformat_free_context(outputFormatContext);
        // }



    }

}
