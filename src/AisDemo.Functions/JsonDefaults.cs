using System.Text.Json;
using System.Text.Json.Serialization;

namespace AisDemo.Functions;

/// <summary>
/// One serializer configuration, used both by the Functions worker for HTTP
/// bodies and by the messaging layer for queue and topic payloads — so what a
/// caller sends, what travels on the bus, and what lands in AuditLog all share
/// the same shape.
/// </summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
