using BeerBot.Models;
using BeerBot.Services;
using Xunit;

namespace BeerBot.Tests;

public class OverlapFinderTests
{
    private readonly OverlapFinder _finder = new();

    private static readonly DateOnly Mon = new(2026, 6, 15);
    private static readonly DateOnly Tue = new(2026, 6, 16);
    private static readonly DateOnly Wed = new(2026, 6, 17);
    private static readonly DateOnly Fri = new(2026, 6, 19);
    private static readonly DateOnly Sat = new(2026, 6, 20);

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
            new("Alice", [new TimeSlot { Day = Mon, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(20, 0) }])
        };

        var result = _finder.FindBestSlots(members);
        Assert.NotEmpty(result);
        Assert.Equal(Mon, result[0].Day);
    }

    [Fact]
    public void Prefers_evening_slots_over_morning_with_same_member_count()
    {
        var members = new List<UserAvailability>
        {
            new("Alice", [
                new TimeSlot { Day = Tue, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) },
                new TimeSlot { Day = Tue, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(20, 0) }
            ])
        };

        var result = _finder.FindBestSlots(members);

        Assert.NotEmpty(result);
        // Evening block should score higher and appear first
        Assert.Equal(Tue, result[0].Day);
        Assert.True(result[0].Start.Hour >= 18);
    }

    [Fact]
    public void Returns_at_most_three_slots()
    {
        var slots = new List<TimeSlot>
        {
            new() { Day = Mon, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) },
            new() { Day = Tue, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) },
            new() { Day = Wed, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(23, 0) }
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
        var sharedSlot = new TimeSlot { Day = Fri, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(21, 0) };
        var aliceOnly = new TimeSlot { Day = Sat, StartTime = new TimeOnly(19, 0), EndTime = new TimeOnly(21, 0) };

        var members = new List<UserAvailability>
        {
            new("Alice", [sharedSlot, aliceOnly]),
            new("Bob", [sharedSlot])
        };

        var result = _finder.FindBestSlots(members);
        Assert.Equal(Fri, result[0].Day);
        Assert.Equal(2, result[0].MemberCount);
    }

    [Fact]
    public void Non_overlapping_windows_are_returned()
    {
        var slots = new List<TimeSlot>
        {
            new() { Day = Mon, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(23, 0) }
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
