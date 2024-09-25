using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ServiceSelf;
using Serilog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TestServer
{
    public class Program
    {
        public static ILoggerFactory LoggerFactory { get; set; }
        static async Task Main(string[] args)
        {
            //设置工作目录位程序发布目录
            Environment.CurrentDirectory = Directory.GetCurrentDirectory();
            var configuration = new ConfigurationManager();
            var builder = Host.CreateDefaultBuilder(args);

            var serviceName = configuration["AppSettings:ProjectNo"] ?? "";
            var serviceOptions = new ServiceOptions
            {
                Description = configuration["AppSettings:ProjectName"] ?? "",
            };
            if (args.Length > 0)
            {
                serviceOptions.Arguments = new List<Argument>();
                for (var i = 0; i < args.Length; i++)
                {
                    serviceOptions.Arguments.Append(new Argument(args[i]));
                }
            }
            serviceOptions.WorkingDirectory = Environment.CurrentDirectory;
            serviceOptions.Linux.Service.Restart = "always";
            serviceOptions.Linux.Service.RestartSec = "10";
            serviceOptions.Windows.DisplayName = serviceOptions.Description;
            serviceOptions.Windows.FailureActionType = WindowsServiceActionType.Restart;

            if (Service.UseServiceSelf(args, serviceName, serviceOptions))
            {
                builder.UseSerilog((ctx, cnf) => cnf.ReadFrom.Configuration(ctx.Configuration));//注册Serilog
                builder.UseServiceSelf();

                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<MqttTask>();
                    services.AddHostedService<SocketService>();//接收抓拍图片 注册的是单例的程序
                    services.AddHostedService<MqttService>();//推送每秒三张图片 注册的是单例的程序
                });

                using var host = builder.Build();
                LoggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
                await host.RunAsync();
            }

        }
    }

}
