using Microsoft.Extensions.Primitives;

namespace Shinano.Host.Panel;

/// <summary>
/// 插件管理 API 的端点来源。插件在首次启用时才登记 API，晚于应用启动，因此由这里收集并通知路由表更新。
/// 每个插件的端点位于 <c>/api/plugin-api/{插件 ID}</c> 下，要求面板登录，插件不处于启用状态时返回 404。
/// </summary>
public sealed class PluginEndpointDataSource : EndpointDataSource, IEndpointRouteBuilder
{
    /// <summary>插件管理 API 的路径前缀，与宿主的插件管理接口分开，避免路由冲突。</summary>
    public const string PREFIX = "/api/plugin-api";

    private readonly Lock                     _lock        = new();
    private readonly List<EndpointDataSource> _dataSources = [];
    private          CancellationTokenSource  _changes     = new();

    /// <summary>创建端点来源。</summary>
    public PluginEndpointDataSource(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc />
    public ICollection<EndpointDataSource> DataSources => _dataSources;

    /// <inheritdoc />
    public override IReadOnlyList<Endpoint> Endpoints
    {
        get
        {
            lock (_lock)
            {
                return [.. _dataSources.SelectMany(source => source.Endpoints)];
            }
        }
    }

    /// <inheritdoc />
    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);

    /// <inheritdoc />
    public override IChangeToken GetChangeToken()
    {
        lock (_lock)
        {
            return new CancellationChangeToken(_changes.Token);
        }
    }

    /// <summary>登记一个插件的管理 API 并通知路由表更新。</summary>
    /// <param name="pluginId">插件 ID。</param>
    /// <param name="isActive">插件当前是否启用；为 false 时请求返回 404。</param>
    /// <param name="map">插件的端点登记。</param>
    public void Map(string pluginId, Func<bool> isActive, Action<IEndpointRouteBuilder> map)
    {
        CancellationTokenSource previous;
        lock (_lock)
        {
            RouteGroupBuilder group = this.MapGroup($"{PREFIX}/{pluginId}")
                                          .RequireAuthorization()
                                          .AddEndpointFilter(async (context, next) =>
                                              isActive() ? await next(context) : Results.NotFound());
            map(group);
            previous = _changes;
            _changes = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }
}
