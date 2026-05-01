using UnityEngine;

public class BanditController : CombatSeekControllerBase
{
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

        ApplySeekHoldDistanceFromRole();
        ApplyMotorSpeedFromCharacter();
    }
}
