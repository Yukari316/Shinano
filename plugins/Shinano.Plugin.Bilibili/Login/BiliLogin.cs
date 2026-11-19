using Microsoft.Extensions.Logging;
using QRCoder;

namespace Shinano.Plugin.Bilibili.Login;

/// <summary>扫码登录：生成二维码、查询扫码结果，成功后交给 <see cref="BiliSession" /> 写入浏览器。面板与私聊指令共用。线程安全。</summary>
public sealed class BiliLogin
{
    /// <summary>二维码的有效期（B站服务端约 180 秒后返回过期）。</summary>
    public static readonly TimeSpan QrLifetime = TimeSpan.FromSeconds(180);

    private const int QR_PIXELS_PER_MODULE = 10;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly BiliHttp           _http;
    private readonly BiliSession        _session;
    private readonly TimeProvider       _time;
    private readonly ILogger<BiliLogin> _logger;

    /// <summary>创建登录流程。</summary>
    public BiliLogin(BiliHttp http, BiliSession session, TimeProvider time, ILogger<BiliLogin> logger)
    {
        _http    = http;
        _session = session;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>生成新的二维码。</summary>
    /// <exception cref="BiliApiException">接口返回错误。</exception>
    /// <exception cref="HttpRequestException">网络错误。</exception>
    public async Task<BiliQrCode> StartAsync(CancellationToken cancellationToken)
    {
        (string url, string key) = await _http.QrGenerateAsync(cancellationToken);
        using QRCodeGenerator generator = new();
        using QRCodeData data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
        _logger.LogInformation("已生成 B站扫码登录二维码");
        return new BiliQrCode
        {
            Key = key,
            Png = new PngByteQRCode(data).GetGraphic(QR_PIXELS_PER_MODULE)
        };
    }

    /// <summary>查询一次扫码结果；扫码成功时写入浏览器并确认登录后才返回成功。出错时为失败。</summary>
    public async Task<BiliQrState> PollAsync(string key, CancellationToken cancellationToken)
    {
        BiliQrPoll poll;
        try
        {
            poll = await _http.QrPollAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "查询 B站扫码结果失败");
            return BiliQrState.Failed;
        }

        return poll.State == BiliQrState.Succeeded ? await _session.CompleteLoginAsync(poll, cancellationToken) : poll.State;
    }

    /// <summary>每 2 秒查询一次，直到成功、过期、失败或超过二维码有效期。</summary>
    public async Task<BiliQrState> WaitAsync(string key, CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = _time.GetUtcNow() + QrLifetime;
        while (_time.GetUtcNow() < deadline)
        {
            await Task.Delay(PollInterval, _time, cancellationToken);
            BiliQrState state = await PollAsync(key, cancellationToken);
            if (state is BiliQrState.Succeeded or BiliQrState.Expired or BiliQrState.Failed) return state;
        }

        return BiliQrState.Expired;
    }
}
