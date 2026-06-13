namespace BeerBot.Models;

// One concrete (date, hour) block a user selected via the button wizard.
// Toggling a button in the wizard adds or deletes one of these rows.
public class AvailabilitySlot
{
    public int Id { get; set; }
    public int AvailabilityId { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }

    public Availability Availability { get; set; } = null!;
}
