using System.Text.Json;
using System.Text.Json.Serialization;

namespace HarmonIQ.Api.Models;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
