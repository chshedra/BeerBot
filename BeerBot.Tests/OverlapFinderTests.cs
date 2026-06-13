using BeerBot.Models;
using BeerBot.Services;
using Xunit;

namespace BeerBot.Tests;

public class OverlapFinderTests
{
    private readonly OverlapFinder _finder = new();

    [Fact]
    public void Returns_empty_when_no_members()
    {
        var result = _finder.FindBestSlots([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Returns_slot_when_single_member_has_availability()
    {
        var members = new List<UserAvailability>
        {
            new("Alice", [new TimeSlot { Day = "monday", StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0) }])
        };

        var result = _finder.FindBestSlots(members);
        Assert.NotEmpty(result);
        Assert.Equal("monday", result[0].Day);
    }

    [Fact]
    public void Prefers_evening_slots_over_morning_with_same_member_count()
    {
        var members = new List<UserAvailability>
        {
            new("Alice", [
                new TimeSlot { Day = "tuesday", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) },
                new TimeSlot { Day = "tuesday", StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(20, 0) }
            ])
        };

        var result = _finder.FindBestSlots(members);

        Assert.NotEmpty(result);
        // Evening block should score higher and appear first
        Assert.Equal("tuesday", result[0].Day);
        Assert.True(result[0].Start.Hour >= 18);
    }

    [Fact]
    public void Returns_at_most_three_slots()
    {
        var slots = new List<TimeSlot>
        {
            new() { Day = "monday", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) },
            new() { Day = "tuesday", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) },
            new() { Day = "wednesday", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) }
        };

        var members = new List<UserAvailability>
        {
            new("Alice", slots),
            new("Bob", slots)
        };

        var result = _finder.FindBestSlots(members);
        Assert.True(result.Count <= 3);
    }

    [Fact]
    public void Slot_with_more_members_scores_higher()
    {
        var sharedSlot = new TimeSlot { Day = "friday", StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(21, 0) };
        var aliceOnly = new TimeSlot { Day = "saturday", StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(21, 0) };

        var members = new List<UserAvailability>
        {
            new("Alice", [sharedSlot, aliceOnly]),
            new("Bob", [sharedSlot])
        };

        var result = _finder.FindBestSlots(members);
        Assert.Equal("friday", result[0].Day);
        Assert.Equal(2, result[0].MemberCount);
    }

    [Fact]
    public void Non_overlapping_windows_are_returned()
    {
        var slots = new List<TimeSlot>
        {
            new() { Day = "monday", StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(23, 0) }
        };
        var members = new List<UserAvailability> { new("Alice", slots) };

        var result = _finder.FindBestSlots(members);

        // No two results should be adjacent 30-min blocks on the same day
        for (var i = 0; i < result.Count - 1; i++)
        for (var j = i + 1; j < result.Count; j++)
        {
            if (result[i].Day != result[j].Day) continue;
            var diff = Math.Abs((result[i].Start - result[j].Start).TotalMinutes);
            Assert.True(diff > 30, $"Slots at {result[i].Start} and {result[j].Start} are too close");
        }
    }
}
