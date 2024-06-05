using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FFmpeg.AutoGen;
using FFmpegAnalyzer;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Encodings.Web;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace TestServer
{

    public class Program
    {
        /// <summary>
        /// 获取时间戳
        /// </summary>
        public static long TimeToken => (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        public static readonly JsonSerializerOptions JsonSerializerOptions = new JsonSerializerOptions()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            //ReferenceHandler = ReferenceHandler.Preserve
            //IgnoreNullValues = true,
            //WriteIndented = true
        };
        private static Timer timerSendPic;
        public static ConcurrentDictionary<string, VideoInfo> VideoInfoDic = new ConcurrentDictionary<string, VideoInfo>();
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

            var timeTicket = DateTime.Now.Ticks;
            AsyncMqtt.UseMqttMessageReceive();//注册mqtt连接
            timerSendPic = new Timer(async (object state) => await SendPic(null), null, 900, 1000);//1秒执行一次

            server.Accepted += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("新连接：" + b.Accepts + "-" + b.AcceptSocket.TcpClient.Client.RemoteEndPoint);
            };
            server.Receive += async (a, b) =>
            {
                //ServerSocketAsync._serverLog.Information("{3} 接收到了{2}消息{0}：{1}", b.AcceptSocket.Id, b.Messager, b.AcceptSocket.TcpClient.Client.RemoteEndPoint, Thread.CurrentThread.ManagedThreadId);
                //b.AcceptSocket.Write(b.Messager);
                //VideoInfoDic.TryGetValue(b.AcceptSocket.Id, out VideoInfo videoInfo);
                VideoInfoDic.TryGetValue(b.Messager.Sn, out VideoInfo videoInfo);
                if (videoInfo == null)
                {
                    videoInfo = new VideoInfo
                    {
                        VideoStream = new MemoryStream()
                    };
                    //VideoInfoDic.TryAdd(b.AcceptSocket.Id, videoInfo);
                    VideoInfoDic.TryAdd(b.Messager.Sn, videoInfo);
                    await AsyncMqtt.SendStrMsg("searchUser", $"{{\"sn\":\"{b.Messager.Sn}\"}}");
                }
                else if (videoInfo.VideoStream == null)
                {
                    videoInfo.VideoStream = new MemoryStream();
                }
                videoInfo.VideoStream.Write(b.Messager.PicData, 0, b.Messager.PicData.Length);
                //ServerSocketAsync._serverLog.Information("图片帧-" + ":" + BitConverter.ToString(b.Messager.PicData.Take(40).ToArray()) + " ...");
                if (b.Messager.EOF == 1)
                {
                    //TODO 先屏蔽
                    ServerSocketAsync._serverLog.Information("开始解码:" + videoInfo.VideoStream.Length);
                    var dirPath = AppContext.BaseDirectory + "\\tmp";
                    try
                    {
                        var videoBytes = videoInfo.VideoStream.ToArray();//00 00 00 01
                        if (!(videoBytes[0] == 0x00 && videoBytes[1] == 0x00 && videoBytes[2] == 0x00 && videoBytes[3] == 0x01))
                        {
                            ServerSocketAsync._serverLog.Information("视频流不完整,帧内容-" + ":" + BitConverter.ToString(videoBytes.Take(40).ToArray()) + " ...");
                            videoInfo.VideoStream = new MemoryStream();
                            return;
                        }
                        var res = decoderWrapper.DecodeFrames(videoBytes);

                        //视频不用存了
                        //using FileStream fsw = new FileStream(dirPath +"/"+ timeTicket + ".yuv", FileMode.Append, FileAccess.Write);
                        //fsw.Write(res.item2.ToArray(), 0, tmpVideoMem.ToArray().Length);

                        //TODO 先屏蔽
                        ServerSocketAsync._serverLog.Information("结束解码:" + res.Item2.Length);

                        //存个图，应该转Base64发MQTT
                        using var jpegStream = videoConvert.SaveJpg(res.Item1, timeTicket.ToString(), dirPath);
                        if (videoInfo.PicItem == null)
                        {
                            videoInfo.PicItem = new PicItem { Time = TimeToken };
                        }
                        if (DateTime.Now.Millisecond < 300)
                        {
                            //picItem.Pic1 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                            videoInfo.PicItem.Pic1 = Convert.ToBase64String(jpegStream.ToArray());
                        }
                        else if (DateTime.Now.Millisecond < 600)
                        {
                            //picItem.Pic2 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                            videoInfo.PicItem.Pic2 = Convert.ToBase64String(jpegStream.ToArray());
                        }
                        else if (DateTime.Now.Millisecond < 900)
                        {
                            //picItem.Pic3 = "data:image/jpeg;base64," + Convert.ToBase64String(jpegStream.ToArray());
                            videoInfo.PicItem.Pic3 = Convert.ToBase64String(jpegStream.ToArray());
                        }
                        videoInfo.PicItem.Time = TimeToken;
                        videoInfo.PicItem.Sn = b.Messager.Sn;

                        unsafe
                        {
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
                        }


                        //tmpVideoMem.Dispose();
                        videoInfo.DevFrameList.Add(new VideoFrame { Time = TimeToken, AVFrame = res.Item1, AVBytes = res.Item2 });

                        videoInfo.VideoStream = new MemoryStream();
                    }
                    catch (SEHException ex)
                    {
                        ServerSocketAsync._serverLog.Error(ex, "解码异常");
                    }
                    catch (Exception ex)
                    {
                        ServerSocketAsync._serverLog.Error(ex, "出现异常");
                    }
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
                ServerSocketAsync._serverLog.Information("发生错误({0})：{1} {2}", b.Errors, b.Exception.Message, b.Exception.StackTrace);
            };
            server.Start();
            ServerSocketAsync._serverLog.Information($"监听{port}");
            Console.Read();
        }


        private static async Task SendPic(object state)
        {
            foreach (var key in VideoInfoDic.Keys)
            {
                if (VideoInfoDic.TryGetValue(key, out var videoInfo) && videoInfo.PicItem?.Pic1 != null && videoInfo.PicItem?.Pic2 != null && videoInfo.PicItem?.Pic3 != null && videoInfo.User > 0)
                {
                    await AsyncMqtt.SendStrMsg("sendPic", JsonSerializer.Serialize(videoInfo.PicItem, JsonSerializerOptions));
                    videoInfo.PicItem.Pic1 = null;
                    videoInfo.PicItem.Pic2 = null;
                    videoInfo.PicItem.Pic3 = null;
                }

                Console.WriteLine("缓存视频流信息:" + key + "-" + videoInfo.DevFrameList.Count);
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
