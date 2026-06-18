namespace BeerBot.Models;

/// <summary>
/// Lifecycle state of a <see cref="MeetingRequest"/>.
/// </summary>
public enum MeetingRequestStatus
{
    /// <summary>The round is collecting availability.</summary>
    Open,

    /// <summary>The round has finished and a suggestion was posted.</summary>
    Closed,
}

/// <summary>
/// A single "beertime" round for one group: tracks who started it, when it was created,
/// and the deadline after which a suggestion is posted even without all replies.
/// </summary>
public class MeetingRequest
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Telegram chat id of the group this round belongs to.</summary>
    public long GroupChatId { get; set; }

    /// <summary>Id of the <see cref="User"/> who started the round.</summary>
    public int InitiatorUserId { get; set; }

    /// <summary>Current lifecycle state of the round.</summary>
    public MeetingRequestStatus Status { get; set; } = MeetingRequestStatus.Open;

    /// <summary>UTC timestamp when the round was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC deadline after which a suggestion is posted even if not everyone replied.</summary>
    public DateTime DeadlineAt { get; set; }

    /// <summary>All member responses gathered for this round.</summary>
    public ICollection<Availability> Availabilities { get; set; } = [];
}
