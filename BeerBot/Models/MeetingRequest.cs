namespace BeerBot.Models;

public enum MeetingRequestStatus { Open, Closed }

public class MeetingRequest
{
    public int Id { get; set; }
    public long GroupChatId { get; set; }
    public int InitiatorUserId { get; set; }
    public MeetingRequestStatus Status { get; set; } = MeetingRequestStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeadlineAt { get; set; }

    public ICollection<Availability> Availabilities { get; set; } = [];
}
