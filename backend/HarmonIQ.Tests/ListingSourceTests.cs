using HarmonIQ.Api.Services;
using Xunit;

namespace HarmonIQ.Tests;

/// <summary>
/// Where the scraper points. The production site bot-blocks an automated client (403), so a local
/// demo reads the same markup from a locally-served LDP instead. That makes the base URL config,
/// and it makes a self-signed dev certificate something the scraper has to accept — but only on
/// loopback, never for a public host.
/// </summary>
public class ListingSourceTests
{
    [Fact]
    public void BuildsTheListingUrlUnderTheConfiguredBase()
    {
        Assert.Equal(
            "https://localhost:44300/349246f/",
            ListingSource.UrlFor("https://localhost:44300", "349246f"));
    }

    [Fact]
    public void FallsBackToTheProductionSiteWhenNoBaseIsConfigured()
    {
        Assert.Equal(
            "https://www.apartments.com/349246f/",
            ListingSource.UrlFor(null, "349246f"));
    }

    [Fact]
    public void ToleratesATrailingSlashOnTheConfiguredBase()
    {
        Assert.Equal(
            "https://localhost:44300/349246f/",
            ListingSource.UrlFor("https://localhost:44300/", "349246f"));
    }

    [Fact]
    public void KeepsTheSlugFormOfAKeyThatCarriesOne()
    {
        Assert.Equal(
            "https://www.apartments.com/beck-at-wells-branch-austin-tx/n3cqt3m/",
            ListingSource.UrlFor(null, "beck-at-wells-branch-austin-tx~n3cqt3m"));
    }

    /// <summary>
    /// The LDP route is <c>{seoName}/{listingKey}</c>. Production canonicalizes a bare key onto it;
    /// a locally-served instance does not, and answers 404. The seo segment's value is not read —
    /// any placeholder 301s to the canonical slug — so a template is enough to address a listing
    /// without knowing its slug in advance.
    /// </summary>
    [Fact]
    public void PlacesTheKeyIntoAConfiguredPathTemplate()
    {
        Assert.Equal(
            "https://localhost:44300/_/349246f/",
            ListingSource.UrlFor("https://localhost:44300", "349246f", "_/{key}/"));
    }

    /// <summary>
    /// Photo hosts are environment-specific: production serves <c>images1.apartments.com</c>, the
    /// test environment behind a local LDP serves <c>imgtst1.apartments.com</c>. Matching only the
    /// production host means a locally-served listing scrapes to zero photos, which the pipeline
    /// reads as "no evidence" and skips — a silent no-op that looks like a scoring bug.
    /// </summary>
    [Fact]
    public void ReadsPhotosFromTheProductionImageHost()
    {
        Assert.Equal(
            ["https://images1.apartments.com/i2/abc/768x576.jpg"],
            ListingSource.PhotoUrls("<img src=\"https://images1.apartments.com/i2/abc/768x576.jpg\">"));
    }

    [Fact]
    public void ReadsPhotosFromTheTestEnvironmentImageHost()
    {
        Assert.Equal(
            ["https://imgtst1.apartments.com/i2/abc/768x576.jpg"],
            ListingSource.PhotoUrls("<img src=\"https://imgtst1.apartments.com/i2/abc/768x576.jpg\">"));
    }

    [Fact]
    public void IgnoresAJpegServedFromTheSiteItselfRatherThanAnImageHost()
    {
        Assert.Empty(ListingSource.PhotoUrls("<img src=\"https://www.apartments.com/logo.jpg\">"));
    }

    [Fact]
    public void ReportsEachPhotoOnceEvenWhenTheMarkupRepeatsIt()
    {
        var html =
            "<img src=\"https://imgtst1.apartments.com/i2/a/768x576.jpg\">" +
            "<img src=\"https://imgtst1.apartments.com/i2/a/768x576.jpg\">";

        Assert.Single(ListingSource.PhotoUrls(html));
    }

    /// <summary>
    /// A listing page publishes its own coordinates in schema.org JSON-LD. Geocoding the address
    /// string instead is both slower and lossier — the scraped street address can be partial
    /// ("3100 Martin"), and a geocode that returns nothing leaves the whole site lens unknown.
    /// </summary>
    [Fact]
    public void ReadsTheListingsPublishedCoordinates()
    {
        const string html =
            """{"@type":"GeoCoordinates","latitude":33.67253,"longitude":-117.85841}""";

        var point = ListingSource.Coordinates(html);

        Assert.NotNull(point);
        Assert.Equal(33.67253, point!.Latitude, 5);
        Assert.Equal(-117.85841, point.Longitude, 5);
    }

    [Fact]
    public void ReportsNoCoordinatesWhenThePagePublishesNone()
    {
        Assert.Null(ListingSource.Coordinates("<html><body>no structured data</body></html>"));
    }

    /// <summary>
    /// Map widgets and ad payloads carry latitude/longitude pairs that are not the subject's
    /// location. Only a <c>GeoCoordinates</c> block is the listing's own claim about where it is.
    /// </summary>
    [Fact]
    public void IgnoresALatLongPairThatIsNotAGeoCoordinatesBlock()
    {
        Assert.Null(ListingSource.Coordinates("""{"map":{"latitude":38.88,"longitude":-77.1}}"""));
    }

    [Fact]
    public void ParsesCoordinatesIndependentlyOfTheHostsDecimalSeparator()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A comma-decimal culture must not turn 33.67253 into 3367253.
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var point = ListingSource.Coordinates(
                """{"@type":"GeoCoordinates","latitude":33.67253,"longitude":-117.85841}""");

            Assert.Equal(33.67253, point!.Latitude, 5);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Plan drawings are sent to the model as-is, and a real property mixes PNG and JPEG plans.
    /// Declaring a media type that contradicts the bytes is a hard 400 from the API, so the type
    /// has to come from the bytes rather than from an assumption about the fixture.
    /// </summary>
    [Fact]
    public void DetectsPngFromItsSignature()
    {
        byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];

        Assert.Equal("image/png", ImageMediaType.Detect(png));
    }

    [Fact]
    public void DetectsJpegFromItsSignature()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0];

        Assert.Equal("image/jpeg", ImageMediaType.Detect(jpeg));
    }

    [Fact]
    public void FallsBackToPngForBytesItCannotIdentify()
    {
        Assert.Equal("image/png", ImageMediaType.Detect([1, 2, 3]));
    }

    /// <summary>
    /// Ten seconds is fine for a CDN-backed production page and too tight for a locally-served
    /// LDP, which can take that long to render cold — and a timeout there reads downstream as
    /// "listing unavailable", i.e. no evidence and a silently skipped subject.
    /// </summary>
    [Fact]
    public void UsesTheConfiguredFetchTimeout()
    {
        Assert.Equal(TimeSpan.FromSeconds(45), ListingSource.TimeoutFrom("45"));
    }

    [Fact]
    public void FallsBackToTheDefaultTimeoutWhenUnconfiguredOrUnparseable()
    {
        Assert.Equal(ListingSource.DefaultTimeout, ListingSource.TimeoutFrom(null));
        Assert.Equal(ListingSource.DefaultTimeout, ListingSource.TimeoutFrom("soon"));
    }

    [Fact]
    public void IgnoresANonPositiveTimeoutRatherThanCancellingEveryRequest()
    {
        Assert.Equal(ListingSource.DefaultTimeout, ListingSource.TimeoutFrom("0"));
    }

    [Fact]
    public void AcceptsADevCertificateOnLoopback()
    {
        Assert.True(ListingSource.AllowsDevCertificate(new Uri("https://localhost:44300/349246f/")));
    }

    [Fact]
    public void RefusesADevCertificateForAPublicHost()
    {
        Assert.False(ListingSource.AllowsDevCertificate(new Uri("https://www.apartments.com/349246f/")));
    }
}
