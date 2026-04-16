using UnityEngine;

[RequireComponent(typeof(TargetSteeringMotor))]
[RequireComponent(typeof(Rigidbody))]
public class FollowerController : MonoBehaviour
{
    TargetSteeringMotor _motor;

    void Awake()
    {
        _motor = GetComponent<TargetSteeringMotor>();
    }

    /// <summary>Assigns a random orbit around the player; call once after spawn.</summary>
    public void Initialize()
    {
        if (_motor == null)
            _motor = GetComponent<TargetSteeringMotor>();
        _motor.InitializeOrbitRandom();
        TryAssignPlayerAnchor();
    }

    void Start()
    {
        TryAssignPlayerAnchor();
    }

    void TryAssignPlayerAnchor()
    {
        var p = GameObject.Find("Player");
        if (p != null)
            _motor.AnchorTarget = p.transform;
    }
}
