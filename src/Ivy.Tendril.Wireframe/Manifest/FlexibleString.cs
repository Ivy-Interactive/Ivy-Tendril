using System.Text.Json.Serialization;

namespace Ivy.Tendril.Wireframe.Manifest;

/// <summary>
/// A manifest scalar rendered as text. Wrapping it keeps the coercion in one place: the
/// manifest's enum members are usually strings but sometimes numbers (WeekDay is 0..6).
/// </summary>
[JsonConverter(typeof(FlexibleStringValueConverter))]
public readonly record struct FlexibleString(string Value)
{
    public override string ToString() => Value;
    public static implicit operator string(FlexibleString s) => s.Value;
}
