using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 360-degree raycast line-of-sight fog. Builds the visibility polygon around the
/// viewer, then renders everything OUTSIDE it as solid black geometry drawn on top.
/// Wall tiles you can see are revealed whole; anything past them is not.
/// </summary>
[DisallowMultipleComponent]
public class FogOfWar2D : MonoBehaviour
{
    const float TAU = Mathf.PI * 2f;
    const float EPS = 1e-4f;

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

    [Header("Wall reveal — grid snapped")]
    [Tooltip("Reveal whole tiles instead of pushing a distance into the wall. Keeps every edge on a tile boundary.")]
    public bool snapToTileGrid = true;
    [Tooltip("Optional. Drag your Tilemap's Grid here and the two fields below are filled in for you.")]
    public Grid tileGrid;
    [Tooltip("Tile size in world units.")]
    public float tileSize = 1f;
    [Tooltip("World position of the corner of cell (0,0).")]
    public Vector2 gridOrigin = Vector2.zero;
    [Tooltip("How many solid tiles deep sight reaches. 1 reveals the wall face only.")]
    [Range(1, 4)] public int maxWallTiles = 1;
    [Tooltip("Emit extra rays at the outer corners of revealed tiles, so those corners come out square.")]
    public bool gridCornerRays = true;

    [Tooltip("Only used when Snap To Tile Grid is off: how far sight pushes into a wall.")]
    public float wallRevealDepth = 1f;

    [Header("Quality")]
    [Tooltip("Evenly spaced rays that fill in the open areas. Corners get their own rays on top.")]
    [Range(8, 240)] public int baseRays = 64;
    [Tooltip("Also fire a ray exactly at each corner, not just to either side.")]
    public bool exactCornerRays = false;
    [Tooltip("Softness of the circular edge at maximum view range. 0 = hard edge.")]
    [Range(0f, 2f)] public float edgeFeather = 0.35f;
    [Tooltip("Softness where sight stops against a wall. Leave at 0 for crisp tile edges.")]
    [Range(0f, 0.5f)] public float wallFeather = 0f;
    [Tooltip("Angle in radians that corner rays are split by, to catch both sides of a wall corner.")]
    public float cornerNudge = 0.0015f;
    [Tooltip("Safety ceiling on rays per rebuild.")]
    public int maxRays = 2048;

    [Header("Look")]
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

    // ---- preallocated buffers, so a steady-state rebuild allocates nothing
    readonly List<Collider2D> overlaps = new List<Collider2D>(64);
    readonly List<Vector2> pathBuf = new List<Vector2>(256);
    readonly RaycastHit2D[] rayHit = new RaycastHit2D[1];
    readonly Collider2D[] pointBuf = new Collider2D[1];

    float[] angles = new float[1024];
    int angleCount;
    Vector2[] dirs = new Vector2[1024];
    float[] dists = new float[1024];
    bool[] blocked = new bool[1024];

    Vector3[] verts = new Vector3[3072];
    Color32[] cols = new Color32[3072];
    int[] tris = new int[0];
    int trisForN = -1;
    int meshVertCount = -1;

    // Per-rebuild memo of which grid cells are solid. Many rays march the same
    // cells, and a dictionary hit is far cheaper than a physics query.
    readonly Dictionary<long, bool> cellSolid = new Dictionary<long, bool>(256);

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
        AdoptGrid();

        // Ignore trigger colliders so door interaction zones don't cast shadows.
        // The plain layerMask overloads of Raycast/OverlapPoint still hit triggers,
        // which is why every query below goes through this filter instead.
        filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstacleMask,
            useTriggers = false
        };

        BuildMeshObject();
    }

    void AdoptGrid()
    {
        if (!tileGrid) return;
        tileSize = Mathf.Abs(tileGrid.cellSize.x) > 1e-4f ? tileGrid.cellSize.x : tileSize;
        gridOrigin = tileGrid.transform.position;
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
        cellSolid.Clear();

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

        EnsureAngleCapacity(baseRays);
        for (int i = 0; i < baseRays; i++)
            angles[angleCount++] = i * TAU / baseRays;

        overlaps.Clear();
        Physics2D.OverlapCircle(origin, viewRadius, filter, overlaps);

        float pad = snapToTileGrid ? tileSize * (maxWallTiles + 1) : wallRevealDepth + 0.1f;
        float reach = viewRadius + pad;
        float reachSqr = reach * reach;

        for (int c = 0; c < overlaps.Count; c++)
        {
            Vector2[] pts = GetWorldCorners(overlaps[c]);
            if (pts == null) continue;

            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i];
                float dx = p.x - origin.x, dy = p.y - origin.y;
                if (dx * dx + dy * dy > reachSqr) continue;

                // Two rays straddling the corner. These are what slip past it and
                // define the shadow edge; the exact-angle ray between them is
                // almost always redundant.
                AddCornerAngles(origin, p);

                // The revealed band sticks out one tile past the wall outline, so
                // its outer corners sit a tile diagonally out. Aiming rays there is
                // what makes those corners come out square instead of chamfered.
                if (snapToTileGrid && gridCornerRays)
                {
                    float t = tileSize * maxWallTiles;
                    AddAngle(origin, p.x - t, p.y - t, reachSqr);
                    AddAngle(origin, p.x + t, p.y - t, reachSqr);
                    AddAngle(origin, p.x + t, p.y + t, reachSqr);
                    AddAngle(origin, p.x - t, p.y + t, reachSqr);
                }
            }
        }

        System.Array.Sort(angles, 0, angleCount);
    }

    void AddCornerAngles(Vector2 origin, Vector2 p)
    {
        if (angleCount + 3 > maxRays) return;
        EnsureAngleCapacity(angleCount + 3);

        float a = Mathf.Atan2(p.y - origin.y, p.x - origin.x);   // -PI..PI
        if (a < 0f) a += TAU;                                     // normalise to 0..TAU

        angles[angleCount++] = Wrap(a - cornerNudge);
        angles[angleCount++] = Wrap(a + cornerNudge);
        if (exactCornerRays) angles[angleCount++] = a;
    }

    void AddAngle(Vector2 origin, float x, float y, float reachSqr)
    {
        if (angleCount + 1 > maxRays) return;
        float dx = x - origin.x, dy = y - origin.y;
        if (dx * dx + dy * dy > reachSqr) return;

        EnsureAngleCapacity(angleCount + 1);
        float a = Mathf.Atan2(dy, dx);
        if (a < 0f) a += TAU;
        angles[angleCount++] = a;
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
            blocked[n] = count > 0;
            dists[n] = count > 0 ? RevealDistance(origin, dir, rayHit[0]) : viewRadius;
            n++;
        }

        LastRayCount = n;
    }

    /// <summary>
    /// How far sight reaches once it has hit a wall. Walks the tile grid from the
    /// hit point and stops at the first cell that isn't solid, so the boundary
    /// always lands exactly on a tile edge — never mid-wall, never in open floor.
    /// </summary>
    float RevealDistance(Vector2 origin, Vector2 dir, RaycastHit2D hit)
    {
        if (!snapToTileGrid) return LegacyReveal(origin, dir, hit);
        if (tileSize <= 1e-4f) return hit.distance;

        float d = hit.distance;

        for (int step = 0; step < maxWallTiles; step++)
        {
            // Sample just past the current boundary to identify the next cell.
            float px = origin.x + dir.x * (d + EPS);
            float py = origin.y + dir.y * (d + EPS);

            int cx = Mathf.FloorToInt((px - gridOrigin.x) / tileSize);
            int cy = Mathf.FloorToInt((py - gridOrigin.y) / tileSize);

            if (!CellSolid(cx, cy)) break;      // stepped out of the wall: done

            float exit = CellExit(origin, dir, cx, cy);
            if (exit <= d + EPS) break;         // numerical safety, never go backwards
            d = exit;
        }

        return d;
    }

    // Distance along the ray at which it leaves cell (cx, cy). Standard slab test —
    // exact, so the result is precisely on a grid line.
    float CellExit(Vector2 origin, Vector2 dir, int cx, int cy)
    {
        float minX = gridOrigin.x + cx * tileSize;
        float minY = gridOrigin.y + cy * tileSize;
        float maxX = minX + tileSize;
        float maxY = minY + tileSize;

        float tx = float.MaxValue, ty = float.MaxValue;
        if (dir.x > 1e-6f)       tx = (maxX - origin.x) / dir.x;
        else if (dir.x < -1e-6f) tx = (minX - origin.x) / dir.x;
        if (dir.y > 1e-6f)       ty = (maxY - origin.y) / dir.y;
        else if (dir.y < -1e-6f) ty = (minY - origin.y) / dir.y;

        return tx < ty ? tx : ty;
    }

    // Is this grid cell inside a wall? Tested at the cell centre, so internal
    // seams between per-tile colliders are invisible to it — which is exactly
    // why this doesn't suffer the artifacts a surface probe does.
    bool CellSolid(int cx, int cy)
    {
        long key = ((long)cx << 32) ^ (uint)cy;
        if (cellSolid.TryGetValue(key, out bool solid)) return solid;

        Vector2 c = new Vector2(gridOrigin.x + (cx + 0.5f) * tileSize,
                                gridOrigin.y + (cy + 0.5f) * tileSize);
        solid = Physics2D.OverlapPoint(c, filter, pointBuf) > 0;

        cellSolid[key] = solid;
        return solid;
    }

    // Non-grid fallback: push a perpendicular depth in, clamped to the far face.
    float LegacyReveal(Vector2 origin, Vector2 dir, RaycastHit2D hit)
    {
        if (wallRevealDepth <= 0f) return hit.distance;

        float cos = Mathf.Abs(dir.x * hit.normal.x + dir.y * hit.normal.y);
        float cap = wallRevealDepth * 1.6f;
        float along = cos > 1e-4f ? wallRevealDepth / cos : cap;
        if (along > cap) along = cap;

        float probe = along + 0.05f;
        Vector2 start = origin + dir * (hit.distance + probe);
        if (Physics2D.Raycast(start, -dir, filter, rayHit, probe) > 0)
        {
            float thickness = probe - rayHit[0].distance;
            if (thickness < 0f) thickness = 0f;
            if (thickness < along) along = thickness;
        }
        return hit.distance + along;
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

        // Re-bake world positions only when the collider has actually moved.
        Matrix4x4 m = col.transform.localToWorldMatrix;
        if (m != wc.matrix)
        {
            wc.matrix = m;
            for (int i = 0; i < wc.local.Length; i++)
                wc.world[i] = m.MultiplyPoint3x4(wc.local[i]);
        }
        return wc.world;
    }

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
            var acc = new Vector2[12];
            for (int k = 0; k < 12; k++)
            {
                float a = k * TAU / 12f;
                acc[k] = circle.offset + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * circle.radius;
            }
            return acc;
        }

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

    /// <summary>Can the viewer currently see this world point? Useful for AI.</summary>
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