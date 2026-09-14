using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ivy.Tendril.Wireframe.Manifest;

/// <summary>Reads any JSON scalar into a <see cref="FlexibleString"/>.</summary>
public sealed class FlexibleStringValueConverter : JsonConverter<FlexibleString>
{
    private static readonly FlexibleStringConverter Inner = new();

    public override FlexibleString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(Inner.Read(ref reader, typeof(string), options) ?? "");

    public override void Write(Utf8JsonWriter writer, FlexibleString value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}
