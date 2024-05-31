using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Client;
using MQTTnet;
using MQTTnet.Protocol;

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
                .WithTcpServer("47.90.134.89", 7083) // MQTT broker 地址 端口 emqx.zzcyi.cn 47.90.134.89:7083
                .WithClientId("ffmpeg_client")
                .WithCredentials("admin", "eLzuAJ@ghcZJkAD4m") // 设置账号密码 1ad6c09e eLzuAJ@ghcZJkAD4m
                .Build();

            var i = 0;
            client.DisconnectedAsync += async (arg) =>
            {
                Console.WriteLine("断开连接:{Reason}", arg.Reason);
                if (i > 0)
                {
                    await Task.Delay(3000);
                }
                i++;
                try
                {
                    Console.WriteLine($"mqtt断开连接后重连{i}次");
                    await client.ReconnectAsync();
                    Console.WriteLine($"重连成功");
                    //await subscribeTopic();
                    i = 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("重连出现异常," + ex.Message);
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
                Console.WriteLine($"连接成功");
                //await subscribeTopic();
            }
            else
            {
                Console.WriteLine($"连接 MQTT broker 失败: {connectResult.ResultCode}");
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
                                Topic= "$SYS/brokers/+/clients/+/connected",
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                            },
                            new MQTTnet.Packets.MqttTopicFilter{
                                Topic= "$SYS/brokers/+/clients/+/disconnected",
                                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtMostOnce,
                            },
                        }
            });
            Console.WriteLine("接收硬件上线消息-订阅成功");
            Console.WriteLine("接收硬件下线消息-订阅成功");
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
            Console.WriteLine($"接收到主题{arg.ApplicationMessage.Topic}的消息:\r\n" + msg);

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
                Console.WriteLine($"发布成功");
            }
            else
            {
                Console.WriteLine("客户端未连接");
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
                await client.PublishStringAsync(topic, msg, MqttQualityOfServiceLevel.AtMostOnce, false);
                Console.WriteLine($"发布成功");
            }
            else
            {
                Console.WriteLine("客户端未连接");
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
                Console.WriteLine($"发布成功");
            }
            else
            {
                Console.WriteLine("客户端未连接");
            }
        }

    }

}
