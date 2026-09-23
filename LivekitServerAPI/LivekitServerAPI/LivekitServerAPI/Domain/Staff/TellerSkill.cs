namespace LivekitServerAPI.Domain.Staff;

/// <summary>Routing hint — "account-opening", "loans", "general". Composite key with TellerId.</summary>
public class TellerSkill
{
    public string TellerId { get; set; } = string.Empty;
    public string Skill { get; set; } = string.Empty;

    public Teller? Teller { get; set; }
}
