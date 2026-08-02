using Medieval.Npcs;
using UnityEngine;

/// <summary>
/// Auto-fires the player's bow at the nearest enemy while standing still.
/// Move input during a shot cancels it via <see cref="RangedCombat.CancelShot"/> (see <see cref="PlayerController"/>).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RangedCombat))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerController))]
public sealed class PlayerBowCombat : MonoBehaviour
{
    [SerializeField] float combatRange = 20f;
    [SerializeField] float eyeHeight = 1.4f;
    [SerializeField] float stopSpeedThreshold = 0.04f;
    [SerializeField] LayerMask obstacleLayers = ~0;
    [Tooltip("Max degrees per second when rotating to face the current aim target.")]
    [SerializeField] float aimTurnSpeedDegreesPerSecond = 720f;

    RangedCombat _ranged;
    TargetFinder _targetFinder;
    Rigidbody _rb;
    PlayerController _player;
    Affiliation _affiliation;
    Vector3 _aimFeetWorld;
    bool _hasAimTarget;

    void Awake()
    {
        _ranged = GetComponent<RangedCombat>();
        _targetFinder = GetComponent<TargetFinder>();
        _rb = GetComponent<Rigidbody>();
        _player = GetComponent<PlayerController>();
        _affiliation = GetComponent<Affiliation>();
        if (_targetFinder != null)
            _targetFinder.SetPeriodicScanEnabled(false);
    }

    void Update()
    {
        if (!_hasAimTarget)
            return;
        if (!_ranged.IsMovementLocked && !_player.IsStationaryForRanged(stopSpeedThreshold))
            return;

        Vector3 to = _aimFeetWorld - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 1e-4f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(to, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot,
            aimTurnSpeedDegreesPerSecond * Time.deltaTime);
    }

    void FixedUpdate()
    {
        _hasAimTarget = false;

        if (_ranged == null || !_ranged.isActiveAndEnabled)
            return;

        if (!TryGetShootTarget(out Transform transformTarget, out Vector3 feetWorld))
            return;

        _aimFeetWorld = transformTarget != null ? transformTarget.position : feetWorld;
        _hasAimTarget = true;

        // Mid-draw: stay locked / facing; do not start another shot.
        if (_ranged.IsMovementLocked)
            return;

        if (!_player.IsStationaryForRanged(stopSpeedThreshold))
            return;

        if (transformTarget != null)
            _ranged.TryFireAt(transformTarget);
        else
            _ranged.TryFireAtWorldFeet(feetWorld);
    }

    bool TryGetShootTarget(out Transform transformTarget, out Vector3 feetWorld)
    {
        transformTarget = null;
        feetWorld = default;

        float rangeSq = combatRange * combatRange;
        float aimHeight = _ranged.TargetAimHeight;

        if (_targetFinder != null)
        {
            _targetFinder.ScanNow();
            Transform candidate = _targetFinder.CurrentEnemyTarget;
            if (candidate != null)
            {
                var health = candidate.GetComponentInParent<IDamageableHealth>();
                if (health == null || !health.IsDead)
                {
                    float sq = SpatialMath.FlatSqrDistance(transform.position, candidate.position);
                    if (sq <= rangeSq &&
                        LineOfSightUtility.HasClearLineOfSight(transform.position, candidate, eyeHeight, aimHeight,
                            obstacleLayers, transform.root))
                    {
                        transformTarget = candidate;
                        return true;
                    }
                }
            }
        }

        int factionId = _affiliation != null ? _affiliation.FactionId : -1;
        return NpcHostileTargetQuery.TryFindNearestHostileDotsNpc(
            factionId,
            transform.position,
            combatRange,
            eyeHeight,
            aimHeight,
            obstacleLayers,
            transform.root,
            out feetWorld,
            out _);
    }
}
