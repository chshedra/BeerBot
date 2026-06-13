namespace BeerBot.Models;

public class Availability
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RequestId { get; set; }
    public string RawText { get; set; } = string.Empty;
    public List<TimeSlot> ParsedSlotsJson { get; set; } = [];

    public User User { get; set; } = null!;
    public MeetingRequest MeetingRequest { get; set; } = null!;
}
