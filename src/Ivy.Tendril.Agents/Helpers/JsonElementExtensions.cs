using System.Text.Json;

namespace Ivy.Tendril.Agents.Helpers;

internal static class JsonElementExtensions
{
    public static int? TryGetInt32Defensive(this JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out var val) ? val : (int)Math.Round(element.GetDouble());
        }
        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var strVal))
        {
            return strVal;
        }
        return null;
    }

    public static long? TryGetInt64Defensive(this JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt64(out var val) ? val : (long)Math.Round(element.GetDouble());
        }
        if (element.ValueKind == JsonValueKind.String && long.TryParse(element.GetString(), out var strVal))
        {
            return strVal;
        }
        return null;
    }
}
