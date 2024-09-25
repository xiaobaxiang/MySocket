using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace TestServer
{
    public class MqttService : IHostedService
    {
        public MqttService(ILogger<MqttService> logger, MqttTask mqttTask)
        {
            _log = logger;
            _mqttTask = mqttTask;
        }
        private ILogger _log { get; }

        private readonly MqttTask _mqttTask;
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            //处理mqtt消息
            await Task.Factory.StartNew(_mqttTask.ConnetToMqtt, TaskCreationOptions.LongRunning);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
        }
    }

}
