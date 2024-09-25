using System.Text;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Protocol;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace TestServer
{
    public class MqttTask
    {
        private ILogger _log { get; }
        private IConfiguration _configuration { get; }
        private IMqttClient client;
        public MqttTask(ILogger<MqttTask> logger, IConfiguration configuration)
        {
            _log = logger;
            _configuration = configuration;
        }

        public async Task ConnetToMqtt()
        {
            // 创建 MQTT 实例
            client = new MqttFactory().CreateMqttClient();

            // 创建 MQTT 客户端选项
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_configuration.GetValue<string>("AppSettings:MqttServer"), _configuration.GetValue<int>("AppSettings:MqttPort"))
                .WithClientId(_configuration.GetValue<string>("AppSettings:MqttClientId"))
                .WithCredentials(_configuration.GetValue<string>("AppSettings:MqttUserName"), _configuration.GetValue<string>("AppSettings:MqttPassword"))
                .Build();

            var i = 0;
            client.DisconnectedAsync += async (arg) =>
            {
                _log.LogInformation("disconnect:{Reason}", arg.Reason);
                if (i > 0)
                {
                    await Task.Delay(3000);
                }
                i++;
                try
                {
                    _log.LogInformation($"mqtt断开连接后重连{i}次");
                    await client.ReconnectAsync();
                    _log.LogInformation("reconnect success");
                    await subscribeTopic();
                    i = 0;
                }
                catch (Exception ex)
                {
                    _log.LogInformation("reconnect error," + ex.Message);
                }
            };

            client.ApplicationMessageReceivedAsync += (arg) =>
            {
                return Task.Factory.StartNew(ParseProto, arg);
            };
            _log.LogInformation($"connect to MQTT");
            // 连接 MQTT broker
            var connectResult = await client.ConnectAsync(options);

            if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
            {
                _log.LogInformation($"MQTT connected");
                await subscribeTopic();
            }
            else
            {
                _log.LogInformation($"connect MQTT broker failed: {connectResult.ResultCode}");
            }
        }

        private async Task subscribeTopic()
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
        /// 解析接收到的消息
        /// </summary>
        /// <param name="_arg"></param>
        /// <returns></returns>
        private async Task ParseProto(object _arg)
        {
            var arg = _arg as MqttApplicationMessageReceivedEventArgs;
            if (arg == null) return;
            var msg = Encoding.UTF8.GetString(arg.ApplicationMessage.PayloadSegment);
            _log.LogInformation($"received topic:{arg.ApplicationMessage.Topic} msg:" + msg);

            if (arg.ApplicationMessage.Topic == "searchUserResult")
            {
                var receive = JsonSerializer.Deserialize<PicItem>(msg, SocketService.JsonSerializerOptions);
                if (receive != null)
                {
                    if (SocketService.VideoInfoDic.TryGetValue(receive.Sn, out var videInfo) && videInfo != null)
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
                var receive = JsonSerializer.Deserialize<VideoClip>(msg, SocketService.JsonSerializerOptions);
                if (receive != null)
                {
                    _log.LogInformation("不支持视频裁剪");
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
        public async Task SendStrMsg(string clientId, string topic, string msg)
        {
            if (client != null && client.IsConnected)
            {
                await client.PublishStringAsync(topic + "/" + clientId, msg, MqttQualityOfServiceLevel.AtMostOnce, false);
                _log.LogInformation("publish success");
            }
            else
            {
                _log.LogInformation("client not connected");
            }
        }

        /// <summary>
        /// 发送字符消息
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public async Task SendStrMsg(string topic, string msg)
        {
            if (client != null && client.IsConnected)
            {
                try
                {
                    await client.PublishStringAsync(topic, msg, MqttQualityOfServiceLevel.AtMostOnce, false);
                    _log.LogInformation("publish success");
                }
                catch (Exception ex)
                {
                    _log.LogInformation("publish error:" + ex.Message);
                }
            }
            else
            {
                _log.LogInformation("client not connected");
            }
        }

        /// <summary>
        /// 发送字节消息
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="topic"></param>
        /// <param name="msg"></param>
        /// <returns></returns>
        public async Task SendByteMsg(string clientId, string topic, byte[] msg)
        {
            if (client != null && client.IsConnected)
            {
                await client.PublishAsync(new MqttApplicationMessage
                {
                    Topic = topic + "/" + clientId,
                    PayloadSegment = msg,
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                    Retain = false,
                });
                _log.LogInformation("publish success");
            }
            else
            {
                _log.LogInformation("client not connected");
            }
        }
    }

}
