using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 60f;   // set very high for instant snappy control
    public float deceleration = 70f;
    public bool normalizeDiagonal = true;

    [Header("Optional")]
    public bool faceMoveDirection = false; // rotates sprite toward movement

    Rigidbody2D rb;
    Vector2 input;
    Vector2 velocity;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    void Update()
    {
        float x = 0f, y = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  y -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    y += 1f;

        input = new Vector2(x, y);
        if (normalizeDiagonal && input.sqrMagnitude > 1f) input.Normalize();
    }

    void FixedUpdate()
    {
        Vector2 target = input * moveSpeed;
        float rate = input.sqrMagnitude > 0.01f ? acceleration : deceleration;
        velocity = Vector2.MoveTowards(velocity, target, rate * Time.fixedDeltaTime);

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = velocity;
#else
        rb.velocity = velocity;
#endif

        if (faceMoveDirection && velocity.sqrMagnitude > 0.01f)
        {
            float ang = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.LerpAngle(rb.rotation, ang, 0.25f));
        }
    }

    /// <summary>True world-space velocity, useful for animator params.</summary>
    public Vector2 Velocity => velocity;
}