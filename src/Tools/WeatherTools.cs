using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;

namespace McpServer.Tools;

[McpServerToolType]
public class WeatherTools
{
    private const string NWS_API_BASE = "https://api.weather.gov";
    private static readonly HttpClient _httpClient = new HttpClient()
    {
        BaseAddress = new Uri(NWS_API_BASE)
    };

    static WeatherTools()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "McpServer-Weather/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/geo+json");
    }

    [McpServerTool, Description("Get weather alerts for a US state.")]
    public static async Task<string> GetAlerts(
        [Description("The US state to get alerts for.")] string state)
    {
        try
        {
            var jsonElement = await _httpClient.GetFromJsonAsync<JsonElement>($"/alerts/active/area/{state}");
            
            if (!jsonElement.TryGetProperty("features", out var featuresElement))
            {
                return "Unable to fetch alerts or no alerts found.";
            }

            var alerts = featuresElement.EnumerateArray();

            if (!alerts.Any())
            {
                return "No active alerts for this state.";
            }

            return string.Join("\n--\n", alerts.Select(alert =>
            {
                JsonElement properties = alert.GetProperty("properties");
                return $"""
                        Event: {properties.GetProperty("event").GetString()}
                        Area: {properties.GetProperty("areaDesc").GetString()}
                        Severity: {properties.GetProperty("severity").GetString()}
                        Description: {properties.GetProperty("description").GetString()}
                        Instruction: {TryGetString(properties, "instruction")}
                        """;
            }));
        }
        catch (Exception ex)
        {
            return $"Error fetching weather alerts: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get weather forecast for a location.")]
    public static async Task<string> GetForecast(
        [Description("Latitude of the location.")] double latitude,
        [Description("Longitude of the location.")] double longitude)
    {
        try
        {
            // First get the forecast grid endpoint
            var pointsData = await _httpClient.GetFromJsonAsync<JsonElement>($"/points/{latitude},{longitude}");
            
            if (!pointsData.TryGetProperty("properties", out var properties))
            {
                return "Unable to fetch forecast data for this location.";
            }

            // Get the forecast URL from the points response
            string forecastUrl = properties.GetProperty("forecast").GetString()!;
            
            // Make a request to the forecast URL
            var forecastData = await _httpClient.GetFromJsonAsync<JsonElement>(forecastUrl);
            
            if (!forecastData.TryGetProperty("properties", out var forecastProps) || 
                !forecastProps.TryGetProperty("periods", out var periodsElement))
            {
                return "Unable to fetch detailed forecast.";
            }

            var periods = periodsElement.EnumerateArray();

            // Format the periods into a readable forecast (limit to first 5 periods)
            return string.Join("\n---\n", periods.Take(5).Select(period => $"""
                    {period.GetProperty("name").GetString()}
                    Temperature: {period.GetProperty("temperature").GetInt32()}°{period.GetProperty("temperatureUnit").GetString()}
                    Wind: {period.GetProperty("windSpeed").GetString()} {period.GetProperty("windDirection").GetString()}
                    Forecast: {period.GetProperty("detailedForecast").GetString()}
                    """));
        }
        catch (Exception ex)
        {
            return $"Error fetching weather forecast: {ex.Message}";
        }
    }

    [McpServerTool, Description("Get aviation SIGMETs (Significant Meteorological Information) for the Continental United States.")]
    public static async Task<string> GetSigmets()
    {
        try
        {
            // Use Aviation Weather Center API for SIGMETs
            using var awcClient = new HttpClient();
            awcClient.DefaultRequestHeaders.Add("User-Agent", "McpServer-Weather/1.0");
            
            var response = await awcClient.GetAsync("https://aviationweather.gov/api/data/isigmet?format=geojson&loc=us");
            
            if (!response.IsSuccessStatusCode)
            {
                return "Unable to fetch SIGMET data from Aviation Weather Center.";
            }

            var content = await response.Content.ReadAsStringAsync();
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(content);
            
            if (!jsonElement.TryGetProperty("features", out var featuresElement))
            {
                return "No SIGMET data available.";
            }

            var sigmets = featuresElement.EnumerateArray();

            if (!sigmets.Any())
            {
                return "No active SIGMETs for the Continental United States.";
            }

            return string.Join("\n---\n", sigmets.Select(sigmet =>
            {
                JsonElement properties = sigmet.GetProperty("properties");
                return $"""
                        SIGMET: {TryGetString(properties, "sigmetId")}
                        FIR: {TryGetString(properties, "fir")}
                        Valid From: {TryGetString(properties, "validTimeFrom")}
                        Valid To: {TryGetString(properties, "validTimeTo")}
                        Phenomenon: {TryGetString(properties, "phenom")}
                        Raw Text: {TryGetString(properties, "rawSigmet")}
                        """;
            }));
        }
        catch (Exception ex)
        {
            return $"Error fetching SIGMETs: {ex.Message}";
        }
    }

    // Helper method to safely get string values from JsonElement
    private static string TryGetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && 
            property.ValueKind != JsonValueKind.Null)
        {
            return property.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}
