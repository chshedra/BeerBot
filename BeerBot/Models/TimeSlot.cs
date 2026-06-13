namespace BeerBot.Models;

public class TimeSlot
{
    public string Day { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
