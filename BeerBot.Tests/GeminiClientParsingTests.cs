using System.Text.Json;
using BeerBot.Models;
using Xunit;

namespace BeerBot.Tests;

// Tests for the JSON parsing logic extracted from GeminiClient
// (static helper methods tested independently without HTTP calls)
public class GeminiClientParsingTests
{
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

    private static List<TimeSlot> ParseSlots(string json)
    {
        var stripped = StripMarkdownFences(json);
        try
        {
            var raw = JsonSerializer.Deserialize<List<RawSlot>>(stripped,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

            return raw.Select(s => new TimeSlot
            {
                Day = s.Day.ToLowerInvariant(),
                StartTime = TimeOnly.Parse(s.StartTime),
                EndTime = TimeOnly.Parse(s.EndTime)
            }).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    [Fact]
    public void Parses_clean_json_array()
    {
        var json = """[{"day":"monday","startTime":"18:00","endTime":"20:00"}]""";
        var slots = ParseSlots(json);

        Assert.Single(slots);
        Assert.Equal("monday", slots[0].Day);
        Assert.Equal(new TimeOnly(18, 0), slots[0].StartTime);
        Assert.Equal(new TimeOnly(20, 0), slots[0].EndTime);
    }

    [Fact]
    public void Parses_multiple_slots()
    {
        var json = """
            [
              {"day":"tuesday","startTime":"09:00","endTime":"12:00"},
              {"day":"thursday","startTime":"19:00","endTime":"22:00"}
            ]
            """;
        var slots = ParseSlots(json);
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void Strips_json_markdown_fence()
    {
        var fenced = "```json\n[{\"day\":\"friday\",\"startTime\":\"17:00\",\"endTime\":\"19:00\"}]\n```";
        var slots = ParseSlots(fenced);

        Assert.Single(slots);
        Assert.Equal("friday", slots[0].Day);
    }

    [Fact]
    public void Strips_plain_code_fence()
    {
        var fenced = "```\n[{\"day\":\"saturday\",\"startTime\":\"10:00\",\"endTime\":\"12:00\"}]\n```";
        var slots = ParseSlots(fenced);

        Assert.Single(slots);
        Assert.Equal("saturday", slots[0].Day);
    }

    [Fact]
    public void Returns_empty_list_on_malformed_json()
    {
        var slots = ParseSlots("this is not json at all");
        Assert.Empty(slots);
    }

    [Fact]
    public void Returns_empty_list_on_empty_array()
    {
        var slots = ParseSlots("[]");
        Assert.Empty(slots);
    }

    [Fact]
    public void Day_is_lowercased()
    {
        var json = """[{"day":"WEDNESDAY","startTime":"08:00","endTime":"09:00"}]""";
        var slots = ParseSlots(json);
        Assert.Equal("wednesday", slots[0].Day);
    }

    private record RawSlot(string Day, string StartTime, string EndTime);
}
