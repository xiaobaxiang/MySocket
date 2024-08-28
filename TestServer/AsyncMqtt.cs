using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text.Json;

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
                .WithTcpServer("172.31.143.4", 7076) // MQTT broker 地址 端口 121.43.125.138 172.31.143.4 172.26.255.91 emqx.zzcyi.cn 47.90.134.89:7083
                .WithClientId("ffmpeg_client")
                .WithCredentials("admin", "eLzuAJ@ghcZJkAD4m") // 设置账号密码 eLzuAJ@ghcZJkAD4m 1ad6c09e eLzuAJ@ghcZJkAD4m
                .Build();

            var i = 0;
            client.DisconnectedAsync += async (arg) =>
            {
                ServerSocketAsync._serverLog.Information("disconnect:{Reason}", arg.Reason);
                if (i > 0)
                {
                    await Task.Delay(3000);
                }
                i++;
                try
                {
                    ServerSocketAsync._serverLog.Information($"mqtt断开连接后重连{i}次");
                    await client.ReconnectAsync();
                    ServerSocketAsync._serverLog.Information($"reconnect");
                    //await subscribeTopic();
                    i = 0;
                }
                catch (Exception ex)
                {
                    ServerSocketAsync._serverLog.Information("reconnect error," + ex.Message);
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
                ServerSocketAsync._serverLog.Information($"MQTT connected");
                await subscribeTopic();
            }
            else
            {
                ServerSocketAsync._serverLog.Information($"connect MQTT broker failed: {connectResult.ResultCode}");
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
                            // new MQTTnet.Packets.MqttTopicFilter{
                            //     Topic= "VideoClip",
                            //     QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                            // },
                        }
            });
            //Console.WriteLine("video clip - subscribe success");
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
            ServerSocketAsync._serverLog.Information($"received topic:{arg.ApplicationMessage.Topic} msg:" + msg);

            if (arg.ApplicationMessage.Topic == "searchUserResult")
            {
                var receive = JsonSerializer.Deserialize<PicItem>(msg, Program.JsonSerializerOptions);
                if (receive != null)
                {
                    if (Program.VideoInfoDic.TryGetValue(receive.Sn, out var videInfo) && videInfo != null)
                    {
                        //videInfo.User = receive.User;
                        videInfo.PicItem.User = receive.User;
                        videInfo.PicItem.Classify = receive.Classify;
                    }
                }
            }
            else if (arg.ApplicationMessage.Topic == "VideoClip")
            {
                //await Task.Delay(10000);
                var receive = JsonSerializer.Deserialize<VideoClip>(msg, Program.JsonSerializerOptions);
                if (receive != null)
                {
                    ServerSocketAsync._serverLog.Information("不支持视频裁剪");
                }
            }
            await Task.CompletedTask;
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
                ServerSocketAsync._serverLog.Information("publish success");
            }
            else
            {
                ServerSocketAsync._serverLog.Information("client not connected");
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
                    ServerSocketAsync._serverLog.Information("publish success");
                }
                catch (Exception ex)
                {
                    ServerSocketAsync._serverLog.Information("publish error:" + ex.Message);
                }
            }
            else
            {
                ServerSocketAsync._serverLog.Information("client not connected");
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
                ServerSocketAsync._serverLog.Information("publish success");
            }
            else
            {
                ServerSocketAsync._serverLog.Information("client not connected");
            }
        }

    }

}
