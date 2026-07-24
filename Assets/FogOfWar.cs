using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 360-degree raycast line-of-sight fog.
///
/// Each ray stops EXACTLY at the wall's near face — so every boundary point is a
/// real hit (or the view radius), and there is nothing to extend, clip, or clean.
/// Wall tiles are revealed by DRAW ORDER, not by pushing sight into them: the fog
/// renders UNDER the walls, so a wall sprite paints over its own shadow and always
/// shows in full. Black can never land on a collideable because the collideable is
/// drawn after the fog.
///
/// Sorting order, low to high:
///   floor (e.g. 0)  <  fog (sortingOrder)  <  walls (wallSortingOrder)  <  actors
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

    [Header("Quality")]
    [Tooltip("Evenly spaced rays that fill in the open areas. Corners get their own rays on top.")]
    [Range(8, 360)] public int baseRays = 90;
    [Tooltip("Also fire a ray exactly at each corner, not just to either side.")]
    public bool exactCornerRays = true;
    [Tooltip("Angle in radians that corner rays are split by, to catch both sides of a wall corner.")]
    [Range(0.0002f, 0.01f)] public float cornerNudge = 0.0015f;
    [Tooltip("Safety ceiling on rays per rebuild, so a pathological scene can't stall the frame.")]
    public int maxRays = 4096;

    [Header("Look")]
    [Tooltip("Softness of the circular edge at maximum view range. 0 = hard edge.")]
    [Range(0f, 2f)] public float edgeFeather = 0.35f;
    public Color fogColor = Color.black;
    public string sortingLayer = "Default";
    [Tooltip("Fog draws at THIS order. It must sit ABOVE the floor and BELOW the walls.")]
    public int sortingOrder = 500;

    [Header("Draw-order safeguard")]
    [Tooltip("Your wall tilemap/sprites' Order in Layer. Only used to warn you if fog would cover them.")]
    public int wallSortingOrder = 1000;
    [Tooltip("Log a one-time warning if fog order isn't safely below wall order.")]
    public bool checkSortingOrder = true;

    [Header("Performance")]
    [Tooltip("Seconds between rebuilds. 0 = every frame.")]
    public float updateInterval = 0f;
    [Tooltip("Skip the rebuild when neither the viewer nor the camera has moved. Turn OFF if walls move.")]
    public bool skipWhenStill = true;
    public float stillThreshold = 0.005f;

    [Header("Dev")]
    public bool drawRadiusGizmo = true;

    public int LastRayCount { get; private set; }

    // ---- preallocated buffers, so a steady-state rebuild allocates nothing
    readonly List<Collider2D> overlaps = new List<Collider2D>(64);
    readonly List<Vector2> pathBuf = new List<Vector2>(256);
    readonly RaycastHit2D[] rayHit = new RaycastHit2D[1];

    float[] angles = new float[1024];
    int angleCount;
    Vector2[] dirs = new Vector2[1024];
    float[] dists = new float[1024];

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
    bool warnedSorting;
    bool warnedNoMask;

    Vector2 Origin => viewer ? (Vector2)viewer.position + eyeOffset : (Vector2)transform.position;

    // ---------------------------------------------------------------- setup

    void Awake()
    {
        if (!viewer)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) viewer = p.transform;
            else Debug.LogWarning(name + ": no viewer found. Tag your player 'Player' or assign Viewer.", this);
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

        if (!ValidateSetup()) return;

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

    // Cheap guards that catch the mistakes that actually happen, each warned once.
    bool ValidateSetup()
    {
        if (obstacleMask.value == 0 && !warnedNoMask)
        {
            warnedNoMask = true;
            Debug.LogWarning(name + ": Obstacle Mask is empty — set it to your Obstacles layer or " +
                             "no walls will block sight.", this);
        }

        if (checkSortingOrder && !warnedSorting && sortingOrder >= wallSortingOrder)
        {
            warnedSorting = true;
            Debug.LogWarning(name + $": fog Sorting Order ({sortingOrder}) is not below Wall Sorting Order " +
                             $"({wallSortingOrder}). Wall tiles will be covered by fog. Lower the fog order " +
                             "or raise your walls' Order in Layer.", this);
        }

        return viewRadius > 0.01f;
    }

    /// <summary>Force a rebuild, e.g. after opening a door or moving a wall.</summary>
    public void ForceRebuild()
    {
        lastOrigin = new Vector2(float.MaxValue, float.MaxValue);
        if (viewer && ValidateSetup()) Rebuild();
    }

    // ---------------------------------------------------------------- the build

    void Rebuild()
    {
        Vector2 origin = Origin;
        lastOrigin = origin;
        if (!cam) cam = Camera.main;
        lastCamPos = cam ? cam.transform.position : Vector3.zero;

        meshGO.transform.position = origin;
        mr.sortingLayerName = sortingLayer;   // picked up if changed at runtime
        mr.sortingOrder = sortingOrder;
        frameStamp++;

        GatherAngles(origin);
        CastRays(origin);

        int n = LastRayCount;
        if (n < 3) { mesh.Clear(); meshVertCount = -1; return; }

        BuildMesh(origin, n);
        PruneCacheOccasionally();
    }

    // --- 1. decide which angles to fire rays at
    void GatherAngles(Vector2 origin)
    {
        // EVERY angle must live in the same 0..TAU range, or sorting interleaves
        // two passes around the circle and the polygon folds over itself.
        angleCount = 0;

        int rays = Mathf.Clamp(baseRays, 8, 360);
        EnsureAngleCapacity(rays);
        for (int i = 0; i < rays; i++)
            angles[angleCount++] = i * TAU / rays;

        // Plus rays straddling every wall corner in range. Those are what slip past
        // the corner and give the shadow its crisp edge. cornerNudge is clamped so a
        // zero or negative value in the Inspector can't collapse the pair.
        float nudge = Mathf.Max(cornerNudge, 1e-4f);

        overlaps.Clear();
        Physics2D.OverlapCircle(origin, viewRadius, filter, overlaps);

        float reachSqr = (viewRadius + 0.5f) * (viewRadius + 0.5f);

        for (int c = 0; c < overlaps.Count; c++)
        {
            Vector2[] pts = GetWorldCorners(overlaps[c]);
            if (pts == null) continue;

            for (int i = 0; i < pts.Length; i++)
            {
                float dx = pts[i].x - origin.x;
                float dy = pts[i].y - origin.y;
                float dsq = dx * dx + dy * dy;
                if (dsq > reachSqr || dsq < 1e-8f) continue;   // out of range, or corner on top of us

                if (angleCount + 3 > maxRays) break;
                EnsureAngleCapacity(angleCount + 3);

                float a = Mathf.Atan2(dy, dx);   // returns -PI..PI
                if (a < 0f) a += TAU;            // normalise into 0..TAU

                angles[angleCount++] = Wrap(a - nudge);
                angles[angleCount++] = Wrap(a + nudge);
                if (exactCornerRays) angles[angleCount++] = a;
            }
            if (angleCount + 3 > maxRays) break;
        }

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
            dirs[n] = dir;
            dists[n] = RayDistance(origin, dir);
            n++;
        }

        LastRayCount = n;
    }

    /// <summary>
    /// Stop exactly at the wall's near face, or at the view radius if nothing is hit.
    /// The result is always a real, on-surface distance — never an extension into or
    /// past a wall — which is precisely why this version can't produce slivers,
    /// notches, or offset shadow lines. Wall tiles are shown by draw order instead.
    /// </summary>
    float RayDistance(Vector2 origin, Vector2 dir)
    {
        int count = Physics2D.Raycast(origin, dir, filter, rayHit, viewRadius);
        if (count == 0) return viewRadius;

        float d = rayHit[0].distance;
        // Clamp into a sane range so a degenerate hit can't emit a bad vertex.
        if (d < 0f) return 0f;
        return d < viewRadius ? d : viewRadius;
    }

    // --- 3. build the INVERSE of the visibility polygon
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
            // Feather only the open max-range edge; wall hits get a hard edge so the
            // shadow line sits crisply on the near face. (A hit is anything shorter
            // than the view radius.)
            bool openEdge = dists[i] >= viewRadius - 0.001f;
            float f = openEdge ? edgeFeather : 0f;
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
            return acc.Count > 0 ? acc.ToArray() : null;
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
            return acc.Count > 0 ? acc.ToArray() : null;
        }

        if (col is EdgeCollider2D edge)
        {
            Vector2[] pts = edge.points;
            if (pts == null || pts.Length == 0) return null;
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
        dists = new float[size];
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