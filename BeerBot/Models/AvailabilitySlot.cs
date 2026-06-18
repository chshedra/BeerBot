namespace BeerBot.Models;

/// <summary>
/// One concrete (date, hour) block a user selected via the button wizard.
/// Toggling a button in the wizard adds or deletes one of these rows.
/// </summary>
public class AvailabilitySlot
{
    /// <summary>Primary key.</summary>
    public int Id { get; set; }

    /// <summary>Foreign key to the owning <see cref="Availability"/>.</summary>
    public int AvailabilityId { get; set; }

    /// <summary>The calendar date of this block.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Inclusive start of the hour block.</summary>
    public TimeOnly Start { get; set; }

    /// <summary>Exclusive end of the hour block (one hour after <see cref="Start"/>).</summary>
    public TimeOnly End { get; set; }

    /// <summary>Navigation to the owning availability.</summary>
    public Availability Availability { get; set; } = null!;
}
