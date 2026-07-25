using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 60f;   // set very high for instant snappy control
    public float deceleration = 70f;
    public bool normalizeDiagonal = true;

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
    [Tooltip("How fast the wobble settles back to neutral when you stop.")]
    public float bounceSettle = 12f;

    [Header("Footsteps")]
    [Tooltip("Play footstep sounds while walking.")]
    public bool footstepsEnabled = true;
    [Tooltip("Clips to pick from. One is chosen at random per step.")]
    public AudioClip[] footstepClips;
    [Tooltip("AudioSource to play through. Leave empty to auto-create one.")]
    public AudioSource footstepSource;
    [Tooltip("Base volume for each step.")]
    [Range(0f, 1f)] public float footstepVolume = 0.7f;
    [Tooltip("Random pitch wiggle so repeats don't sound identical. 0 = off.")]
    public float footstepPitchJitter = 0.08f;
    [Tooltip("Steps taken per second at full walking speed. Scales down when moving slower.")]
    public float stepsPerSecond = 2f;
    [Tooltip("Sync steps to the walk bounce peaks instead of a fixed timer. Needs a Visual child.")]
    public bool syncStepsToBounce = true;

    Rigidbody2D rb;
    Vector2 input;
    Vector2 velocity;

    Transform visual;        // child that holds the sprite and does the bounce
    Vector3 visualBasePos;   // resting local position of the visual
    float facing = -90f;     // math angle, start facing down
    float bouncePhase;

    float stepTimer;         // fixed-timer fallback accumulator
    int lastStepClip = -1;   // avoids playing the same clip twice in a row
    float lastBounceSin;     // for detecting bounce peak crossings

    public bool frozen;
    public float freezeTimeTotal = 1f;
    private float freezeTimeCurrent = 0f;

    [SerializeField] LevelTracker lt;

    countdownTimer cd;

    // velocity was renamed linearVelocity in Unity 6. This works on both.
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
        rb.freezeRotation = true;

        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        // The bounce rotates a child, not the body, so physics stays clean. If the
        // sprite is directly on this object, we can't rotate it without rotating the
        // collider too — so move it onto an auto-created Visual child.
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

                spriteRenderer.enabled = false;   // hide the original; the child draws now
                spriteRenderer = sr;
            }
            else
            {
                visual = spriteRenderer.transform;   // already a child, use it
            }
        }

        if (visual) visualBasePos = visual.localPosition;
        UpdateSprite();

        // Grab or create an AudioSource for footsteps.
        if (footstepsEnabled && !footstepSource)
        {
            footstepSource = GetComponent<AudioSource>();
            if (!footstepSource) footstepSource = gameObject.AddComponent<AudioSource>();
        }
        if (footstepSource)
        {
            footstepSource.playOnAwake = false;
            footstepSource.loop = false;
        }

        cd = FindFirstObjectByType<countdownTimer>();
    }

    void Start()
    {
        frozen = false;
    }

    void Update()
    {
        float x = 0f, y = 0f;
        if (!frozen)
        {
            #if ENABLE_INPUT_SYSTEM
                    var k = Keyboard.current;
                    if (k != null)
                    {
                        if (k.aKey.isPressed || k.leftArrowKey.isPressed)  x -= 1f;
                        if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
                        if (k.sKey.isPressed || k.downArrowKey.isPressed)  y -= 1f;
                        if (k.wKey.isPressed || k.upArrowKey.isPressed)    y += 1f;
                    }
            #endif
            #if ENABLE_LEGACY_INPUT_MANAGER
                    if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  x -= 1f;
                    if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
                    if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  y -= 1f;
                    if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    y += 1f;
            #endif

                    input = new Vector2(x, y);
                    if (normalizeDiagonal && input.sqrMagnitude > 1f) input.Normalize();

                    AnimateVisual();
                    UpdateFootsteps();
        }
        else
        {
            rb.velocity = new Vector2(0,0);
            freezeTimeCurrent += Time.deltaTime;

            if(freezeTimeCurrent > freezeTimeTotal)
            {
                lt.Reset();
            }
        }
    }

    void FixedUpdate()
    {
        if (!frozen)
        {
            Vector2 target = input * moveSpeed;
            float rate = input.sqrMagnitude > 0.01f ? acceleration : deceleration;
            velocity = Vector2.MoveTowards(velocity, target, rate * Time.fixedDeltaTime);
            Vel = velocity;

            // Only change facing when actually moving, so we don't reset to a default
            // direction the instant the player stops.
            if (velocity.sqrMagnitude > 0.01f)
            {
                cd.start = true;

                facing = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg;
                UpdateSprite();
            }
        }
    }

    // ---------------------------------------------------------------- facing → sprite

    // Pick front / back / side (flipped for left) from the facing angle.
    // Bands: right [-45,45], up [45,135], left [135,225], down otherwise.
    void UpdateSprite()
    {
        if (!spriteRenderer) return;

        float a = Mathf.Repeat(facing, 360f);

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

    // ---------------------------------------------------------------- bounce

    void AnimateVisual()
    {
        if (!visual) return;

        bool walking = velocity.sqrMagnitude > 0.04f;

        if (bounceWhileWalking && walking)
        {
            // Advance the rock. One full cycle = left, back, right, back.
            bouncePhase += Time.deltaTime * bounceSpeed * Mathf.PI * 2f;

            float rock = Mathf.Sin(bouncePhase) * bounceAngle;         // side-to-side tilt
            float hop = Mathf.Abs(Mathf.Sin(bouncePhase)) * bounceHeight; // optional up-down

            visual.localRotation = Quaternion.Euler(0f, 0f, rock);
            visual.localPosition = visualBasePos + Vector3.up * hop;
        }
        else
        {
            // Settle smoothly back to upright and resting when not walking.
            visual.localRotation = Quaternion.RotateTowards(
                visual.localRotation, Quaternion.identity, bounceSettle * 60f * Time.deltaTime);
            visual.localPosition = Vector3.MoveTowards(
                visual.localPosition, visualBasePos, bounceSettle * Time.deltaTime);

            if (Mathf.Abs(Mathf.DeltaAngle(visual.localEulerAngles.z, 0f)) < 0.1f)
                bouncePhase = 0f;   // reset so the next walk starts from neutral
        }
    }

    // ---------------------------------------------------------------- footsteps

    void UpdateFootsteps()
    {
        if (!footstepsEnabled || footstepSource == null || footstepClips == null || footstepClips.Length == 0)
            return;

        bool walking = velocity.sqrMagnitude > 0.04f;

        if (!walking)
        {
            // Reset timers/peak tracking so the first step after stopping lands cleanly.
            stepTimer = 0f;
            lastBounceSin = 0f;
            return;
        }

        if (syncStepsToBounce && bounceWhileWalking && visual)
        {
            // A foot plants at each side of the sway — i.e. when sin() reaches ±1,
            // which is where it changes direction. Detect that turnaround.
            float s = Mathf.Sin(bouncePhase);
            bool peak = (s >= 0.99f && lastBounceSin < 0.99f) ||
                        (s <= -0.99f && lastBounceSin > -0.99f);
            lastBounceSin = s;
            if (peak) PlayFootstep();
        }
        else
        {
            // Fixed timer, scaled by how fast we're actually moving.
            float speed01 = Mathf.Clamp01(velocity.magnitude / Mathf.Max(0.01f, moveSpeed));
            stepTimer += Time.deltaTime * stepsPerSecond * speed01;
            if (stepTimer >= 1f)
            {
                stepTimer -= 1f;
                PlayFootstep();
            }
        }
    }

    void PlayFootstep()
    {
        // Pick a random clip, avoiding an immediate repeat when there's more than one.
        int i = Random.Range(0, footstepClips.Length);
        if (footstepClips.Length > 1 && i == lastStepClip)
            i = (i + 1) % footstepClips.Length;
        lastStepClip = i;

        AudioClip clip = footstepClips[i];
        if (!clip) return;

        footstepSource.pitch = 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter);
        footstepSource.PlayOneShot(clip, footstepVolume);
    }

    /// <summary>True world-space velocity, useful for animator params.</summary>
    public Vector2 Velocity => velocity;
}