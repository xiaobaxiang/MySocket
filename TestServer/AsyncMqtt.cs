using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.IO;
using FFmpegAnalyzer;
using FFmpeg.AutoGen;
using System.Linq;
using System.Diagnostics;

namespace TestServer
{
    public static class AsyncMqtt
    {
        private static IMqttClient client;
        public static void UseMqttMessageReceive()
        {
            //处理mqtt消息
            Task.Factory.StartNew(ConnetToMqtt, TaskCreationOptions.LongRunning);
        }

        private static async Task ConnetToMqtt()
        {
            // 创建 MQTT 实例
            client = new MqttFactory().CreateMqttClient();

            // 创建 MQTT 客户端选项
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("172.26.255.91", 7083) // MQTT broker 地址 端口 emqx.zzcyi.cn 47.90.134.89:7083
                .WithClientId("ffmpeg_client")
                .WithCredentials("admin", "eLzuAJ@ghcZJkAD4m") // 设置账号密码 1ad6c09e eLzuAJ@ghcZJkAD4m
                .Build();

            var i = 0;
            client.DisconnectedAsync += async (arg) =>
            {
                Console.WriteLine("disconnect:{Reason}", arg.Reason);
                if (i > 0)
                {
                    await Task.Delay(3000);
                }
                i++;
                try
                {
                    Console.WriteLine($"mqtt断开连接后重连{i}次");
                    await client.ReconnectAsync();
                    Console.WriteLine($"reconnect");
                    //await subscribeTopic();
                    i = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("reconnect error," + ex.Message);
                }
                //while (!client.IsConnected)
                //{
                //    i++;
                //    await ReConnect();
                //    log.LogInformation($"mqtt断开连接后重连{i}次");
                //    Thread.Sleep(1000);
                //}
            };

            client.ApplicationMessageReceivedAsync += (arg) =>
            {
                return Task.Factory.StartNew(ParseProto, arg);
            };
            // 连接 MQTT broker
            var connectResult = await client.ConnectAsync(options);

            if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
            {
                Console.WriteLine($"MQTT connected");
                await subscribeTopic();
            }
            else
            {
                Console.WriteLine($"connect MQTT broker failed: {connectResult.ResultCode}");
            }
        }

        private async static Task subscribeTopic()
        {
            if (client == null || !client.IsConnected)
                return;
            // 订阅主题
            await client.SubscribeAsync(new MqttClientSubscribeOptions
            {
                TopicFilters = new List<MQTTnet.Packets.MqttTopicFilter> {
                            new MQTTnet.Packets.MqttTopicFilter{
                                Topic= "searchUserResult",
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                            },
                            new MQTTnet.Packets.MqttTopicFilter{
                                Topic= "VideoClip",
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                            },
                        }
            });
            Console.WriteLine("video clip - subscribe success");
        }

        /// <summary>
        /// 重新连接
        /// </summary>
        private static Task ReConnect()
        {
            //return client.ReconnectAsync();
            return ConnetToMqtt();
        }

        /// <summary>
        /// 解析接收到的消息
        /// </summary>
        /// <param name="_arg"></param>
        /// <returns></returns>
        private static async Task ParseProto(object _arg)
        {
            var arg = _arg as MqttApplicationMessageReceivedEventArgs;
            if (arg == null) return;
            var msg = Encoding.UTF8.GetString(arg.ApplicationMessage.PayloadSegment);
            Console.WriteLine($"received topic:{arg.ApplicationMessage.Topic} msg:\r\n" + msg);

            if (arg.ApplicationMessage.Topic == "searchUserResult")
            {
                var receive = JsonSerializer.Deserialize<PicItem>(msg, Program.JsonSerializerOptions);
                if (receive != null)
                {
                    if (Program.VideoInfoDic.TryGetValue(receive.Sn, out var videInfo) && videInfo != null)
                    {
                        videInfo.User = receive.User;
                        videInfo.PicItem.User = receive.User;
                    }
                }
            }
            else if (arg.ApplicationMessage.Topic == "VideoClip")
            {
                //await Task.Delay(10000);
                var receive = JsonSerializer.Deserialize<VideoClip>(msg, Program.JsonSerializerOptions);
                if (receive != null)
                {
                    if (Program.VideoInfoDic.TryGetValue(receive.Sn, out var videInfo) && videInfo != null)
                    {
                        var filename = $"{receive.Sn}-{receive.Time}.mp4";
                        var yuvfilename = $"{receive.Sn}-{receive.Time}.yuv";
                        var savePath = AppContext.BaseDirectory + "\\tmp\\" + filename;

                        //var filterResult = videInfo.DevFrameList.Filter(x => x.Time >= receive.Time - 10 * 1000, x => x.Time <= receive.Time + 10 * 1000);
                        var filterResult = videInfo.DevFrameList.Filter(x => x.Time >= receive.Time - 10 * 1000, x => x.Time <= receive.Time + 10 * 1000).ToList();
                        var frame = 0;

                        //var MP4Streamer = new MP4Streamer((int)(filterResult.Count / 10));
                        var MP4Streamer = new MP4Streamer(10);
                        MP4Streamer.Initialize(savePath);
                        var isGen = false;
                        foreach (var x in filterResult)
                        {
                            Console.WriteLine($"current frame -{frame++}-{x.Time}");
                            // using FileStream fsw = new FileStream(savePath, FileMode.Append, FileAccess.Write);
                            // fsw.Write(x.AVFrame, 0, x.AVFrame.Length);

                            MP4Streamer.Stream(x.AVFrame);
                            isGen = true;

                            // //存yuv视频
                            // var path = AppContext.BaseDirectory + "\\tmp\\" + $"{yuvfilename}";
                            // using FileStream fsw = new FileStream(path, FileMode.Append, FileAccess.Write);
                            // fsw.Write(x.AVBytes, 0, x.AVBytes.Length);
                        }
                        MP4Streamer.Dispose();
                        if (isGen)
                        {
                            filename = reBuildMp4(filename);
                            //filename = reBuildYUV2Mp4(yuvfilename);
                        }
                        receive.File = "http://video.acesmarttech.com/tmp/" + filename;
                        await SendStrMsg("VideoClipResult", JsonSerializer.Serialize(receive, Program.JsonSerializerOptions));
                    }
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 重构mp4文件
        /// </summary>
        /// <param name="file"></param>
        private static string reBuildMp4(string file)
        {
            var result = "N-" + file;
            Process p = new Process
            {
                StartInfo =
                {
                    FileName = AppContext.BaseDirectory+"\\ffmpeg\\bin\\ffmpeg.exe",
                    Arguments = " -i "+file+" -r 10 "+ result,
                    WorkingDirectory =  AppContext.BaseDirectory+"\\tmp\\",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            var reader = p.StandardError;
            while (!reader.EndOfStream)
            {
                Console.WriteLine(reader.ReadLine());
            }

            var reader1 = p.StandardOutput;
            while (!reader1.EndOfStream)
            {
                Console.WriteLine(reader1.ReadLine());
            }
            p.WaitForExit();
            return result;
        }


        /// <summary>
        /// yuv转mp4文件
        /// </summary>
        /// <param name="file"></param>
        private static string reBuildYUV2Mp4(string file)
        {
            //var result = "N-" + file.Replace(".yuv", ".mp4");
            var result = file.Replace(".yuv", ".mp4");
            Process p = new Process
            {
                StartInfo =
                {
                    FileName = AppContext.BaseDirectory+"\\ffmpeg\\bin\\ffmpeg.exe",
                    Arguments = " -pix_fmt yuv420p -s 640x480 -i "+file+" -r 10 -c:v libx264 -pix_fmt yuv420p "+ result,
                    WorkingDirectory =  AppContext.BaseDirectory+"\\tmp\\",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            p.Start();
            var reader = p.StandardError;
            while (!reader.EndOfStream)
            {
                Console.WriteLine(reader.ReadLine());
            }

            var reader1 = p.StandardOutput;
            while (!reader1.EndOfStream)
            {
                Console.WriteLine(reader1.ReadLine());
            }
            p.WaitForExit();
            return result;
        }


        /// <summary>
        /// 发送字符消息
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="topic"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static async Task SendStrMsg(string clientId, string topic, string msg)
        {
            if (client.IsConnected)
            {
                await client.PublishStringAsync(topic + "/" + clientId, msg, MqttQualityOfServiceLevel.AtMostOnce, false);
                Console.WriteLine("publish success");
            }
            else
            {
                Console.WriteLine("client not connected");
            }
        }

        /// <summary>
        /// 发送字符消息
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static async Task SendStrMsg(string topic, string msg)
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.PublishStringAsync(topic, msg, MqttQualityOfServiceLevel.AtMostOnce, false);
                    Console.WriteLine("publish success");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("publish error:" + ex.Message);
                }
            }
            else
            {
                Console.WriteLine("client not connected");
            }
        }

        /// <summary>
        /// 发送字节消息
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="topic"></param>
        /// <param name="msg"></param>
        /// <returns></returns>

        public static async Task SendByteMsg(string clientId, string topic, byte[] msg)
        {
            if (client.IsConnected)
            {
                await client.PublishAsync(new MqttApplicationMessage
                {
                    Topic = topic + "/" + clientId,
                    PayloadSegment = msg,
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                    Retain = false,
                });
                Console.WriteLine("publish success");
            }
            else
            {
                Console.WriteLine("client not connected");
            }
        }

    }

}
