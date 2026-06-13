using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BeerBot.Models;

namespace BeerBot.Services;

public record UserAvailability(string UserName, List<TimeSlot> Slots);

public class OpenRouterClient
{
    private readonly HttpClient _http;
    private readonly ILogger<OpenRouterClient> _logger;
    private readonly string _model;
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public OpenRouterClient(
        IHttpClientFactory factory,
        IConfiguration config,
        ILogger<OpenRouterClient> logger
    )
    {
        _logger = logger;
        _model = config["OpenRouter:Model"] ?? "google/gemini-2.5-flash";
        _http = factory.CreateClient("openrouter");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            config["OpenRouter:ApiKey"]
        );
    }

    public async Task<List<TimeSlot>> ParseAvailabilityAsync(string rawText, DateTime today)
    {
        _logger.LogInformation("OpenRouter: ParseAvailability called");
        var systemPrompt =
            $"Today is {today:dddd, MMMM d, yyyy}. "
            + "Extract all time slots from the user message. "
            + "Return ONLY a valid JSON array with objects [{\"day\":\"monday\",\"startTime\":\"HH:mm\",\"endTime\":\"HH:mm\"}]. "
            + "Day must be lowercase English weekday name. Times in 24-hour HH:mm. No prose, no markdown.";

        try
        {
            var response = await CallAsync(
                systemPrompt,
                rawText,
                maxTokens: 512,
                temperature: 0.2f
            );
            var json = StripMarkdownFences(response);
            var slots = JsonSerializer.Deserialize<List<RawSlot>>(json, JsonOpts) ?? [];
            return slots
                .Select(s => new TimeSlot
                {
                    Day = s.Day.ToLowerInvariant(),
                    StartTime = TimeOnly.Parse(s.StartTime),
                    EndTime = TimeOnly.Parse(s.EndTime),
                })
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "OpenRouter: Failed to parse availability JSON");
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenRouter: ParseAvailability error");
            return [];
        }
    }

    public async Task<string> GenerateSuggestionAsync(List<UserAvailability> all)
    {
        _logger.LogInformation(
            "OpenRouter: GenerateSuggestion called for {Count} users",
            all.Count
        );
        var systemPrompt =
            "You are a friendly group-chat bot. Write a short casual Telegram message (3-5 lines) "
            + "suggesting the best 2-3 meeting windows based on the availability data. Use emojis. "
            + "Do not mention specific users by name, just the times.";

        var userContent = JsonSerializer.Serialize(all);

        try
        {
            return await CallAsync(systemPrompt, userContent, maxTokens: 256, temperature: 0.8f);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenRouter: GenerateSuggestion error");
            return "Here are the best times I found for everyone! Check the poll below. 🍺";
        }
    }

    private async Task<string> CallAsync(
        string systemPrompt,
        string userContent,
        int maxTokens,
        float temperature
    )
    {
        var body = new
        {
            model = _model,
            max_tokens = maxTokens,
            temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent },
            },
        };

        var json = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
            ?? string.Empty;
    }

    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewline >= 0 && lastFence > firstNewline)
                return trimmed[(firstNewline + 1)..lastFence].Trim();
        }
        return trimmed;
    }

    private record RawSlot(string Day, string StartTime, string EndTime);
}
