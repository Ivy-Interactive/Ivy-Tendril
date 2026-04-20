using System.Text.Json;

namespace Ivy.Tendril.Services;

public class WeatherService(IHttpClientFactory httpClientFactory) : IWeatherService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private WeatherInfo? _cachedResult;
    private DateTime _lastFetchTime = DateTime.MinValue;

    public async Task<WeatherInfo?> GetWeatherAsync()
    {
        if (_cachedResult != null && DateTime.UtcNow - _lastFetchTime < CacheDuration)
            return _cachedResult;

        try
        {
            var weatherData = await FetchWeatherDataAsync();
            if (weatherData != null)
            {
                _cachedResult = weatherData;
                _lastFetchTime = DateTime.UtcNow;
            }
            return weatherData;
        }
        catch
        {
            // On error, return null (don't display weather on wallpaper)
            return null;
        }
    }

    private async Task<WeatherInfo?> FetchWeatherDataAsync()
    {
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);

        // wttr.in provides free weather data without API key
        // format=j1 returns JSON
        var response = await client.GetAsync("https://wttr.in/?format=j1");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;

        // Parse current condition
        if (!root.TryGetProperty("current_condition", out var currentArray) ||
            currentArray.GetArrayLength() == 0)
            return null;

        var current = currentArray[0];

        var tempF = current.GetProperty("temp_F").GetString();
        var condition = current.GetProperty("weatherDesc")[0].GetProperty("value").GetString();

        // Parse location from nearest_area
        var location = "Unknown";
        if (root.TryGetProperty("nearest_area", out var areaArray) && areaArray.GetArrayLength() > 0)
        {
            var area = areaArray[0];
            var cityName = area.GetProperty("areaName")[0].GetProperty("value").GetString();
            var region = area.GetProperty("region")[0].GetProperty("value").GetString();
            location = !string.IsNullOrEmpty(region) ? $"{cityName}, {region}" : cityName ?? "Unknown";
        }

        // Map weather condition to emoji icon
        var icon = MapWeatherIcon(condition?.ToLowerInvariant() ?? "");

        return new WeatherInfo(
            Temperature: $"{tempF}°F",
            Condition: condition ?? "Unknown",
            Location: location,
            LastUpdated: DateTime.UtcNow,
            Icon: icon);
    }

    private static string MapWeatherIcon(string condition)
    {
        return condition switch
        {
            var c when c.Contains("sunny") || c.Contains("clear") => "☀️",
            var c when c.Contains("partly cloudy") => "⛅",
            var c when c.Contains("cloudy") || c.Contains("overcast") => "☁️",
            var c when c.Contains("rain") || c.Contains("drizzle") => "🌧️",
            var c when c.Contains("thunder") || c.Contains("storm") => "⛈️",
            var c when c.Contains("snow") || c.Contains("sleet") => "❄️",
            var c when c.Contains("fog") || c.Contains("mist") => "🌫️",
            var c when c.Contains("wind") => "💨",
            _ => "🌤️"
        };
    }
}
