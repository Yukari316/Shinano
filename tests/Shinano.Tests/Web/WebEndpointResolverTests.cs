using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shinano.Host.Web;

namespace Shinano.Tests.Web;

public sealed class WebEndpointResolverTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("shinano-web-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("::1")]
    public void Loopback_WithoutCertificate_UsesHttp(string address)
    {
        WebEndpoint endpoint = WebEndpointResolver.Resolve(new WebOptions { ListenAddress = address }, _directory);

        Assert.True(IPAddress.IsLoopback(endpoint.Address));
        Assert.Null(endpoint.Certificate);
        Assert.Empty(endpoint.Problems);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("not-an-address")]
    public void WildcardOrInvalidAddress_FallsBackToLoopback(string address)
    {
        WebEndpoint endpoint = WebEndpointResolver.Resolve(new WebOptions { ListenAddress = address }, _directory);

        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.Null(endpoint.Certificate);
        Assert.NotEmpty(endpoint.Problems);
    }

    [Fact]
    public void NonLoopback_WithoutCertificate_FallsBackToLoopback()
    {
        WebEndpoint endpoint = WebEndpointResolver.Resolve(new WebOptions { ListenAddress = "10.7.21.5" }, _directory);

        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.NotEmpty(endpoint.Problems);
    }

    [Fact]
    public void NonLoopback_WithCertificate_UsesHttps()
    {
        const string PASSWORD = "test";
        string path = Path.Combine(_directory, "panel.pfx");
        using (RSA key = RSA.Create(2048))
        {
            CertificateRequest request = new("CN=shinano-test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using X509Certificate2 certificate =
                request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, PASSWORD));
        }

        WebEndpoint endpoint = WebEndpointResolver.Resolve(
            new WebOptions
            {
                ListenAddress       = "10.7.21.5",
                CertificatePath     = "panel.pfx",
                CertificatePassword = PASSWORD
            },
            _directory);

        Assert.Equal(IPAddress.Parse("10.7.21.5"), endpoint.Address);
        Assert.NotNull(endpoint.Certificate);
        Assert.Empty(endpoint.Problems);
    }

    [Fact]
    public void InvalidPort_UsesDefaultPort()
    {
        WebEndpoint endpoint = WebEndpointResolver.Resolve(new WebOptions { Port = 0 }, _directory);

        Assert.Equal(WebOptions.DEFAULT_PORT, endpoint.Port);
        Assert.NotEmpty(endpoint.Problems);
    }
}
