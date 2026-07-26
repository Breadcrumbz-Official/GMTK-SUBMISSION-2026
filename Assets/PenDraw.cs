using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A cursor-following pen. Hold LEFT click to draw, RIGHT click erases everything.
/// Strokes are custom mesh ribbons so width can vary along the line — that's what
/// gives the calligraphy nib its thick/thin. Pick a Style preset, or set Style to
/// Custom to expose and tune every knob yourself.
/// </summary>
public class PenDraw : MonoBehaviour
{
    public enum PenStyle { Marker, Calligraphy, Custom }

    [Header("Camera")]
    [Tooltip("Camera used to turn the mouse into world space. Empty = Camera.main.")]
    public Camera cam;

    [Header("Pen")]
    [Tooltip("Optional. Draw from this point instead of the object center — set to the nib. Empty = this transform.")]
    public Transform penTip;
    [Tooltip("Z the ink sits at. Keep in front of your sprites for 2D.")]
    public float penZ = 0f;
    [Tooltip("Hide the OS cursor while active.")]
    public bool hideSystemCursor = false;

    [Header("Style")]
    [Tooltip("Marker = uniform round line. Calligraphy = angled flat nib. Custom = use every field below.")]
    public PenStyle style = PenStyle.Marker;

    [Header("Ink")]
    [Tooltip("Line color.")]
    public Color inkColor = Color.red;
    [Tooltip("Base thickness in world units. ~0.15 is a chunky marker.")]
    public float width = 0.15f;

    [Header("Calligraphy (used by Calligraphy & Custom)")]
    [Tooltip("Flat-nib orientation in degrees. The stroke is thickest moving across it, thinnest moving along it.")]
    public float nibAngle = 45f;
    [Tooltip("0 = perfectly uniform (marker). 1 = full flat-nib thick/thin. Custom only; presets force 0 or 1.")]
    [Range(0f, 1f)] public float calligraphyAmount = 1f;
    [Tooltip("Thinnest the thin parts get, as a fraction of width. Stops strokes vanishing to nothing.")]
    [Range(0.01f, 1f)] public float minWidthRatio = 0.1f;

    [Header("Shape")]
    [Tooltip("Round the start/end of a stroke. Auto-on for Marker, off for Calligraphy, your call in Custom.")]
    public bool roundCaps = true;
    [Tooltip("Triangles per rounded cap. Higher = smoother.")]
    [Range(3, 24)] public int capSegments = 10;
    [Tooltip("Minimum distance the tip must move before a new point is laid down.")]
    public float minPointDistance = 0.03f;

    [Header("Sorting")]
    public string sortingLayer = "Default";
    [Tooltip("Higher draws on top. Bump above your ground/props.")]
    public int sortingOrder = 10;

    [Header("Erase")]
    [Tooltip("Right click clears every stroke.")]
    public bool rightClickErases = true;

    // ---- runtime ----
    class Stroke
    {
        public GameObject go;
        public Mesh mesh;
        public MeshFilter mf;
        public readonly List<Vector2> pts = new List<Vector2>();
        // params captured at stroke start so changing the inspector mid-draw is safe
        public float width, nibAngle, callig, minRatio;
        public bool caps;
        public int capSeg;
    }

    readonly List<Stroke> strokes = new List<Stroke>();
    Stroke current;
    Vector2 lastPoint;
    Material inkMaterial;
    MaterialPropertyBlock mpb;

    void Awake()
    {
        if (!cam) cam = Camera.main;

        Shader sh = Shader.Find("Sprites/Default");   // flat + double-sided, tints via _Color
        if (!sh) sh = Shader.Find("Unlit/Color");
        inkMaterial = new Material(sh);
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()  { if (hideSystemCursor) Cursor.visible = false; }
    void OnDisable() { if (hideSystemCursor) Cursor.visible = true; }

    void Update()
    {
        if (!cam) { cam = Camera.main; if (!cam) return; }

        Vector3 world = MouseWorld();
        transform.position = world;

        Vector3 tip3 = penTip ? penTip.position : transform.position;
        Vector2 tip = new Vector2(tip3.x, tip3.y);

        if (LeftPressedThisFrame())      BeginStroke(tip);
        else if (LeftHeld() && current != null) ExtendStroke(tip);
        else if (LeftReleasedThisFrame()) current = null;

        if (rightClickErases && RightPressedThisFrame()) ClearAll();
    }

    // ---------------------------------------------------------------- strokes

    void BeginStroke(Vector2 tip)
    {
        var s = new Stroke();
        s.go = new GameObject("Stroke");
        s.go.transform.SetParent(transform.parent, true);
        s.go.transform.position = new Vector3(0f, 0f, penZ);

        s.mf = s.go.AddComponent<MeshFilter>();
        var mr = s.go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = inkMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingLayerName = sortingLayer;
        mr.sortingOrder = sortingOrder;

        mpb.Clear();
        mpb.SetColor("_Color", inkColor);
        mr.SetPropertyBlock(mpb);

        s.mesh = new Mesh { name = "InkStroke" };
        s.mf.mesh = s.mesh;

        // Resolve style into concrete params so presets and Custom share one code path.
        ResolveParams(out s.width, out s.nibAngle, out s.callig, out s.minRatio, out s.caps);
        s.capSeg = capSegments;

        s.pts.Add(tip);
        current = s;
        lastPoint = tip;
        strokes.Add(s);

        Rebuild(s);   // shows a dot on a plain click
    }

    void ExtendStroke(Vector2 tip)
    {
        if ((tip - lastPoint).sqrMagnitude < minPointDistance * minPointDistance)
        {
            // glue the trailing point to the tip for a live feel without spamming verts
            if (current.pts.Count >= 2) current.pts[current.pts.Count - 1] = tip;
            else current.pts.Add(tip);
            Rebuild(current);
            return;
        }
        current.pts.Add(tip);
        lastPoint = tip;
        Rebuild(current);
    }

    void ResolveParams(out float w, out float ang, out float cal, out float minR, out bool caps)
    {
        w = Mathf.Max(0.001f, width);
        ang = nibAngle;
        minR = minWidthRatio;
        switch (style)
        {
            case PenStyle.Marker:      cal = 0f; caps = true;  break;
            case PenStyle.Calligraphy: cal = 1f; caps = false; break;
            default:                   cal = calligraphyAmount; caps = roundCaps; break; // Custom
        }
    }

    public void ClearAll()
    {
        foreach (var s in strokes) if (s.go) Destroy(s.go);
        strokes.Clear();
        current = null;
    }

    // ---------------------------------------------------------------- mesh build

    void Rebuild(Stroke s)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();

        int n = s.pts.Count;
        float half = s.width * 0.5f;
        float floorHalf = half * s.minRatio;
        Vector2 nibDir = new Vector2(Mathf.Cos(s.nibAngle * Mathf.Deg2Rad),
                                     Mathf.Sin(s.nibAngle * Mathf.Deg2Rad));

        if (n == 1)
        {
            // A single click leaves a mark: a disk for markers, a nib dab for calligraphy.
            if (s.callig < 0.5f) AddDisk(verts, tris, s.pts[0], half, Mathf.Max(6, s.capSeg * 2));
            else AddQuad(verts, tris, s.pts[0], nibDir * half, Perp(nibDir) * floorHalf);
            Commit(s, verts, tris);
            return;
        }

        // Build a ribbon: for each point compute a left/right edge offset.
        var left = new Vector2[n];
        var right = new Vector2[n];

        for (int i = 0; i < n; i++)
        {
            Vector2 tan = Tangent(s.pts, i);
            Vector2 perp = Perp(tan);

            // Keep the nib on a consistent side so the ribbon never twists on itself.
            Vector2 nd = nibDir;
            if (Vector2.Dot(nd, perp) < 0f) nd = -nd;

            Vector2 oMarker = perp * half;
            Vector2 oCallig = nd * half;
            Vector2 o = Vector2.Lerp(oMarker, oCallig, s.callig);

            // Guarantee a minimum visible thickness (perpendicular to travel).
            float along = Vector2.Dot(o, tan);
            Vector2 perpComp = o - along * tan;
            if (perpComp.magnitude < floorHalf) perpComp = perp * floorHalf;
            o = along * tan + perpComp;

            left[i] = s.pts[i] + o;
            right[i] = s.pts[i] - o;
        }

        // Two triangles per segment.
        for (int i = 0; i < n; i++)
        {
            verts.Add(left[i]);
            verts.Add(right[i]);
        }
        for (int i = 0; i < n - 1; i++)
        {
            int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(b); tris.Add(c); tris.Add(d);
        }

        // Rounded end caps for marker-like pens.
        if (s.caps)
        {
            AddCap(verts, tris, s.pts[0], right[0], -Tangent(s.pts, 0), half, s.capSeg);
            AddCap(verts, tris, s.pts[n - 1], left[n - 1], Tangent(s.pts, n - 1), half, s.capSeg);
        }

        Commit(s, verts, tris);
    }

    void Commit(Stroke s, List<Vector3> verts, List<int> tris)
    {
        s.mesh.Clear();
        s.mesh.SetVertices(verts);
        s.mesh.SetTriangles(tris, 0);
        s.mesh.RecalculateBounds();
    }

    // Semicircle fan bulging along 'outward', spanning from 'edge' around to its mirror.
    void AddCap(List<Vector3> verts, List<int> tris, Vector2 center, Vector2 edge, Vector2 outward, float r, int seg)
    {
        Vector2 u = (edge - center);
        if (u.sqrMagnitude < 1e-6f) u = Perp(outward) * r; else u = u.normalized * r;
        Vector2 v = outward.normalized * r;

        int start = verts.Count;
        verts.Add(center);
        for (int i = 0; i <= seg; i++)
        {
            float t = Mathf.PI * i / seg;               // 0..PI sweeps the outward half
            verts.Add(center + u * Mathf.Cos(t) + v * Mathf.Sin(t));
        }
        for (int i = 0; i < seg; i++)
        {
            tris.Add(start);
            tris.Add(start + 1 + i);
            tris.Add(start + 2 + i);
        }
    }

    void AddDisk(List<Vector3> verts, List<int> tris, Vector2 c, float r, int seg)
    {
        int start = verts.Count;
        verts.Add(c);
        for (int i = 0; i <= seg; i++)
        {
            float t = 2f * Mathf.PI * i / seg;
            verts.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r);
        }
        for (int i = 0; i < seg; i++)
        {
            tris.Add(start);
            tris.Add(start + 1 + i);
            tris.Add(start + 2 + i);
        }
    }

    // A rectangle centered at c, spanning +/-alongHalf and +/-sideHalf.
    void AddQuad(List<Vector3> verts, List<int> tris, Vector2 c, Vector2 alongHalf, Vector2 sideHalf)
    {
        int start = verts.Count;
        verts.Add(c + alongHalf + sideHalf);
        verts.Add(c + alongHalf - sideHalf);
        verts.Add(c - alongHalf + sideHalf);
        verts.Add(c - alongHalf - sideHalf);
        tris.Add(start); tris.Add(start + 2); tris.Add(start + 1);
        tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 3);
    }

    static Vector2 Perp(Vector2 v) => new Vector2(-v.y, v.x);

    Vector2 Tangent(List<Vector2> pts, int i)
    {
        int n = pts.Count;
        Vector2 prev = i > 0 ? (pts[i] - pts[i - 1]) : (pts[Mathf.Min(i + 1, n - 1)] - pts[i]);
        Vector2 next = i < n - 1 ? (pts[i + 1] - pts[i]) : (pts[i] - pts[Mathf.Max(i - 1, 0)]);
        Vector2 t = prev.normalized + next.normalized;
        if (t.sqrMagnitude < 1e-6f) t = next.sqrMagnitude > 1e-6f ? next : Vector2.right;
        return t.normalized;
    }

    // ---------------------------------------------------------------- input

    Vector3 MouseWorld()
    {
        Vector3 screen;
#if ENABLE_INPUT_SYSTEM
        screen = Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
#else
        screen = Input.mousePosition;
#endif
        screen.z = Mathf.Abs(cam.transform.position.z - penZ);
        Vector3 w = cam.ScreenToWorldPoint(screen);
        w.z = penZ;
        return w;
    }

    bool LeftHeld()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
        return Input.GetMouseButton(0);
#endif
    }
    bool LeftPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(0);
#endif
    }
    bool LeftReleasedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
        return Input.GetMouseButtonUp(0);
#endif
    }
    bool RightPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
#else
        return Input.GetMouseButtonDown(1);
#endif
    }
}