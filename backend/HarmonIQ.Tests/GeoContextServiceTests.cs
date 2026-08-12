using System.Net;
using System.Text;
using HarmonIQ.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HarmonIQ.Tests;

/// <summary>
/// Where the site lens gets its point from. A listing page publishes its own coordinates; using
/// them skips a geocode that can fail outright on a partial street address — and when it fails,
/// every side of the environment stays unknown and the whole site lens contributes nothing.
/// </summary>
public class GeoContextServiceTests
{
    private const string GeocoderUrl = "https://geocoder.test/search";
    private const string OverpassUrl = "https://overpass.test/api";
    private const string ElevationUrl = "https://elevation.test/v1";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];
        public List<string> PostedBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            if (request.Content is not null)
            {
                PostedBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var json =
                url.StartsWith(GeocoderUrl, StringComparison.Ordinal) ? """[{"lat":"1.5","lon":"2.5"}]""" :
                url.StartsWith(ElevationUrl, StringComparison.Ordinal) ? """{"elevation":[10,10,10,10,10]}""" :
                """{"elements":[]}""";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (GeoContextService Service, RecordingHandler Handler) Build()
    {
        var handler = new RecordingHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Geo:GeocoderUrl"] = GeocoderUrl,
            ["Geo:OverpassUrl"] = OverpassUrl,
            ["Geo:ElevationUrl"] = ElevationUrl,
        }).Build();

        var service = new GeoContextService(
            new SingleHandlerFactory(handler),
            config,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GeoContextService>.Instance);

        return (service, handler);
    }

    [Fact]
    public async Task SkipsTheGeocoderWhenTheListingPublishedItsCoordinates()
    {
        var (service, handler) = Build();

        await service.GetEnvironmentAsync("listing-1", "3100 Martin, Irvine, CA", new GeoPoint(33.67253, -117.85841), CancellationToken.None);

        Assert.DoesNotContain(handler.RequestedUrls, u => u.StartsWith(GeocoderUrl, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AsksOverpassAboutThePublishedCoordinates()
    {
        var (service, handler) = Build();

        await service.GetEnvironmentAsync("listing-2", "3100 Martin, Irvine, CA", new GeoPoint(33.67253, -117.85841), CancellationToken.None);

        Assert.Contains(handler.PostedBodies, b => b.Contains("33.67253", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StillGeocodesWhenThePagePublishedNoCoordinates()
    {
        var (service, handler) = Build();

        await service.GetEnvironmentAsync("listing-3", "3100 Martin, Irvine, CA", null, CancellationToken.None);

        Assert.Contains(handler.RequestedUrls, u => u.StartsWith(GeocoderUrl, StringComparison.Ordinal));
    }
}
