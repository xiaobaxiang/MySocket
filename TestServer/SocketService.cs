using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TestServer;

public sealed class SocketService : IHostedService, IHostedLifecycleService
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly MqttTask _mqttTask;

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
    public static ConcurrentDictionary<string, VideoInfo> VideoInfoDic = new ConcurrentDictionary<string, VideoInfo>();

    public SocketService(
        ILogger<SocketService> logger,
        IConfiguration configuration,
        MqttTask mqttTask,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _configuration = configuration;
        _mqttTask = mqttTask;

        appLifetime.ApplicationStarted.Register(OnStarted);
        appLifetime.ApplicationStopping.Register(OnStopping);
        appLifetime.ApplicationStopped.Register(OnStopped);
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("1. StartingAsync has been called.");

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("2. StartAsync has been called.");

        return Task.CompletedTask;
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("3. StartedAsync has been called.");

        return Task.CompletedTask;
    }

    private void OnStarted()
    {
        _logger.LogInformation("4. OnStarted has been called.");

        var port = _configuration.GetValue<int>("AppSettings:ServerPort");
        var server = new ServerSocketAsync(port, _logger); //监听0.0.0.0:19990

        var timeTicket = DateTime.Now.Ticks;
        server.Accepted += (a, b) =>
        {
            _logger.LogInformation("new connect" + b.Accepts + "-" + b.AcceptSocket.TcpClient.Client.RemoteEndPoint);
        };
        server.Receive += async (a, b) =>
        {
            b.AcceptSocket.Write(new SocketMessager(b.Messager.TimeToken, 1, b.Messager.Sn, new byte[] { 0x00 }));
            _logger.LogInformation(b.Messager.ToString());
            VideoInfoDic.TryGetValue(b.Messager.Sn, out VideoInfo videoInfo);
            if (videoInfo == null)
            {
                videoInfo = new VideoInfo();
                VideoInfoDic.TryAdd(b.Messager.Sn, videoInfo);
                await _mqttTask.SendStrMsg("searchUser", $"{{\"sn\":\"{b.Messager.Sn}\"}}");
            }

            try
            {
                if (videoInfo.PicItem == null)
                {
                    videoInfo.PicItem = new PicItem { Time = b.Messager.TimeToken };
                }
                if (videoInfo.PicItem.Pic1 == null)
                {
                    videoInfo.PicItem.Pic1 = Convert.ToBase64String(b.Messager.PicData);
                    File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-1.jpg", b.Messager.PicData);
                }
                else if (videoInfo.PicItem.Pic2 == null)
                {
                    videoInfo.PicItem.Pic2 = Convert.ToBase64String(b.Messager.PicData);
                    File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-2.jpg", b.Messager.PicData);
                }
                else if (videoInfo.PicItem.Pic3 == null)
                {
                    videoInfo.PicItem.Pic3 = Convert.ToBase64String(b.Messager.PicData);
                    File.WriteAllBytes(AppContext.BaseDirectory + "/tmp/" + b.Messager.Sn + "-" + b.Messager.TimeToken + "-3.jpg", b.Messager.PicData);
                }
                else
                {
                    videoInfo.PicItem.Pic1 = null;
                    videoInfo.PicItem.Pic2 = null;
                    videoInfo.PicItem.Pic3 = null;
                }
                if (videoInfo.PicItem.Pic1 != null && videoInfo.PicItem.Pic2 != null && videoInfo.PicItem.Pic3 != null && videoInfo.PicItem.User > 0)
                {
                    //videoInfo.PicItem.Time = b.Messager.TimeToken;
                    videoInfo.PicItem.Time = TimeToken;//先取服务器时间
                    videoInfo.PicItem.Sn = b.Messager.Sn;
                    await _mqttTask.SendStrMsg("sendPic", JsonSerializer.Serialize(videoInfo.PicItem, JsonSerializerOptions));
                    videoInfo.PicItem.Pic1 = null;
                    videoInfo.PicItem.Pic2 = null;
                    videoInfo.PicItem.Pic3 = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex?.Message + "\r\n" + ex?.StackTrace);
                _logger.LogError(ex, "error occurred");
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
            _logger.LogInformation("error occurred ({0})：{1} {2}", b.Errors, b.Exception.Message, b.Exception.StackTrace);
        };
        server.Start();
        _logger.LogInformation($"listen {port}");
    }

    private void OnStopping()
    {
        _logger.LogInformation("5. OnStopping has been called.");
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("6. StoppingAsync has been called.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("7. StopAsync has been called.");

        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("8. StoppedAsync has been called.");

        return Task.CompletedTask;
    }

    private void OnStopped()
    {
        _logger.LogInformation("9. OnStopped has been called.");
    }

}