using BeerBot.Models;

namespace BeerBot.Services;

public record SuggestedSlot(string Day, TimeOnly Start, TimeOnly End, int MemberCount);

public class OverlapFinder
{
    private static readonly string[] Weekdays =
    [
        "monday",
        "tuesday",
        "wednesday",
        "thursday",
        "friday",
        "saturday",
        "sunday",
    ];

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

        Dictionary<(string Day, TimeOnly Block), double> scores = [];

        foreach (string day in Weekdays)
        {
            for (int minutes = 0; minutes <= LastBlockStartMinutes; minutes += BlockMinutes)
            {
                TimeOnly blockStart = TimeOnly.MinValue.AddMinutes(minutes);
                TimeOnly blockEnd = blockStart.AddMinutes(BlockMinutes);
                double score = 0;

                foreach (UserAvailability member in members)
                {
                    if (
                        member.Slots.Any(slot =>
                            slot.Day == day
                            && slot.StartTime <= blockStart
                            && slot.EndTime >= blockEnd
                        )
                    )
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

        var sorted = scores.OrderByDescending(kv => kv.Value).ToList();
        var results = new List<SuggestedSlot>();
        var used = new HashSet<(string, TimeOnly)>();

        foreach (var kv in sorted)
        {
            if (results.Count >= 3)
                break;

            var (day, block) = kv.Key;

            // Skip if adjacent block on same day already claimed (non-overlapping windows)
            if (
                used.Contains((day, block))
                || used.Contains((day, block.AddMinutes(-30)))
                || used.Contains((day, block.AddMinutes(30)))
            )
                continue;

            var memberCount = members.Count(m =>
                m.Slots.Any(s =>
                    s.Day == day && s.StartTime <= block && s.EndTime >= block.AddMinutes(30)
                )
            );

            results.Add(new SuggestedSlot(day, block, block.AddMinutes(30), memberCount));
            used.Add((day, block));
        }

        return results;
    }
}
