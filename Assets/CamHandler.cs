using UnityEngine;

[DisallowMultipleComponent]
public class SecurityCamera2D : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Leave empty and it finds the object tagged 'Player' at startup.")]
    public Transform player;
    [Tooltip("Leave empty and it pulls the Collider2D off the player. This defines the 'body'.")]
    public Collider2D playerBody;

    [Header("Vision cone")]
    [Tooltip("How far the camera can see, in world units.")]
    public float range = 8f;
    [Tooltip("Total width of the cone in degrees. 360 sees in every direction.")]
    [Range(1f, 360f)] public float fovAngle = 70f;
    [Tooltip("Layers that block sight — walls, crates. The player's layer may safely be included.")]
    public LayerMask obstacleMask;
    [Tooltip("Nudges the eye point off the object's origin, e.g. to a lens on the front.")]
    public Vector2 eyeOffset = Vector2.zero;

    [Header("Detection")]
    [Tooltip("Body is sampled on an N x N grid. 3 = 9 sample points, plenty for a small sprite.")]
    [Range(2, 6)] public int samplesPerAxis = 3;
    [Tooltip("Fraction of those points that must be visible to count as spotted. 0.5 = half the body.")]
    [Range(0f, 1f)] public float requiredVisibleFraction = 0.5f;
    [Tooltip("Seconds the player must stay exposed before it fires. 0 = instant.")]
    public float detectionDelay = 0.35f;
    [Tooltip("Fire only once ever. Uncheck to re-arm each time the player breaks line of sight.")]
    public bool triggerOnce = true;

    [Header("Dev view")]
    public bool drawGizmos = true;
    [Tooltip("Also draw the cone as real geometry in the Game view. Turn off for release.")]
    public bool showConeInGame = false;
    [Range(8, 96)] public int coneSegments = 36;
    public Color clearColor = new Color(0.25f, 1f, 0.45f, 0.13f);
    public Color alertColor = new Color(1f, 0.25f, 0.2f, 0.28f);
    public int coneSortingOrder = -10;

    // ---- read these from other scripts if you want a UI meter or a warning sound
    public float VisibleFraction { get; private set; }
    public bool Spotted { get; private set; }

    float timer;
    bool fired;

    // Scratch buffers, allocated once so Update() never generates garbage.
    readonly Vector2[] samples = new Vector2[36];
    GameObject coneGO;
    Mesh coneMesh;
    MeshRenderer coneMR;
    Vector3[] coneVerts;
    int[] coneTris;

    // The direction the camera faces. Rotate the object's Z to aim it.
    Vector2 Forward => transform.right;
    Vector2 Origin => (Vector2)transform.position + (Vector2)(transform.rotation * eyeOffset);

    void Awake()
    {
        if (!player)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
        }
        if (!playerBody && player) playerBody = player.GetComponentInChildren<Collider2D>();
        if (!player) Debug.LogWarning(name + ": no player found. Tag your player 'Player'.", this);
    }

    void Update()
    {
        if (!player) return;

        VisibleFraction = ComputeVisibleFraction();
        Spotted = VisibleFraction >= requiredVisibleFraction && VisibleFraction > 0f;

        if (Spotted)
        {
            timer += Time.deltaTime;
            if (timer >= detectionDelay && !fired)
            {
                fired = true;
                PlayerDie();
            }
        }
        else
        {
            timer = 0f;
            if (!triggerOnce) fired = false;   // re-arm once they're out of sight
        }

        UpdateConeMesh();
    }

    /// <summary>
    /// Fires once the player has been sufficiently exposed for detectionDelay seconds.
    /// Hook up the game-over screen, respawn, alarm, etc. here.
    /// </summary>
    void PlayerDie()
    {
        Debug.Log(name + " spotted the player.", this);
        // TODO
    }

    // ---------------------------------------------------------------- detection

    // Returns 0..1: how much of the player's body this camera can currently see.
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

    // Lays an N x N grid of points over the player's collider bounds, inset slightly
    // so the corner points sit just inside the body rather than exactly on its edge.
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

    // A point counts as visible if it's in range, inside the cone, and nothing blocks the line.
    bool PointVisible(Vector2 origin, Vector2 p)
    {
        Vector2 to = p - origin;
        float dist = to.magnitude;

        if (dist > range) return false;
        if (dist > 0.0001f && Vector2.Angle(Forward, to) > fovAngle * 0.5f) return false;

        RaycastHit2D hit = Physics2D.Linecast(origin, p, obstacleMask);

        // Hitting the player themselves isn't an obstruction — it means the line got through.
        if (hit.collider && !IsPlayer(hit.collider)) return false;
        return true;
    }

    bool IsPlayer(Collider2D c)
    {
        if (c == playerBody) return true;
        return player && c.transform.IsChildOf(player);
    }

    // ---------------------------------------------------------------- game-view cone

    void UpdateConeMesh()
    {
        if (!showConeInGame)
        {
            if (coneGO) coneGO.SetActive(false);
            return;
        }

        if (!coneGO)
        {
            coneGO = new GameObject("FOV Cone");
            coneGO.transform.SetParent(transform, false);
            coneMesh = new Mesh { name = "FOVCone" };
            coneGO.AddComponent<MeshFilter>().mesh = coneMesh;
            coneMR = coneGO.AddComponent<MeshRenderer>();
            // Sprites/Default is unlit, supports transparency, and is culled off (double sided).
            coneMR.material = new Material(Shader.Find("Sprites/Default"));
        }

        coneGO.SetActive(true);
        coneMR.sortingOrder = coneSortingOrder;
        coneMR.material.color = Spotted ? alertColor : clearColor;

        int seg = Mathf.Clamp(coneSegments, 8, 96);
        if (coneVerts == null || coneVerts.Length != seg + 2)
        {
            coneVerts = new Vector3[seg + 2];
            coneTris = new int[seg * 3];
            for (int i = 0; i < seg; i++)
            {
                coneTris[i * 3] = 0;
                coneTris[i * 3 + 1] = i + 1;
                coneTris[i * 3 + 2] = i + 2;
            }
        }

        Vector2 origin = Origin;
        coneVerts[0] = transform.InverseTransformPoint(origin);

        for (int i = 0; i <= seg; i++)
        {
            float a = -fovAngle * 0.5f + fovAngle * (i / (float)seg);
            Vector2 dir = Quaternion.Euler(0f, 0f, a) * Forward;
            coneVerts[i + 1] = transform.InverseTransformPoint(origin + dir * RayLength(origin, dir));
        }

        coneMesh.Clear();
        coneMesh.vertices = coneVerts;
        coneMesh.triangles = coneTris;
        coneMesh.RecalculateBounds();
    }

    // How far a ray travels before a wall stops it. Used to clip the cone to geometry.
    float RayLength(Vector2 origin, Vector2 dir)
    {
        RaycastHit2D h = Physics2D.Raycast(origin, dir, range, obstacleMask);
        return (h.collider && !IsPlayer(h.collider)) ? h.distance : range;
    }

    // ---------------------------------------------------------------- scene gizmos

    void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        Vector2 origin = Origin;
        Gizmos.color = Application.isPlaying && Spotted
            ? new Color(1f, 0.3f, 0.25f, 0.9f)
            : new Color(0.3f, 1f, 0.5f, 0.85f);

        // Cone outline, clipped to walls so you can see exactly what it covers.
        int seg = 40;
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= seg; i++)
        {
            float a = -fovAngle * 0.5f + fovAngle * (i / (float)seg);
            Vector2 dir = Quaternion.Euler(0f, 0f, a) * Forward;
            Vector3 pt = origin + dir * RayLength(origin, dir);

            if (i == 0 || i == seg) Gizmos.DrawLine(origin, pt);   // the two edges
            if (i > 0) Gizmos.DrawLine(prev, pt);                  // the arc
            prev = pt;
        }

        Gizmos.DrawWireSphere(origin, 0.12f);

        // Short stub showing which way it's aimed.
        Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
        Gizmos.DrawLine(origin, origin + Forward * 0.6f);
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos || !player) return;

        // One line per sample point: green got through, red was blocked.
        Vector2 origin = Origin;
        int count = BuildSamplePoints();
        for (int i = 0; i < count; i++)
        {
            bool ok = PointVisible(origin, samples[i]);
            Gizmos.color = ok ? Color.green : new Color(1f, 0.35f, 0.35f, 0.55f);
            Gizmos.DrawLine(origin, samples[i]);
            Gizmos.DrawWireSphere(samples[i], 0.05f);
        }
    }
}