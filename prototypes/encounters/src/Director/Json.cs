using System.Text.Json;
using System.Text.Json.Serialization;

namespace Encounters.Director;

/// <summary>
/// One set of JSON options for every data file: comments allowed (the data files document
/// themselves), trailing commas forgiven, enums written as camelCase strings.
/// </summary>
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static T Parse<T>(string text, string what) where T : class =>
        JsonSerializer.Deserialize<T>(text, Options) ?? throw new System.FormatException($"{what}: empty document");
}
