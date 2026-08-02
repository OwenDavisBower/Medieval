/// <summary>Quest template / seed kind. New templates compose objectives rather than new tick branches.</summary>
public enum QuestType : byte
{
    None = 0,
    ClearCamp = 1,
    DeliverWood = 2,
    Escort = 3,
    /// <summary>Haul wood to a needy village, then report back home.</summary>
    TradeRun = 4,
    /// <summary>Clear a camp, then escort a survivor home.</summary>
    RescueSurvivor = 5
}

public enum QuestStatus : byte
{
    None = 0,
    Active = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>Composable objective kinds driven by <see cref="QuestService"/>.</summary>
public enum QuestObjectiveKind : byte
{
    None = 0,
    /// <summary>Kill N enemies within radius of a camp/point.</summary>
    KillNear = 1,
    /// <summary>Spend N wood at a settlement (manual turn-in).</summary>
    DeliverItem = 2,
    /// <summary>Keep an escort entity alive and bring them (and the player) to a point.</summary>
    EscortTo = 3,
    /// <summary>Player reaches a settlement and confirms (turn-in / report).</summary>
    ReportBack = 4
}
