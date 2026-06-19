namespace KeywordGuard.Pro.Security;

public class BlockedItem
{
    public string Value { get; set; } = "";
    public bool IsAggressive { get; set; }

    public string AggressiveText => IsAggressive ? "[AGGRESSIV]" : "";
}

public class GuardConfig
{
    public List<string> Urls { get; set; } = new();
    public List<BlockedItem> Keywords { get; set; } = new();
    public DateTime? EndTime { get; set; }
    public string? PinHash { get; set; }
    public string? PinSalt { get; set; }

    public bool IsActive()
    {
        return EndTime.HasValue && EndTime.Value > DateTime.Now;
    }
}