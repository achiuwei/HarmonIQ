using System.Net;
using System.Text;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HarmonIQ.Tests;

/// <summary>
/// The site environment for a real (scraped) property. <see cref="IListingService"/> used to
/// answer null for every key that was not a built-in sample, which meant a scraped listing's
/// environment — the one thing the site lens scores from — never reached the input set, and every
/// side stayed unknown no matter how well the page's geo data resolved.
/// </summary>
public class ListingEnvironmentIngestionTests
{
    /// <summary>
    /// Deliberately NOT one of <see cref="SampleListingProvider"/>'s two keys.
    ///
    /// This test is about the scrape path, and <c>GetPropertyEnvironmentAsync</c> short-circuits
    /// the two fixture keys before reaching it — the local demo fixture resolves no geo by design,
    /// so that demo mode needs no network. Naming a fixture key here would silently test the
    /// fixture branch instead, and assert nothing about ingestion.
    /// </summary>
    private const string ScrapedPropertyKey = "9xk4m2p";

    private static readonly ListingEnvironment Resolved = new(
        new SideEnvironment("busy", "none", "taller-building", "level"),
        new SideEnvironment("quiet", "none", "similar", "rises"),
        new SideEnvironment("none", "river", "open", "falls"),
        new SideEnvironment("quiet", "none", "open", "level"));

    private const string Html = """
        <html><head><title>Enzo - 3100 Martin Irvine, CA 92612 | Apartments.com</title></head>
        <body>
        <script type="application/ld+json">
        {"@type":"PostalAddress","streetAddress":"3100 Martin","addressLocality":"Irvine","addressRegion":"CA"}
        {"@type":"GeoCoordinates","latitude":33.67253,"longitude":-117.85841}
        </script>
        <img alt="Bedroom at Enzo" src="https://imgtst1.apartments.com/i2/abc/768x576.jpg">
        </body></html>
        """;

    private sealed class StaticHtmlHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Html, Encoding.UTF8, "text/html"),
            });
    }

    private sealed class SingleHandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubGeoContextService : IGeoContextService
    {
        public GeoPoint? ReceivedPoint { get; private set; }

        public Task<ListingEnvironment> GetEnvironmentAsync(
            string listingId, string address, GeoPoint? point, CancellationToken ct)
        {
            ReceivedPoint = point;
            return Task.FromResult(Resolved);
        }
    }

    /// <summary>Walks up from the test binary to the repo root, then into the API project.</summary>
    private static string ApiContentRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "backend", "HarmonIQ.Api");
            if (Directory.Exists(Path.Combine(candidate, "Data"))) return candidate;
        }
        throw new DirectoryNotFoundException("Could not locate the HarmonIQ.Api content root from the test binary.");
    }

    private sealed class StubWebHostEnvironment(string contentRoot) : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(contentRoot, "wwwroot");
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
        public string ApplicationName { get; set; } = "HarmonIQ.Api";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRoot);
        public string ContentRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
    }

    private static (ListingService Service, StubGeoContextService Geo) Build()
    {
        var geo = new StubGeoContextService();
        var provider = new ServiceCollection()
            .AddSingleton<IGeoContextService>(geo)
            .BuildServiceProvider();

        var service = new ListingService(
            new SampleListingProvider(new StubWebHostEnvironment(ApiContentRoot())),
            new MemoryCache(new MemoryCacheOptions()),
            new SingleHandlerFactory(new StaticHtmlHandler()),
            provider,
            new ConfigurationBuilder().Build(),
            NullLogger<ListingService>.Instance);

        return (service, geo);
    }

    [Fact]
    public async Task ResolvesTheEnvironmentOfARealPropertyRatherThanAnsweringNull()
    {
        var (service, _) = Build();

        var environment = await service.GetPropertyEnvironmentAsync(ScrapedPropertyKey, CancellationToken.None);

        Assert.Equal(Resolved, environment);
    }

    [Fact]
    public async Task HandsThePagesPublishedCoordinatesToTheGeoLookup()
    {
        var (service, geo) = Build();

        await service.GetPropertyEnvironmentAsync(ScrapedPropertyKey, CancellationToken.None);

        Assert.Equal(new GeoPoint(33.67253, -117.85841), geo.ReceivedPoint);
    }
}
