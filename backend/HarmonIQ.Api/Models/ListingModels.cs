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
