using UnityEngine;

[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public class VillagerController : MonoBehaviour
{
    TargetSteeringMotor _motor;
    Character _character;
    bool _initialized;

    /// <summary>Wander around this anchor (e.g. the cabin they belong to).</summary>
    public void Initialize(Transform villageAnchor)
    {
        if (_motor == null)
            _motor = GetComponent<TargetSteeringMotor>();
        _motor.Mode = TargetSteeringMovementMode.WanderAroundTarget;
        _motor.InitializeWanderAroundAnchor(villageAnchor);
        _initialized = true;
    }

    void Awake()
    {
        _motor = GetComponent<TargetSteeringMotor>();
        _character = GetComponent<Character>();
    }

    void Start()
    {
        if (!_initialized)
            _motor.InitializeWanderAroundAnchor(transform);

        CharacterMotorLink.ApplyMovementSpeed(_character, _motor);
    }
}
