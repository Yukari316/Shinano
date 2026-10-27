namespace Shinano.Host.Configuration;

public static class ConfigurationBindingExtensions
{
    public static T Bind<T>(this WebApplicationBuilder builder, string section) where T : new() =>
        builder.Configuration.GetSection(section).Get<T>() ?? new T();
}
