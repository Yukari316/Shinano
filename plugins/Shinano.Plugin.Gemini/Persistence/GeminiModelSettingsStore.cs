using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>Gemini 模型与思考深度存储：位于全局 schema，启动读入内存，保存即时生效。线程安全。</summary>
public sealed class GeminiModelSettingsStore
{
    private readonly IPluginInitializationContext      _context;
    private readonly ILogger<GeminiModelSettingsStore> _logger;
    private readonly SemaphoreSlim                     _saving = new(1, 1);
    private volatile GeminiModelSettingsRecord         _current = new();

    /// <summary>创建存储。</summary>
    public GeminiModelSettingsStore(IPluginInitializationContext context, ILogger<GeminiModelSettingsStore> logger)
    {
        _context = context;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>当前生效的模型与思考深度配置。</summary>
    public GeminiModelSettingsRecord Current => _current;

    /// <summary>创建缺失的表并加载现有设置。</summary>
    public async Task LoadAsync()
    {
        Database.CodeFirst.InitTables<GeminiModelSettingsRecord>();
        GeminiModelSettingsRecord? existing = await Database.Queryable<GeminiModelSettingsRecord>()
                                                            .FirstAsync(record => record.Id == GeminiModelSettingsRecord.SINGLETON_ID);
        _current = existing ?? new GeminiModelSettingsRecord();
    }

    /// <summary>保存新的模型与思考深度配置并立即生效。</summary>
    public async Task SaveAsync(string model, string effort)
    {
        GeminiModelSettingsRecord record = new()
        {
            Id     = GeminiModelSettingsRecord.SINGLETON_ID,
            Model  = model,
            Effort = effort
        };
        await _saving.WaitAsync();
        try
        {
            await Database.Storageable(record).ExecuteCommandAsync();
            _current = record;
        }
        finally
        {
            _saving.Release();
        }

        _logger.LogInformation("面板修改 Gemini 模型配置：model={Model}，effort={Effort}", model, effort.Length == 0 ? "（禁用）" : effort);
    }
}
