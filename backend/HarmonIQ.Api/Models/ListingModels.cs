namespace HarmonIQ.Api.Models;

public record ListingPhoto(
    string PhotoId, string ThumbnailUrl, string? Caption,
    bool Interior, bool Selected, string? SuggestedRoomType);

public record ListingNumbers(string? UnitNumber, int? Floor, string? StreetNumber);

public record SideEnvironment(string Road, string Water, string Structures, string Slope)
{
    public static readonly SideEnvironment Unknown = new("unknown", "unknown", "unknown", "unknown");
}

public record ListingEnvironment(
    SideEnvironment North, SideEnvironment East, SideEnvironment South, SideEnvironment West)
{
    public static readonly ListingEnvironment AllUnknown =
        new(SideEnvironment.Unknown, SideEnvironment.Unknown, SideEnvironment.Unknown, SideEnvironment.Unknown);
    public SideEnvironment Side(string dir) => dir switch
    {
        "north" => North, "east" => East, "south" => South, "west" => West,
        _ => SideEnvironment.Unknown,
    };
}

public record ListingResponse(
    string ListingId, string Title, string Address, string Url,
    IReadOnlyList<ListingPhoto> Photos, ListingNumbers Numbers, ListingEnvironment Environment);

public record PhotoBytes(byte[] Data, string ContentType);

public class ListingNotFoundException(string message) : Exception(message);
public class ListingSourceException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// A single unit row scraped from a plan card's availability table (including rows behind a
/// "Show More Units" expander — the scraper reads raw markup, not rendered/visible DOM state).
/// </summary>
public record ScrapedUnit(string UnitNumber, int? Floor, int? Sqft, decimal? Price);

/// <summary>
/// One plan card scraped from a multi-plan LDP. <see cref="RentalKey"/> is the stable machine
/// key (<c>data-rentalkey</c>) and the primary identity — never the display name
/// (<see cref="ModelName"/>), which real LDPs repeat across distinct plans. An empty
/// <see cref="RentalKey"/> means the card had no <c>data-rentalkey</c> attribute; callers fall
/// back to the perceptual image hash + beds/baths content signature (design §5), and an
/// ambiguous fallback match writes no row rather than risk a wrong grade.
/// </summary>
public record ScrapedPlan(
    string RentalKey, string ModelName, string? AttachmentId, string? PlanImageUrl,
    int? Beds, double? Baths, int? SqftMin, int? SqftMax, IReadOnlyList<ScrapedUnit> Units);
