using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 360-degree raycast line-of-sight fog. Builds the visibility polygon around the
/// viewer, then renders everything OUTSIDE it as solid black geometry drawn on top.
/// Walls you can see are revealed; anything past them is not.
/// </summary>
[DisallowMultipleComponent]
public class FogOfWar2D : MonoBehaviour
{
    const float TAU = Mathf.PI * 2f;

    [Header("Viewer")]
    [Tooltip("Leave empty and it finds the object tagged 'Player' automatically.")]
    public Transform viewer;
    [Tooltip("How far the player can see, in world units.")]
    public float viewRadius = 9f;
    [Tooltip("Shifts the eye point off the viewer's origin if you need it.")]
    public Vector2 eyeOffset = Vector2.zero;

    [Header("Walls")]
    [Tooltip("Set this to Obstacles. Anything on these layers blocks sight.")]
    public LayerMask obstacleMask;
    [Tooltip("How far past a wall hit sight pushes, so the wall itself is revealed. Set a bit ABOVE your wall thickness.")]
    public float revealDistance = 1.5f;

    [Header("Quality")]
    [Tooltip("Evenly spaced rays that fill in the open areas. Corners get their own rays on top.")]
    [Range(8, 240)] public int baseRays = 64;
    [Tooltip("Also fire a ray exactly at each corner, not just to either side.")]
    public bool exactCornerRays = false;
    [Tooltip("Angle in radians that corner rays are split by, to catch both sides of a wall corner.")]
    public float cornerNudge = 0.0015f;
    [Tooltip("Corners closer together than this are treated as one. Stops tiled colliders emitting duplicate ray pairs.")]
    public float cornerMergeDistance = 0.05f;
    [Tooltip("Safety ceiling on rays per rebuild.")]
    public int maxRays = 2048;

    [Header("Cleanup")]
    [Tooltip("Removes isolated single-ray spikes and notches — the black slivers inside walls.")]
    public bool despeckle = true;
    [Tooltip("A ray must differ from BOTH neighbours by more than this to be treated as a spike.")]
    public float despeckleTolerance = 0.05f;
    [Tooltip("Only rays this close in angle to both neighbours are eligible. Keeps real shadow edges intact.")]
    public float despeckleMaxGap = 0.012f;

    [Header("Look")]
    [Tooltip("Softness of the circular edge at maximum view range. 0 = hard edge.")]
    [Range(0f, 2f)] public float edgeFeather = 0.35f;
    [Tooltip("Softness where sight stops against a wall. Leave at 0 for crisp edges.")]
    [Range(0f, 0.5f)] public float wallFeather = 0f;
    public Color fogColor = Color.black;
    public string sortingLayer = "Default";
    [Tooltip("Must be higher than every sprite you want hidden.")]
    public int sortingOrder = 1000;

    [Header("Performance")]
    [Tooltip("Seconds between rebuilds. 0 = every frame.")]
    public float updateInterval = 0f;
    [Tooltip("Skip the rebuild when neither the viewer nor the camera has moved. Turn OFF if walls move.")]
    public bool skipWhenStill = true;
    public float stillThreshold = 0.005f;

    [Header("Dev")]
    public bool drawRadiusGizmo = true;

    public int LastRayCount { get; private set; }
    public int LastCornerCount { get; private set; }

    // ---- preallocated buffers, so a steady-state rebuild allocates nothing
    readonly List<Collider2D> overlaps = new List<Collider2D>(64);
    readonly List<Vector2> pathBuf = new List<Vector2>(256);
    readonly RaycastHit2D[] rayHit = new RaycastHit2D[1];
    readonly Collider2D[] pointBuf = new Collider2D[1];
    readonly HashSet<long> cornerSeen = new HashSet<long>();

    float[] angles = new float[1024];
    int angleCount;
    Vector2[] dirs = new Vector2[1024];
    float[] rayAngle = new float[1024];
    float[] dists = new float[1024];
    bool[] blocked = new bool[1024];

    Vector3[] verts = new Vector3[3072];
    Color32[] cols = new Color32[3072];
    int[] tris = new int[0];
    int trisForN = -1;
    int meshVertCount = -1;

    class WallCorners
    {
        public Vector2[] local;
        public Vector2[] world;
        public Matrix4x4 matrix;
        public int touchedFrame;
    }
    readonly Dictionary<Collider2D, WallCorners> cornerCache = new Dictionary<Collider2D, WallCorners>();
    readonly List<Collider2D> pruneList = new List<Collider2D>();
    int frameStamp;

    ContactFilter2D filter;
    GameObject meshGO;
    Mesh mesh;
    MeshRenderer mr;
    float timer;
    Vector2 lastOrigin = new Vector2(float.MaxValue, float.MaxValue);
    Vector3 lastCamPos = new Vector3(float.MaxValue, 0f, 0f);
    Camera cam;

    Vector2 Origin => viewer ? (Vector2)viewer.position + eyeOffset : (Vector2)transform.position;

    // ---------------------------------------------------------------- setup

    void Awake()
    {
        if (!viewer)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) viewer = p.transform;
            else Debug.LogWarning(name + ": no viewer found. Tag your player 'Player'.", this);
        }

        cam = Camera.main;

        // Ignore trigger colliders so door interaction zones don't cast shadows.
        // The plain layerMask overloads still hit triggers, which is why every
        // query below goes through this filter instead.
        filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstacleMask,
            useTriggers = false
        };

        BuildMeshObject();
    }

    void OnDestroy()
    {
        if (meshGO) Destroy(meshGO);
        if (mesh) Destroy(mesh);
    }

    void BuildMeshObject()
    {
        meshGO = new GameObject("FogOfWar Mesh");
        meshGO.transform.SetParent(null);   // world space, never inherits viewer rotation

        mesh = new Mesh { name = "FogMesh" };
        mesh.MarkDynamic();
        meshGO.AddComponent<MeshFilter>().mesh = mesh;

        mr = meshGO.AddComponent<MeshRenderer>();
        // Sprites/Default is unlit, supports vertex alpha, and sorts with your sprites.
        mr.material = new Material(Shader.Find("Sprites/Default"));
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder = sortingOrder;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // ---------------------------------------------------------------- tick

    void LateUpdate()
    {
        if (!viewer) return;

        if (updateInterval > 0f)
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = updateInterval;
        }

        // Nothing moved, so last frame's mesh is still correct.
        if (skipWhenStill && meshVertCount > 0)
        {
            Vector2 o = Origin;
            Vector3 c = cam ? cam.transform.position : Vector3.zero;
            if ((o - lastOrigin).sqrMagnitude < stillThreshold * stillThreshold &&
                (c - lastCamPos).sqrMagnitude < stillThreshold * stillThreshold)
                return;
        }

        Rebuild();
    }

    /// <summary>Force a rebuild, e.g. after opening a door or moving a wall.</summary>
    public void ForceRebuild()
    {
        lastOrigin = new Vector2(float.MaxValue, float.MaxValue);
        if (viewer) Rebuild();
    }

    // ---------------------------------------------------------------- the build

    void Rebuild()
    {
        Vector2 origin = Origin;
        lastOrigin = origin;
        if (!cam) cam = Camera.main;
        lastCamPos = cam ? cam.transform.position : Vector3.zero;

        meshGO.transform.position = origin;
        mr.sortingOrder = sortingOrder;
        frameStamp++;

        GatherAngles(origin);
        CastRays(origin);

        int n = LastRayCount;
        if (n < 3) { mesh.Clear(); meshVertCount = -1; return; }

        if (despeckle) Despeckle(n);

        BuildMesh(origin, n);
        PruneCacheOccasionally();
    }

    // --- 1. decide which angles to fire rays at
    void GatherAngles(Vector2 origin)
    {
        // EVERY angle must live in the same 0..TAU range, or sorting interleaves
        // two passes around the circle and the polygon folds over itself.
        angleCount = 0;

        // An even ring so open floor is covered no matter what.
        EnsureAngleCapacity(baseRays);
        for (int i = 0; i < baseRays; i++)
            angles[angleCount++] = i * TAU / baseRays;

        overlaps.Clear();
        Physics2D.OverlapCircle(origin, viewRadius, filter, overlaps);

        float reach = viewRadius + revealDistance + 0.1f;
        float reachSqr = reach * reach;
        float merge = Mathf.Max(cornerMergeDistance, 1e-4f);
        float inv = 1f / merge;

        cornerSeen.Clear();
        int corners = 0;

        for (int c = 0; c < overlaps.Count; c++)
        {
            Vector2[] pts = GetWorldCorners(overlaps[c]);
            if (pts == null) continue;

            for (int i = 0; i < pts.Length; i++)
            {
                float dx = pts[i].x - origin.x;
                float dy = pts[i].y - origin.y;
                if (dx * dx + dy * dy > reachSqr) continue;   // too far to cast a visible shadow

                // Tiled colliders share corner points exactly, so without this each
                // shared corner emits four near-identical rays whose nudged pairs
                // interleave — that interleaving is what produced the black slivers.
                long key = ((long)Mathf.RoundToInt(pts[i].x * inv) << 32)
                         ^ (uint)Mathf.RoundToInt(pts[i].y * inv);
                if (!cornerSeen.Add(key)) continue;

                if (angleCount + 3 > maxRays) break;
                EnsureAngleCapacity(angleCount + 3);

                float a = Mathf.Atan2(dy, dx);   // returns -PI..PI
                if (a < 0f) a += TAU;            // normalise into 0..TAU

                angles[angleCount++] = Wrap(a - cornerNudge);
                angles[angleCount++] = Wrap(a + cornerNudge);
                if (exactCornerRays) angles[angleCount++] = a;
                corners++;
            }
        }

        LastCornerCount = corners;
        System.Array.Sort(angles, 0, angleCount);
    }

    // --- 2. fire them, skipping duplicate angles that would make zero-area wedges
    void CastRays(Vector2 origin)
    {
        EnsureRayCapacity(angleCount);

        int n = 0;
        float prev = float.NegativeInfinity;

        for (int i = 0; i < angleCount; i++)
        {
            float a = angles[i];
            if (a - prev < 1e-5f) continue;     // dedupe BEFORE paying for a raycast
            prev = a;

            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            int count = Physics2D.Raycast(origin, dir, filter, rayHit, viewRadius);

            dirs[n] = dir;
            rayAngle[n] = a;
            blocked[n] = count > 0;
            dists[n] = count > 0 ? RevealDistance(origin, dir, rayHit[0].distance) : viewRadius;
            n++;
        }

        LastRayCount = n;
    }

    /// <summary>
    /// Hit an obstacle, go a little further, stop. If the endpoint landed in open
    /// air we overshot the wall, so cast back and stop exactly on the surface we
    /// crossed. Errors are deliberately biased LONG: a ray that stops short leaves
    /// a black notch in the middle of a wall, which looks far worse than a sliver
    /// of extra reveal, and the despeckle pass cleans those up anyway.
    /// </summary>
    float RevealDistance(Vector2 origin, Vector2 dir, float hitDist)
    {
        if (revealDistance <= 0f) return hitDist;

        float limit = hitDist + revealDistance;
        Vector2 end = origin + dir * limit;

        // Still buried in the wall: the whole extension is inside solid geometry.
        if (Physics2D.OverlapPoint(end, filter, pointBuf) > 0) return limit;

        // Overshot. Starting in open air makes the backward ray unambiguous — it
        // doesn't depend on the Queries Start In Colliders project setting — and it
        // lands exactly on the wall's outer surface.
        if (Physics2D.Raycast(end, -dir, filter, rayHit, revealDistance + 0.01f) > 0)
            return limit - rayHit[0].distance;

        // Backward ray missed, which only happens when the forward ray clipped a
        // corner tip. Keep the full push rather than collapsing to the hit point.
        return limit;
    }

    /// <summary>
    /// Removes single-ray outliers: a ray whose distance differs from BOTH its
    /// angular neighbours, where both neighbours sit within a hair of it. Real
    /// shadow edges always run across many consecutive rays, so they're untouched.
    /// </summary>
    void Despeckle(int n)
    {
        if (n < 5) return;

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                int p = i == 0 ? n - 1 : i - 1;
                int q = i == n - 1 ? 0 : i + 1;

                float gp = Mathf.Abs(Mathf.DeltaAngle(rayAngle[p] * Mathf.Rad2Deg,
                                                      rayAngle[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                float gq = Mathf.Abs(Mathf.DeltaAngle(rayAngle[i] * Mathf.Rad2Deg,
                                                      rayAngle[q] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (gp > despeckleMaxGap || gq > despeckleMaxGap) continue;

                float lo = Mathf.Min(dists[p], dists[q]);
                float hi = Mathf.Max(dists[p], dists[q]);

                if (dists[i] > hi + despeckleTolerance) dists[i] = hi;        // light spike
                else if (dists[i] < lo - despeckleTolerance) dists[i] = lo;   // black notch
            }
        }
    }

    // --- 3. build the INVERSE of that polygon
    void BuildMesh(Vector2 origin, int n)
    {
        // Three rings of vertices along each ray:
        //   inner (transparent) -> boundary (opaque) -> far offscreen (opaque)
        // The gap in the middle is what you can see through.
        float maxDist = 0f;
        for (int i = 0; i < n; i++) if (dists[i] > maxDist) maxDist = dists[i];
        float far = Mathf.Max(FarDistance(origin), maxDist + 2f);

        int total = n * 3;
        EnsureVertCapacity(total);

        byte r8 = (byte)(fogColor.r * 255f);
        byte g8 = (byte)(fogColor.g * 255f);
        byte b8 = (byte)(fogColor.b * 255f);
        Color32 clear = new Color32(r8, g8, b8, 0);
        Color32 solid = new Color32(r8, g8, b8, (byte)(fogColor.a * 255f));

        for (int i = 0; i < n; i++)
        {
            // Feather inwards so the fog is fully opaque by the time it reaches the
            // boundary. Fading outwards would let you see through walls. Wall hits
            // get no feather by default; the open max-range edge gets a soft one.
            float f = blocked[i] ? wallFeather : edgeFeather;
            float inner = dists[i] - f;
            if (inner < 0f) inner = 0f;

            Vector2 d = dirs[i];
            verts[i]         = new Vector3(d.x * inner,    d.y * inner,    0f);
            verts[n + i]     = new Vector3(d.x * dists[i], d.y * dists[i], 0f);
            verts[2 * n + i] = new Vector3(d.x * far,      d.y * far,      0f);
            cols[i]          = clear;
            cols[n + i]      = solid;
            cols[2 * n + i]  = solid;
        }

        // Indices depend only on the ray count, so they're rebuilt only when that
        // changes. Most frames this is skipped entirely.
        bool topologyChanged = trisForN != n;
        if (topologyChanged) BuildTriangles(n);

        if (meshVertCount != total)
        {
            mesh.Clear();
            meshVertCount = total;
            topologyChanged = true;
        }

        mesh.SetVertices(verts, 0, total);
        mesh.SetColors(cols, 0, total);
        if (topologyChanged) mesh.SetTriangles(tris, 0, n * 12, 0, false);

        // Set bounds by hand so the mesh is never frustum-culled at screen edges.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(far * 2.2f, far * 2.2f, 1f));
    }

    void BuildTriangles(int n)
    {
        trisForN = n;
        int need = n * 12;
        if (tris.Length < need) tris = new int[Mathf.NextPowerOfTwo(need)];

        int t = 0;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;            // wrap the last wedge back to the first
            int a0 = i,         a1 = j;
            int b0 = n + i,     b1 = n + j;
            int c0 = 2 * n + i, c1 = 2 * n + j;

            tris[t++] = a0; tris[t++] = b0; tris[t++] = b1;   // feather band
            tris[t++] = a0; tris[t++] = b1; tris[t++] = a1;
            tris[t++] = b0; tris[t++] = c0; tris[t++] = c1;   // solid band out to offscreen
            tris[t++] = b0; tris[t++] = c1; tris[t++] = b1;
        }
    }

    // How far out the fog must reach to cover the whole screen.
    float FarDistance(Vector2 origin)
    {
        if (cam && cam.orthographic)
        {
            float h = cam.orthographicSize;
            float w = h * cam.aspect;
            float offset = Vector2.Distance(origin, cam.transform.position);
            return offset + Mathf.Sqrt(w * w + h * h) + 2f;
        }
        return 200f;
    }

    static float Wrap(float a)
    {
        if (a < 0f) return a + TAU;
        if (a >= TAU) return a - TAU;
        return a;
    }

    // ---------------------------------------------------------------- corner cache

    /// <summary>
    /// World-space corner points for a collider. Local points are extracted once;
    /// world points are re-baked only when that collider's transform actually moves.
    /// </summary>
    Vector2[] GetWorldCorners(Collider2D col)
    {
        if (!col) return null;

        if (!cornerCache.TryGetValue(col, out WallCorners wc))
        {
            Vector2[] local = ExtractLocalCorners(col);
            if (local == null || local.Length == 0) return null;

            wc = new WallCorners
            {
                local = local,
                world = new Vector2[local.Length],
                matrix = Matrix4x4.zero     // forces the first bake below
            };
            cornerCache[col] = wc;
        }

        wc.touchedFrame = frameStamp;

        Matrix4x4 m = col.transform.localToWorldMatrix;
        if (m != wc.matrix)
        {
            wc.matrix = m;
            for (int i = 0; i < wc.local.Length; i++)
                wc.world[i] = m.MultiplyPoint3x4(wc.local[i]);
        }
        return wc.world;
    }

    // Pulls the corner points out of whatever collider shape the wall uses,
    // in the collider's own local space with its offset already folded in.
    Vector2[] ExtractLocalCorners(Collider2D col)
    {
        if (col is BoxCollider2D box)
        {
            Vector2 e = box.size * 0.5f;
            Vector2 o = box.offset;
            return new[]
            {
                o + new Vector2(-e.x, -e.y),
                o + new Vector2( e.x, -e.y),
                o + new Vector2( e.x,  e.y),
                o + new Vector2(-e.x,  e.y),
            };
        }

        if (col is PolygonCollider2D poly)
        {
            var acc = new List<Vector2>(poly.GetTotalPointCount());
            for (int i = 0; i < poly.pathCount; i++)
            {
                pathBuf.Clear();
                poly.GetPath(i, pathBuf);
                for (int k = 0; k < pathBuf.Count; k++) acc.Add(pathBuf[k] + poly.offset);
            }
            return acc.ToArray();
        }

        // This is what a Tilemap Collider 2D merges down into.
        if (col is CompositeCollider2D comp)
        {
            var acc = new List<Vector2>(comp.pointCount);
            for (int i = 0; i < comp.pathCount; i++)
            {
                pathBuf.Clear();
                comp.GetPath(i, pathBuf);
                for (int k = 0; k < pathBuf.Count; k++) acc.Add(pathBuf[k] + comp.offset);
            }
            return acc.ToArray();
        }

        if (col is EdgeCollider2D edge)
        {
            Vector2[] pts = edge.points;
            var acc = new Vector2[pts.Length];
            for (int k = 0; k < pts.Length; k++) acc[k] = pts[k] + edge.offset;
            return acc;
        }

        if (col is CircleCollider2D circle)
        {
            // No corners on a circle, so ring it with sample points instead.
            var acc = new Vector2[12];
            for (int k = 0; k < 12; k++)
            {
                float a = k * TAU / 12f;
                acc[k] = circle.offset + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * circle.radius;
            }
            return acc;
        }

        // Anything else (capsule, etc): fall back to its local bounding box.
        Bounds b = col.bounds;
        Matrix4x4 inv = col.transform.worldToLocalMatrix;
        return new[]
        {
            (Vector2)inv.MultiplyPoint3x4(new Vector3(b.min.x, b.min.y)),
            (Vector2)inv.MultiplyPoint3x4(new Vector3(b.max.x, b.min.y)),
            (Vector2)inv.MultiplyPoint3x4(new Vector3(b.max.x, b.max.y)),
            (Vector2)inv.MultiplyPoint3x4(new Vector3(b.min.x, b.max.y)),
        };
    }

    // Drop entries for colliders we haven't seen in a while, so the cache doesn't
    // grow forever in a big level or hold references to destroyed objects.
    void PruneCacheOccasionally()
    {
        if (cornerCache.Count < 128 || (frameStamp & 255) != 0) return;

        pruneList.Clear();
        foreach (var kv in cornerCache)
            if (!kv.Key || frameStamp - kv.Value.touchedFrame > 600) pruneList.Add(kv.Key);
        for (int i = 0; i < pruneList.Count; i++) cornerCache.Remove(pruneList[i]);
    }

    /// <summary>Call if you rebuild or resize wall colliders at runtime.</summary>
    public void ClearCornerCache() => cornerCache.Clear();

    // ---------------------------------------------------------------- capacity

    void EnsureAngleCapacity(int need)
    {
        if (angles.Length >= need) return;
        System.Array.Resize(ref angles, Mathf.NextPowerOfTwo(need));
    }

    void EnsureRayCapacity(int need)
    {
        if (dirs.Length >= need) return;
        int size = Mathf.NextPowerOfTwo(need);
        dirs = new Vector2[size];
        rayAngle = new float[size];
        dists = new float[size];
        blocked = new bool[size];
    }

    void EnsureVertCapacity(int need)
    {
        if (verts.Length >= need) return;
        int size = Mathf.NextPowerOfTwo(need);
        verts = new Vector3[size];
        cols = new Color32[size];
        meshVertCount = -1;     // force a full re-upload after the resize
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Can the viewer currently see this world point? Handy for enemy AI,
    /// or for hiding objects rather than just covering them with fog.
    /// </summary>
    public bool IsVisible(Vector2 worldPoint)
    {
        Vector2 origin = Origin;
        Vector2 to = worldPoint - origin;
        float d = to.sqrMagnitude;
        if (d > viewRadius * viewRadius) return false;
        if (d < 1e-6f) return true;

        d = Mathf.Sqrt(d);
        return Physics2D.Raycast(origin, to / d, filter, rayHit, d) == 0;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawRadiusGizmo) return;
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.8f);
        Gizmos.DrawWireSphere(Origin, viewRadius);
    }
}