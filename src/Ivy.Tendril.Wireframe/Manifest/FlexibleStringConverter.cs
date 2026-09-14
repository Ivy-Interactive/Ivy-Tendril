using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ivy.Tendril.Wireframe.Manifest;

/// <summary>
/// Reads any JSON scalar as a string.
///
/// The manifest's enum members are usually strings, but not always -- WeekDay is
/// [0,1,2,...] and some prop defaults are numbers or booleans. Since everything here is
/// rendered as documentation text, coercing is both correct and simpler than modelling
/// three value shapes.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
