using UnityEngine;

public class FollowerController : CombatSeekControllerBase
{
    [Header("Formation")]
    [Tooltip("Horizontal distance from the leader beyond which followers stop chasing and move back. 0 = no limit.")]
    [SerializeField] float maxDistanceFromLeader = 25f;

    /// <summary>Assigns a random orbit around the player; call once after spawn.</summary>
    public void Initialize()
    {
        EnsureComponentsInitialized();
        Motor.InitializeOrbitRandom();
        TryAssignPlayerAnchor();
    }

    void Start()
    {
        TryAssignPlayerAnchor();
        ApplySeekHoldDistanceFromRole();
        ApplyMotorSpeedFromCharacter();
    }

    void TryAssignPlayerAnchor()
    {
        var p = PlayerReference.TryGetTransform();
        if (p != null)
            Motor.AnchorTarget = p;
    }

    protected override bool BeforeSeekCombat()
    {
        if (maxDistanceFromLeader <= 0f)
            return true;

        Transform anchor = Motor.AnchorTarget;
        if (anchor != null &&
            SpatialMath.FlatSqrDistance(transform.position, anchor.position) >
            maxDistanceFromLeader * maxDistanceFromLeader)
            return false;

        return true;
    }

    protected override Transform FindCombatTarget()
    {
        float aggroSq = AggroRadiusSqr;
        Transform best = null;
        float bestSq = float.MaxValue;

        BanditController[] bandits = CombatUnitRegistry.GetBandits();
        for (int i = 0; i < bandits.Length; i++)
        {
            BanditController b = bandits[i];
            if (b == null)
                continue;
            Transform bt = b.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, bt.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(bt))
            {
                best = bt;
                bestSq = sq;
            }
        }

        return best;
    }
}
