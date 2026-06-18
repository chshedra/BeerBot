namespace BeerBot.Models;

/// <summary>
/// In-memory DTO used by <c>OverlapFinder</c>. Not persisted (see <see cref="AvailabilitySlot"/>
/// for storage).
/// </summary>
public class TimeSlot
{
    /// <summary>The calendar date this slot covers.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Inclusive start of the slot.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Exclusive end of the slot.</summary>
    public TimeOnly EndTime { get; set; }
}
