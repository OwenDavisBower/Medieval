using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] float moveSpeed = 6f;

    Rigidbody _rb;
    Transform _cam;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        if (Camera.main != null)
            _cam = Camera.main.transform;
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
        Vector3 targetHorizontal = move * moveSpeed;
        velocity.x = targetHorizontal.x;
        velocity.z = targetHorizontal.z;
        _rb.linearVelocity = velocity;
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
