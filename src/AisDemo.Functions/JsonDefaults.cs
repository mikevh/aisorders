using System.Text.Json;

namespace AisDemo.Functions;

/// <summary>
/// One shared serializer for payloads no framework touches: Service Bus message
/// bodies, and the ItemsJson and PayloadJson columns.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="JsonSerializerDefaults.Web"/> already supplies camelCase naming,
/// case-insensitive reads, and number-from-string handling, so nothing is
/// overridden here. What matters is that options are passed at all: the
/// parameterless <c>JsonSerializer</c> overloads fall back to
/// <see cref="JsonSerializerOptions.Default"/>, which is PascalCase and
/// case-sensitive. Parsing a request body with those would bind the camelCase
/// contract in SPEC.md 5.2 to nulls, and every order would be rejected for a
/// missing customerId.
/// </para>
/// <para>
/// HTTP responses do not come through here. The functions return
/// <c>ObjectResult</c> via ASP.NET Core integration, so ASP.NET Core serializes
/// them with its own Web defaults - verified by setting the naming policy to
/// null and observing the response stay camelCase.
/// </para>
/// <para>
/// The single cached instance matters for its own sake: constructing
/// <see cref="JsonSerializerOptions"/> per call defeats the serializer's
/// metadata caching.
/// </para>
/// </remarks>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
