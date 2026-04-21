using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    /// <summary>Fired once after the player has been snapped onto terrain; argument is the applied world position (use this instead of <see cref="Transform.position"/>—RB teleport may not sync the transform yet this frame).</summary>
    public static event Action<Vector3>? PlayerStartPositionApplied;
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float terrainSnapHeightOffset = 0.05f;
    [Tooltip("Max degrees per second when rotating to face movement input.")]
    [SerializeField] float facingTurnSpeedDegreesPerSecond = 720f;

    Rigidbody _rb;
    Transform _cam;
    Character _character;
    bool _snappedToTerrain;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _character = GetComponent<Character>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
        if (Camera.main != null)
            _cam = Camera.main.transform;
    }

    void OnEnable()
    {
        TerrainGenerator.TerrainGenerated += OnTerrainGenerated;
    }

    void OnDisable()
    {
        TerrainGenerator.TerrainGenerated -= OnTerrainGenerated;
    }

    void Start()
    {
        TrySnapToTerrain();
    }

    void OnTerrainGenerated(TerrainGenerator _) => TrySnapToTerrain();

    void TrySnapToTerrain()
    {
        if (_snappedToTerrain)
            return;

        var gen = TerrainGenerator.GetActiveOrFind();
        if (gen == null || !gen.IsTerrainReady)
            return;

        _snappedToTerrain = true;
        Vector3 p;
        var spawnXz = new Vector2(transform.position.x, transform.position.z);
        if (!gen.TryGetClosestPathPointWorldXZ(spawnXz, terrainSnapHeightOffset, out p))
            p = TerrainSpawnUtility.GetWorldPositionOnTerrain(transform.position, terrainSnapHeightOffset);
        // Dynamic Rigidbodies: set Rigidbody.position so PhysX state matches (transform-only snaps often appear ignored).
        _rb.position = p;
        Vector3 v = _rb.linearVelocity;
        v.y = 0f;
        _rb.linearVelocity = v;
        PlayerStartPositionApplied?.Invoke(p);
    }

    void FixedUpdate()
    {
        ReadMoveAxes(out float h, out float v);

        Vector3 move;
        if (_cam != null)
        {
            Vector3 f = _cam.forward;
            f.y = 0f;
            f.Normalize();
            Vector3 r = _cam.right;
            r.y = 0f;
            r.Normalize();
            move = f * v + r * h;
        }
        else
        {
            move = Vector3.forward * v + Vector3.right * h;
        }

        if (move.sqrMagnitude > 1f)
            move.Normalize();

        Vector3 velocity = _rb.linearVelocity;
        float speed = moveSpeed;
        if (_character != null)
            speed *= _character.MovementSpeedMultiplier;
        speed *= WaterMovement.SpeedMultiplier(transform.position.y);
        Vector3 targetHorizontal = move * speed;
        velocity.x = targetHorizontal.x;
        velocity.z = targetHorizontal.z;
        _rb.linearVelocity = velocity;

        if (move.sqrMagnitude > 1e-4f)
        {
            Quaternion targetRot = Quaternion.LookRotation(move, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot,
                facingTurnSpeedDegreesPerSecond * Time.fixedDeltaTime);
        }
    }

    static void ReadMoveAxes(out float h, out float v)
    {
        h = 0f;
        v = 0f;

        var pad = Gamepad.current;
        if (pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            if (stick.sqrMagnitude > 0.01f)
            {
                h = stick.x;
                v = stick.y;
                return;
            }
        }

        var k = Keyboard.current;
        if (k == null)
            return;

        if (k.leftArrowKey.isPressed || k.aKey.isPressed)
            h -= 1f;
        if (k.rightArrowKey.isPressed || k.dKey.isPressed)
            h += 1f;
        if (k.downArrowKey.isPressed || k.sKey.isPressed)
            v -= 1f;
        if (k.upArrowKey.isPressed || k.wKey.isPressed)
            v += 1f;
    }
}
