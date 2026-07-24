using UnityEngine;

/// <summary>
/// A patrolling guard with a vision cone. Walks its PatrolPath by driving velocity
/// (so shoves resolve as collisions instead of breaking the path), shows one of four
/// directional sprites, wobbles side-to-side while walking, freezes to stare when it
/// spots the player, and walks back onto the path if knocked off.
///
/// Sight is blocked by anything on Obstacle Mask — a guard can't see through walls.
/// Sprites: assign Front (down/toward camera), Back (up/away), Side (right). Left
/// reuses Side flipped.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class Guard : MonoBehaviour
{
    [Header("Sprites")]
    [Tooltip("Facing down / toward the camera.")]
    public Sprite frontSprite;
    [Tooltip("Facing up / away from the camera.")]
    public Sprite backSprite;
    [Tooltip("Facing right. Left automatically reuses this, flipped.")]
    public Sprite sideSprite;
    [Tooltip("The SpriteRenderer to drive. Leave empty to use one on this object or a child.")]
    public SpriteRenderer spriteRenderer;

    [Header("Walk bounce")]
    [Tooltip("Rock the sprite side to side while walking.")]
    public bool bounceWhileWalking = true;
    [Tooltip("Max tilt in degrees at the peak of each rock.")]
    public float bounceAngle = 8f;
    [Tooltip("Full left-right rocks per second.")]
    public float bounceSpeed = 6f;
    [Tooltip("Optional vertical hop height, in world units. 0 = no hop.")]
    public float bounceHeight = 0f;
    [Tooltip("How fast the wobble settles back to neutral when the guard stops.")]
    public float bounceSettle = 12f;

    [Header("Patrol")]
    public PatrolPath path;
    public float moveSpeed = 2.5f;
    [Tooltip("How close to a waypoint counts as 'reached'.")]
    public float arriveDistance = 0.12f;
    [Tooltip("Seconds to pause at each waypoint before moving on.")]
    public float waitAtPoint = 0.4f;
    [Tooltip("Degrees per second the cone rotates. Lower = lazier sweeps.")]
    public float turnSpeed = 360f;
    [Tooltip("Won't start walking until the cone is within this many degrees of the target, so corners pivot in place.")]
    public float walkAlignAngle = 25f;

    [Header("Path recovery")]
    [Tooltip("If shoved more than this far from the PATH LINE (not just the next waypoint), the guard walks back to the nearest point before resuming.")]
    public float recoverDistance = 1.2f;
    [Tooltip("Speed while walking back onto the path.")]
    public float recoverSpeed = 3f;

    [Header("Target")]
    [Tooltip("Leave empty and it finds the object tagged 'Player'.")]
    public Transform player;
    [Tooltip("Leave empty and it pulls the Collider2D off the player.")]
    public Collider2D playerBody;

    [Header("Vision cone")]
    public float range = 6f;
    [Range(1f, 360f)] public float fovAngle = 75f;
    [Tooltip("Layers that block sight — walls, crates. May include the player's layer safely. Set this to Obstacles.")]
    public LayerMask obstacleMask;
    [Tooltip("Nudges the eye off the guard's origin, e.g. to the front of the sprite.")]
    public Vector2 eyeOffset = Vector2.zero;

    [Header("Detection")]
    [Range(2, 6)] public int samplesPerAxis = 3;
    [Range(0f, 1f)] public float requiredVisibleFraction = 0.5f;
    [Tooltip("Seconds the player must stay in view before PlayerDie fires.")]
    public float detectionDelay = 0.5f;
    public bool triggerOnce = true;

    [Header("On spot")]
    public bool freezeOnSpot = true;
    public float alertTurnSpeed = 720f;
    [Tooltip("Seconds to keep still after losing sight, before resuming patrol.")]
    public float relaxDelay = 0.6f;

    [Header("Dev view")]
    public bool drawGizmos = true;
    public Color clearColor = new Color(0.3f, 1f, 0.5f, 0.85f);
    public Color alertColor = new Color(1f, 0.3f, 0.25f, 0.9f);
    public Color recoverColor = new Color(0.3f, 0.6f, 1f, 0.9f);

    // ---- read from other scripts
    public float VisibleFraction { get; private set; }
    public bool Spotted { get; private set; }
    public bool Alerted { get; private set; }
    public bool Recovering { get; private set; }

    enum State { Patrol, Recover, Alert }

    Rigidbody2D rb;
    State state = State.Patrol;
    int index;
    int dir = 1;
    float wait;
    float facing;        // math convention: 0 = +X (right), 90 = up, CCW
    float detectTimer;
    float relaxTimer;
    bool fired;

    readonly Vector2[] samples = new Vector2[36];

    // Wobble state
    Transform visual;
    Vector3 visualBasePos;
    float bouncePhase;

    Vector2 Forward => new Vector2(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad));
    Vector2 Origin  => (Vector2)transform.position + (Vector2)(Quaternion.Euler(0, 0, facing) * eyeOffset);

    // velocity was renamed linearVelocity in Unity 6. This works on both versions.
    Vector2 Vel
    {
#if UNITY_6000_0_OR_NEWER
        get => rb.linearVelocity;
        set => rb.linearVelocity = value;
#else
        get => rb.velocity;
        set => rb.velocity = value;
#endif
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;          // the BODY never rotates; we swap sprites instead
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // The wobble tilts a child, not the body. If the sprite is directly on this
        // object, move it onto an auto-created Visual child so the tilt doesn't rotate
        // the collider.
        if (spriteRenderer)
        {
            if (spriteRenderer.transform == transform)
            {
                var go = new GameObject("Visual");
                visual = go.transform;
                visual.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = spriteRenderer.sprite;
                sr.sortingLayerID = spriteRenderer.sortingLayerID;
                sr.sortingOrder = spriteRenderer.sortingOrder;
                sr.material = spriteRenderer.material;
                sr.flipX = spriteRenderer.flipX;

                spriteRenderer.enabled = false;   // hide original; the child draws now
                spriteRenderer = sr;
            }
            else
            {
                visual = spriteRenderer.transform;   // already a child, wobble that
            }
        }
        if (visual) visualBasePos = visual.localPosition;

        if (!player)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
        }
        if (!playerBody && player) playerBody = player.GetComponentInChildren<Collider2D>();

        if (path && path.HasPath)
        {
            index = NearestPointIndex(transform.position);
            facing = AngleTo(path.Get(index));
        }
        UpdateSprite();
    }

    void Update()
    {
        Detect();
        AnimateVisual();
    }

    void FixedUpdate() => Tick();

    // ---------------------------------------------------------------- state machine

    void Tick()
    {
        // Alert overrides everything: freeze and stare at the player.
        if (Alerted && freezeOnSpot)
        {
            state = State.Alert;
            Vel = Vector2.zero;
            if (player) RotateToward(AngleTo(player.position), alertTurnSpeed);
            return;
        }

        if (!path || !path.HasPath) { Vel = Vector2.zero; return; }

        // Only recover if we're far from the WHOLE path — i.e. shoved off the line
        // we're walking — not merely far from the next waypoint, which is normal
        // mid-leg. Measuring distance to the nearest path SEGMENT is what keeps the
        // guard from freezing whenever waypoints are spaced wider than recoverDistance.
        if (state != State.Recover)
        {
            float offBy = DistanceToPath(rb.position);
            if (offBy > recoverDistance)
            {
                state = State.Recover;
                index = NearestPointIndex(rb.position);
                wait = 0f;
            }
        }

        if (state == State.Recover) Recover();
        else                        Patrol();
    }

    // ---------------------------------------------------------------- patrol

    void Patrol()
    {
        Recovering = false;

        Vector2 pos = rb.position;
        Vector2 waypoint = path.Get(index);

        // Pause at a waypoint, pre-aiming at the next so the turn finishes here.
        if (wait > 0f)
        {
            Vel = Vector2.zero;
            wait -= Time.fixedDeltaTime;
            int peek = path.Peek(index, dir);
            RotateToward(AngleTo(path.Get(peek)), turnSpeed);
            return;
        }

        // Arrived at the waypoint: advance and pause.
        if (Vector2.Distance(pos, waypoint) <= arriveDistance)
        {
            index = path.Advance(index, ref dir);
            wait = waitAtPoint;
            Vel = Vector2.zero;
            return;
        }

        // Turn first; only move once roughly aligned, so corners pivot in place.
        float targetAngle = AngleToward(pos, waypoint);
        RotateToward(targetAngle, turnSpeed);

        if (Mathf.Abs(Mathf.DeltaAngle(facing, targetAngle)) > walkAlignAngle)
        {
            Vel = Vector2.zero;
            return;
        }

        DriveToward(waypoint, moveSpeed);
    }

    // Walk back onto the path after being displaced, then hand back to Patrol.
    void Recover()
    {
        Recovering = true;

        Vector2 target = path.Get(index);
        Vector2 to = target - rb.position;

        if (to.magnitude <= arriveDistance * 1.5f)
        {
            state = State.Patrol;
            Recovering = false;
            wait = 0f;
            Vel = Vector2.zero;
            return;
        }

        // Head straight back — no align gate, getting on the path fast matters more.
        RotateToward(AngleTo(target), turnSpeed);
        DriveToward(target, recoverSpeed);
    }

    // Set velocity toward a point, easing as we arrive. Velocity (not MovePosition)
    // lets the physics engine resolve collisions, so shoves don't break the path.
    void DriveToward(Vector2 target, float speed)
    {
        Vector2 to = target - rb.position;
        float dist = to.magnitude;
        if (dist < 1e-4f) { Vel = Vector2.zero; return; }

        float v = Mathf.Min(speed, dist / Time.fixedDeltaTime);
        Vel = to / dist * v;
    }

    // ---------------------------------------------------------------- facing → sprite

    void RotateToward(float targetAngle, float speed)
    {
        facing = Mathf.MoveTowardsAngle(facing, targetAngle, speed * Time.fixedDeltaTime);
        UpdateSprite();
    }

    // Pick front / back / side (flipped for left) from the current facing angle.
    // Bands: right [-45,45], up [45,135], left [135,225], down otherwise.
    void UpdateSprite()
    {
        if (!spriteRenderer) return;

        float a = Mathf.Repeat(facing, 360f);   // 0..360

        if (a >= 45f && a < 135f)               // up / away
        {
            if (backSprite) spriteRenderer.sprite = backSprite;
            spriteRenderer.flipX = false;
        }
        else if (a >= 135f && a < 225f)         // left
        {
            if (sideSprite) spriteRenderer.sprite = sideSprite;
            spriteRenderer.flipX = true;
        }
        else if (a >= 225f && a < 315f)         // down / toward camera
        {
            if (frontSprite) spriteRenderer.sprite = frontSprite;
            spriteRenderer.flipX = false;
        }
        else                                    // right
        {
            if (sideSprite) spriteRenderer.sprite = sideSprite;
            spriteRenderer.flipX = false;
        }
    }

    // ---------------------------------------------------------------- wobble

    void AnimateVisual()
    {
        if (!visual) return;

        // Wobble while actually moving. Alerted = frozen, so no wobble.
        bool walking = !Alerted && Vel.sqrMagnitude > 0.04f;

        if (bounceWhileWalking && walking)
        {
            bouncePhase += Time.deltaTime * bounceSpeed * Mathf.PI * 2f;

            float rock = Mathf.Sin(bouncePhase) * bounceAngle;              // side-to-side tilt
            float hop = Mathf.Abs(Mathf.Sin(bouncePhase)) * bounceHeight;   // optional up-down

            visual.localRotation = Quaternion.Euler(0f, 0f, rock);
            visual.localPosition = visualBasePos + Vector3.up * hop;
        }
        else
        {
            visual.localRotation = Quaternion.RotateTowards(
                visual.localRotation, Quaternion.identity, bounceSettle * 60f * Time.deltaTime);
            visual.localPosition = Vector3.MoveTowards(
                visual.localPosition, visualBasePos, bounceSettle * Time.deltaTime);

            if (Mathf.Abs(Mathf.DeltaAngle(visual.localEulerAngles.z, 0f)) < 0.1f)
                bouncePhase = 0f;
        }
    }

    // ---------------------------------------------------------------- helpers

    int NearestPointIndex(Vector2 from)
    {
        int best = 0;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            float sq = ((Vector2)path.Get(i) - from).sqrMagnitude;
            if (sq < bestSqr) { bestSqr = sq; best = i; }
        }
        return best;
    }

    // Shortest distance from a point to the whole patrol path, checking each segment
    // rather than just the corner points. A guard mid-leg sits ON a segment, so this
    // reads ~0 even when it's far from any single waypoint.
    float DistanceToPath(Vector2 from)
    {
        if (path.Count == 0) return 0f;
        if (path.Count == 1) return Vector2.Distance(from, path.Get(0));

        float best = float.MaxValue;
        int segs = path.loop ? path.Count : path.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            Vector2 a = path.Get(i);
            Vector2 b = path.Get((i + 1) % path.Count);
            float d = Vector2.Distance(from, ClosestOnSegment(a, b, from));
            if (d < best) best = d;
        }
        return best;
    }

    static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
    {
        Vector2 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    float AngleTo(Vector2 worldPoint)
    {
        Vector2 d = worldPoint - rb.position;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    // From an arbitrary position.
    float AngleToward(Vector2 from, Vector2 to)
    {
        Vector2 d = to - from;
        return Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
    }

    // ---------------------------------------------------------------- detection

    void Detect()
    {
        if (!player) return;

        VisibleFraction = ComputeVisibleFraction();
        Spotted = VisibleFraction >= requiredVisibleFraction && VisibleFraction > 0f;

        if (Spotted)
        {
            Alerted = true;
            relaxTimer = 0f;
            detectTimer += Time.deltaTime;

            if (detectTimer >= detectionDelay && !fired)
            {
                fired = true;
                PlayerDie();
            }
        }
        else
        {
            detectTimer = 0f;
            if (!triggerOnce) fired = false;

            if (Alerted)
            {
                relaxTimer += Time.deltaTime;
                if (relaxTimer >= relaxDelay)
                {
                    Alerted = false;
                    if (path && path.HasPath) index = NearestPointIndex(rb.position);
                    state = State.Patrol;
                }
            }
        }
    }

    /// <summary>Fires once the player has been visible for detectionDelay seconds.</summary>
    void PlayerDie()
    {
        Debug.Log(name + " spotted the player.", this);
        // TODO
    }

    float ComputeVisibleFraction()
    {
        int count = BuildSamplePoints();
        if (count == 0) return 0f;

        Vector2 origin = Origin;
        int visible = 0;
        for (int i = 0; i < count; i++)
            if (PointVisible(origin, samples[i])) visible++;

        return visible / (float)count;
    }

    int BuildSamplePoints()
    {
        Bounds b = playerBody
            ? playerBody.bounds
            : new Bounds(player.position, new Vector3(0.6f, 0.9f, 0f));

        int n = Mathf.Clamp(samplesPerAxis, 2, 6);
        int i = 0;
        for (int gx = 0; gx < n; gx++)
        {
            float tx = Mathf.Lerp(0.15f, 0.85f, gx / (float)(n - 1));
            for (int gy = 0; gy < n; gy++)
            {
                float ty = Mathf.Lerp(0.15f, 0.85f, gy / (float)(n - 1));
                samples[i++] = new Vector2(Mathf.Lerp(b.min.x, b.max.x, tx),
                                           Mathf.Lerp(b.min.y, b.max.y, ty));
            }
        }
        return i;
    }

    // A sample counts as seen if it's in range, inside the cone, and the sightline to
    // it isn't blocked by anything on Obstacle Mask — this stops the guard seeing
    // through walls, exactly like the security camera.
    bool PointVisible(Vector2 origin, Vector2 p)
    {
        Vector2 to = p - origin;
        float dist = to.magnitude;

        if (dist > range) return false;
        if (dist > 0.0001f && Vector2.Angle(Forward, to) > fovAngle * 0.5f) return false;

        RaycastHit2D hit = Physics2D.Linecast(origin, p, obstacleMask);
        if (hit.collider && !IsPlayer(hit.collider)) return false;   // wall in the way
        return true;
    }

    bool IsPlayer(Collider2D c)
    {
        if (c == playerBody) return true;
        return player && c.transform.IsChildOf(player);
    }

    // ---------------------------------------------------------------- dev gizmos

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector2 origin = Application.isPlaying ? Origin : (Vector2)transform.position;
        float aim = Application.isPlaying ? facing : 0f;

        Gizmos.color = clearColor;
        if (Application.isPlaying)
        {
            if (Spotted) Gizmos.color = alertColor;
            else if (Recovering) Gizmos.color = recoverColor;
        }

        // Cone edges + arc, clipped to walls so it shows exactly what's seen.
        int seg = 26;
        Vector3 prev = origin;
        for (int i = 0; i <= seg; i++)
        {
            float a = aim - fovAngle * 0.5f + fovAngle * (i / (float)seg);
            Vector2 dir = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));

            float len = range;
            if (Application.isPlaying)
            {
                RaycastHit2D h = Physics2D.Raycast(origin, dir, range, obstacleMask);
                if (h.collider && !IsPlayer(h.collider)) len = h.distance;
            }
            Vector3 pt = origin + dir * len;

            if (i == 0 || i == seg) Gizmos.DrawLine(origin, pt);
            if (i > 0) Gizmos.DrawLine(prev, pt);
            prev = pt;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !Application.isPlaying || !player) return;

        Vector2 origin = Origin;
        int count = BuildSamplePoints();
        for (int i = 0; i < count; i++)
        {
            bool ok = PointVisible(origin, samples[i]);
            Gizmos.color = ok ? Color.green : new Color(1f, 0.35f, 0.35f, 0.5f);
            Gizmos.DrawLine(origin, samples[i]);
        }
    }
}