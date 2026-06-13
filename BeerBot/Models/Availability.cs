namespace BeerBot.Models;

public class Availability
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RequestId { get; set; }

    // True once the user pressed "Done" in the wizard. Rows exist while the user is
    // still toggling slots, so the all-replied check must filter on this flag.
    public bool Submitted { get; set; }

    public User User { get; set; } = null!;
    public MeetingRequest MeetingRequest { get; set; } = null!;
    public ICollection<AvailabilitySlot> Slots { get; set; } = [];
}
