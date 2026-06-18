namespace BeerBot.Models;

/// <summary>
/// A single user's response to a <see cref="MeetingRequest"/>: the set of hour blocks they
/// selected, plus a flag marking whether they finished picking.
/// </summary>
public class Availability
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the <see cref="User"/> who owns this availability.</summary>
    public int UserId { get; set; }

    /// <summary>Foreign key to the <see cref="MeetingRequest"/> this availability answers.</summary>
    public int RequestId { get; set; }

    /// <summary>
    /// True once the user pressed "Done" in the wizard. Rows exist while the user is
    /// still toggling slots, so the all-replied check must filter on this flag.
    /// </summary>
    public bool Submitted { get; set; }

    /// <summary>Navigation to the owning user.</summary>
    public User User { get; set; } = null!;

    /// <summary>Navigation to the meeting request being answered.</summary>
    public MeetingRequest MeetingRequest { get; set; } = null!;

    /// <summary>The concrete hour blocks the user selected.</summary>
    public ICollection<AvailabilitySlot> Slots { get; set; } = [];
}
