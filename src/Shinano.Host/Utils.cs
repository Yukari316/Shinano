namespace Shinano.Host;

public static class Utils
{
    public static T Bind<T>(this WebApplicationBuilder builder, string section) where T : new() =>
        builder.Configuration.GetSection(section).Get<T>() ?? new T();
}
