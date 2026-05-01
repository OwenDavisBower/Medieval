using UnityEngine;

public class FollowerController : CombatSeekControllerBase
{
    [Header("Formation")]
    [Tooltip("Horizontal distance from the leader beyond which followers stop chasing and move back. 0 = no limit.")]
    [SerializeField] float maxDistanceFromLeader = 25f;
    [Tooltip("When farther than this many blocks from the player, follower is teleported closer.")]
    [SerializeField] float teleportBackDistance = 80f;
    [Tooltip("Distance from the player to place follower after teleporting back.")]
    [SerializeField] float teleportBackTargetDistance = 50f;

    Rigidbody _rb;

    /// <summary>Assigns a random orbit around the player; call once after spawn.</summary>
    public void Initialize()
    {
        EnsureComponentsInitialized();
        Motor.InitializeOrbitRandom();
        TryAssignPlayerAnchor();
    }

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        TryAssignPlayerAnchor();
        ApplySeekHoldDistanceFromRole();
        ApplyMotorSpeedFromCharacter();
    }

    void Update()
    {
        TryTeleportBackTowardLeader();
    }

    void TryAssignPlayerAnchor()
    {
        var p = PlayerReference.TryGetTransform();
        if (p != null)
            Motor.AnchorTarget = p;
    }

    void TryTeleportBackTowardLeader()
    {
        if (teleportBackDistance <= 0f || teleportBackTargetDistance < 0f)
            return;

        Transform anchor = Motor.AnchorTarget;
        if (anchor == null)
            return;

        Vector3 followerPos = transform.position;
        Vector3 leaderPos = anchor.position;
        float sq = SpatialMath.FlatSqrDistance(followerPos, leaderPos);
        if (sq <= teleportBackDistance * teleportBackDistance)
            return;

        Vector3 awayFromLeader = followerPos - leaderPos;
        awayFromLeader.y = 0f;
        if (awayFromLeader.sqrMagnitude <= 1e-6f)
            awayFromLeader = Vector3.back;
        awayFromLeader.Normalize();

        Vector3 target = leaderPos + awayFromLeader * teleportBackTargetDistance;
        target = TerrainSpawnUtility.GetWorldPositionOnTerrain(target);

        if (_rb != null)
        {
            _rb.position = target;
            _rb.linearVelocity = Vector3.zero;
        }
        else
        {
            transform.position = target;
        }
    }
}
