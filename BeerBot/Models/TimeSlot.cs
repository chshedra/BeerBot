namespace BeerBot.Models;

// In-memory DTO used by OverlapFinder. Not persisted (see AvailabilitySlot for storage).
public class TimeSlot
{
    public DateOnly Day { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
