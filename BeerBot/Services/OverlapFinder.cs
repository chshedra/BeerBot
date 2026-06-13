using BeerBot.Models;

namespace BeerBot.Services;

public record UserAvailability(string UserName, List<TimeSlot> Slots);

public record SuggestedSlot(DateOnly Day, TimeOnly Start, TimeOnly End, int MemberCount);

public class OverlapFinder
{
    private static readonly TimeOnly EveningStart = new(18, 0);
    private static readonly TimeOnly EveningEnd = new(23, 0);

    private const int BlockMinutes = 30;

    // Last 30-min block starts at 23:00 (ends 23:30); starting at 23:30 would cross
    // midnight and TimeOnly wraps, so we stop here to keep all blocks within one day.
    private const int LastBlockStartMinutes = 23 * 60;

    public List<SuggestedSlot> FindBestSlots(List<UserAvailability> members)
    {
        if (members.Count == 0)
        {
            return [];
        }

        Dictionary<(DateOnly Day, TimeOnly Block), double> scores = [];

        // Only consider dates that at least one member actually offered.
        IEnumerable<DateOnly> days = members
            .SelectMany(m => m.Slots)
            .Select(s => s.Day)
            .Distinct()
            .OrderBy(d => d);

        foreach (DateOnly day in days)
        {
            for (int minutes = 0; minutes <= LastBlockStartMinutes; minutes += BlockMinutes)
            {
                TimeOnly blockStart = TimeOnly.MinValue.AddMinutes(minutes);
                TimeOnly blockEnd = blockStart.AddMinutes(BlockMinutes);
                double score = 0;

                foreach (UserAvailability member in members)
                {
                    if (IsFree(member, day, blockStart, blockEnd))
                    {
                        score += 1.0;
                        if (blockStart >= EveningStart && blockStart < EveningEnd)
                        {
                            score += 0.5;
                        }
                    }
                }

                if (score > 0)
                {
                    scores[(day, blockStart)] = score;
                }
            }
        }

        List<KeyValuePair<(DateOnly Day, TimeOnly Block), double>> sorted = scores
            .OrderByDescending(kv => kv.Value)
            .ToList();
        List<SuggestedSlot> results = [];
        HashSet<(DateOnly, TimeOnly)> used = [];

        foreach (KeyValuePair<(DateOnly Day, TimeOnly Block), double> entry in sorted)
        {
            if (results.Count >= 3)
            {
                break;
            }

            (DateOnly day, TimeOnly block) = entry.Key;
            TimeOnly blockEnd = block.AddMinutes(BlockMinutes);

            // Skip if adjacent block on same day already claimed (non-overlapping windows)
            if (
                used.Contains((day, block))
                || used.Contains((day, block.AddMinutes(-BlockMinutes)))
                || used.Contains((day, blockEnd))
            )
            {
                continue;
            }

            int memberCount = members.Count(m => IsFree(m, day, block, blockEnd));

            results.Add(new SuggestedSlot(day, block, blockEnd, memberCount));
            used.Add((day, block));
        }

        return results;
    }

    /// <summary>
    /// Whether the member is free during the specified time block.
    /// </summary>
    private static bool IsFree(
        UserAvailability member,
        DateOnly day,
        TimeOnly blockStart,
        TimeOnly blockEnd
    ) =>
        member.Slots.Any(slot =>
            slot.Day == day && slot.StartTime <= blockStart && slot.EndTime >= blockEnd
        );
}
