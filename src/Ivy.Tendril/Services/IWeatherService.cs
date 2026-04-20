namespace Ivy.Tendril.Services;

public interface IWeatherService
{
    Task<WeatherInfo?> GetWeatherAsync();
}

public record WeatherInfo(
    string Temperature,
    string Condition,
    string Location,
    DateTime LastUpdated,
    string Icon);
