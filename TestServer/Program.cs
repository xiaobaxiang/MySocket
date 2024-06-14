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
using SkiaSharp;

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

        //存放RTMP推送通道 key是SN, value是Streamer
        public static Dictionary<String, RtmpStreamer> rtmpPusher = new Dictionary<String, RtmpStreamer>();

        //本地
        //public const string rtmpUrl = "rtmp://192.168.1.6/live/";
        //远程
        //public const string rtmpUrl = "rtmp://47.92.37.224/live/";
        public const string rtmpUrl = "rtmp://127.0.0.1/live/";
        static void Main(string[] args)
        {
            var port = 9100;
            var server = new ServerSocketAsync(port); //监听0.0.0.0:19990
            FFmpegWrapper.RegisterFFmpeg();
            var decoderWrapper = new FFmpegWrapper();
            var size = new SKImageInfo(640, 480);
            var videoConvert = new VideoFrameConverter(size, AVPixelFormat.AV_PIX_FMT_YUV420P, size, AVPixelFormat.AV_PIX_FMT_BGR24);
            decoderWrapper.CreateDecoder(size, false);

            var timeTicket = DateTime.Now.Ticks;
            AsyncMqtt.UseMqttMessageReceive();//注册mqtt连接
            timerSendPic = new Timer(async (object state) => await SendPic(null), null, 900, 1000);//1秒执行一次

            //var count = 1;
            // var filename = $"{TimeToken}.mp4";
            // var savePath = AppContext.BaseDirectory + "\\tmp\\" + filename;

            // using var MP4Streamer = new MP4Streamer(5);
            // MP4Streamer.Initialize(savePath);
            // var listAvFrame = new List<AVFrame>();
            server.Accepted += (a, b) =>
            {
                ServerSocketAsync._serverLog.Information("new connect" + b.Accepts + "-" + b.AcceptSocket.TcpClient.Client.RemoteEndPoint);
            };
            server.Receive += async (a, b) =>
            {
                //ServerSocketAsync._serverLog.Information("{3} receive {2} data packet {0}：{1}", b.AcceptSocket.Id, b.Messager, b.AcceptSocket.TcpClient.Client.RemoteEndPoint, Thread.CurrentThread.ManagedThreadId);
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
                    //ServerSocketAsync._serverLog.Information("开始解码:" + videoInfo.VideoStream.Length);
                    var dirPath = AppContext.BaseDirectory + "\\tmp";
                    try
                    {
                        var videoBytes = videoInfo.VideoStream.ToArray();//00 00 00 01
                        if (!(videoBytes[0] == 0x00 && videoBytes[1] == 0x00 && videoBytes[2] == 0x00 && videoBytes[3] == 0x01))
                        {
                            ServerSocketAsync._serverLog.Information("avframe uncomplete,frame content-" + ":" + BitConverter.ToString(videoBytes.Take(40).ToArray()) + " ...");
                            videoInfo.VideoStream = new MemoryStream();
                            return;
                        }
                        var res = decoderWrapper.DecodeFrames(videoBytes);
                        if (res == null) return;

                        //视频不用存了
                        //using FileStream fsw = new FileStream(dirPath +"/"+ timeTicket + ".yuv", FileMode.Append, FileAccess.Write);
                        //fsw.Write(res.item2.ToArray(), 0, tmpVideoMem.ToArray().Length);

                        //TODO 先屏蔽
                        //ServerSocketAsync._serverLog.Information("结束解码:" + res.Item2.Length);

                        //Console.WriteLine("decode 5");
                        //存个图，应该转Base64发MQTT
                        using var jpegStream = videoConvert.SaveJpg(res.Item1, timeTicket.ToString(), dirPath);
                        if (jpegStream == null) return;
                        //Console.WriteLine("decode 6");
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
                        //count++;
                        unsafe
                        {
                            //Console.WriteLine("decode 7");
                            //推流
                            if (rtmpPusher.ContainsKey(b.Messager.Sn))
                            {
                                var streamer = rtmpPusher[b.Messager.Sn];
                                var result = streamer.Stream(res.Item1);
                                if (result < 0)
                                {
                                    streamer.Dispose();
                                    streamer = new RtmpStreamer();
                                    streamer.Initialize(rtmpUrl + b.Messager.Sn);
                                    rtmpPusher[b.Messager.Sn] = streamer;
                                }
                            }
                            else
                            {
                                var streamer = new RtmpStreamer();
                                streamer.Initialize(rtmpUrl + b.Messager.Sn);
                                rtmpPusher.Add(b.Messager.Sn, streamer);
                                streamer.Stream(res.Item1);
                            }
                        }

                        // if (count < 50)
                        // {
                        //     listAvFrame.Add(DeepCopyFrame(res.Item1));

                        //     //listAvFrame.Add(DeepCopyFrame(res.Item1));
                        //     //MP4Streamer.Stream(res.Item1);
                        // }
                        // else if (count == 50)
                        // {
                        //     listAvFrame.ForEach(e =>
                        //     {
                        //         MP4Streamer.Stream(e);
                        //     });
                        //     MP4Streamer.Dispose();
                        // }

                        //tmpVideoMem.Dispose();
                        //videoInfo.DevFrameList.Add(new VideoFrame { Time = TimeToken, AVFrame = DeepCopyFrame(res.Item1), AVBytes = res.Item2 });
                        videoInfo.DevFrameList.Add(new VideoFrame { Time = TimeToken, AVFrame = DeepCopyFrame(res.Item1) });
                        //videoInfo.DevFrameList.Add(new VideoFrame { Time = TimeToken, AVFrame = res.Item1, AVBytes = res.Item2 });
                        videoInfo.VideoStream = new MemoryStream();
                    }
                    catch (SEHException ex)
                    {
                        ServerSocketAsync._serverLog.Error(ex, "decode error");
                    }
                    catch (Exception ex)
                    {
                        ServerSocketAsync._serverLog.Information(ex?.Message + "\r\n" + ex?.StackTrace);
                        ServerSocketAsync._serverLog.Error(ex, "error occurred");
                    }
                }

                // if (b.Messager.EOF == 1)
                // {
                //     ServerSocketAsync._serverLog.Information("data frame," + (count++));
                // }
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
            Console.Read();
        }

        public unsafe static AVFrame DeepCopyFrame(AVFrame srcFrame)
        {
            var avframe = new AVFrame
            {
                linesize = srcFrame.linesize,
                width = srcFrame.width,
                height = srcFrame.height,
                pts = srcFrame.pts,
                pkt_dts = srcFrame.pkt_dts,
                format = srcFrame.format,
            };
            // 复制实际的图像数据
            for (uint i = 0; srcFrame.data[i] != null && i < ffmpeg.AV_NUM_DATA_POINTERS; i++)
            {
                int dataSize = ffmpeg.av_image_get_linesize((AVPixelFormat)avframe.format, avframe.width, (int)i) * avframe.height;
                avframe.data[i] = (byte*)ffmpeg.av_malloc((ulong)dataSize);
                Buffer.MemoryCopy(srcFrame.data[i], avframe.data[i], dataSize, dataSize);
            }
            return avframe;
        }


        private static async Task SendPic(object state)
        {
            foreach (var key in VideoInfoDic.Keys)
            {
                if (VideoInfoDic.TryGetValue(key, out var videoInfo))
                {
                    if (videoInfo.PicItem?.Pic1 != null && videoInfo.PicItem?.Pic2 != null && videoInfo.PicItem?.Pic3 != null && videoInfo.User > 0)
                    {
                        videoInfo.PicItem.User = videoInfo.User;
                        await AsyncMqtt.SendStrMsg("sendPic", JsonSerializer.Serialize(videoInfo.PicItem, JsonSerializerOptions));
                        videoInfo.PicItem.Pic1 = null;
                        videoInfo.PicItem.Pic2 = null;
                        videoInfo.PicItem.Pic3 = null;
                    }

                    // if (videoInfo.DevFrameList?.Current?.Value.Time < TimeToken - 1000)
                    // {
                    //     //rtmpPusher[key].Stream(videoInfo.DevFrameList.Current.Value.AVFrame);
                    //     var streamer = rtmpPusher[key];
                    //     var result = streamer.Stream(videoInfo.DevFrameList.Current.Value.AVFrame);
                    //     if (result < 0)
                    //     {
                    //         streamer.Dispose();
                    //         streamer = new RtmpStreamer();
                    //         streamer.Initialize(rtmpUrl + key);
                    //         rtmpPusher[key] = streamer;
                    //     }
                    // }
                }

                Console.WriteLine("cach video info:" + key + "-" + videoInfo.DevFrameList.Count);
            }
        }

    }

}
