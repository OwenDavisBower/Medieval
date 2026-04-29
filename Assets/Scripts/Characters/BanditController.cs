using UnityEngine;

public class BanditController : CombatSeekControllerBase
{
    Transform _player;
    bool _initialized;

    public void Initialize(Transform campAnchor)
    {
        EnsureComponentsInitialized();
        Motor.Mode = TargetSteeringMovementMode.WanderAroundTarget;
        Motor.InitializeWanderAroundAnchor(campAnchor);
        _initialized = true;
    }

    void Start()
    {
        if (!_initialized)
            Motor.InitializeWanderAroundAnchor(transform);

        _player = PlayerReference.TryGetTransform();

        ApplySeekHoldDistanceFromRole();
        ApplyMotorSpeedFromCharacter();
    }

    protected override Transform FindCombatTarget()
    {
        Transform viaFaction = TrySelectEnemyViaFactionFinder();
        if (viaFaction != null)
            return viaFaction;

        float aggroSq = AggroRadiusSqr;
        Transform best = null;
        float bestSq = float.MaxValue;

        if (_player != null && IsAliveDamageableTarget(_player))
        {
            float sq = SpatialMath.FlatSqrDistance(transform.position, _player.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(_player))
            {
                best = _player;
                bestSq = sq;
            }
        }

        FollowerController[] followers = CombatUnitRegistry.GetFollowers();
        for (int i = 0; i < followers.Length; i++)
        {
            FollowerController f = followers[i];
            if (f == null || !IsAliveDamageableTarget(f.transform))
                continue;
            Transform ft = f.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, ft.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(ft))
            {
                best = ft;
                bestSq = sq;
            }
        }

        VillagerController[] villagers = CombatUnitRegistry.GetVillagers();
        for (int i = 0; i < villagers.Length; i++)
        {
            VillagerController v = villagers[i];
            if (v == null || !IsAliveDamageableTarget(v.transform))
                continue;
            Transform vt = v.transform;
            float sq = SpatialMath.FlatSqrDistance(transform.position, vt.position);
            if (sq <= aggroSq && sq < bestSq && HasLineOfSightTo(vt))
            {
                best = vt;
                bestSq = sq;
            }
        }

        return best;
    }
}
