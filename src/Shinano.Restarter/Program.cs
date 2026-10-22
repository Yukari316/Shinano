using System.Text.Json;
using Shinano.Restarter;

int requestIndex = Array.IndexOf(args, RestarterRequest.REQUEST_ARGUMENT);
if (requestIndex < 0 || requestIndex + 1 >= args.Length)
{
    Console.Error.WriteLine($"用法：Shinano.Restarter {RestarterRequest.REQUEST_ARGUMENT} <请求文件>。由宿主在面板重启时启动，不需要手动运行。");
    return 2;
}

RestarterRequest? request;
try
{
    request = JsonSerializer.Deserialize<RestarterRequest>(
        File.ReadAllText(args[requestIndex + 1]),
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"无法读取重启请求：{exception.Message}");
    return 2;
}

if (request is null)
{
    Console.Error.WriteLine("重启请求为空");
    return 2;
}

using RestartLog log = new(request.LogDirectory);
try
{
    bool ready = await new RestartHelper(request, log).RunAsync();
    return ready ? 0 : 1;
}
catch (Exception exception)
{
    log.Error($"重启失败：{exception}");
    return 1;
}
