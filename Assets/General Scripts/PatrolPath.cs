using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A patrol route made of waypoints in world space. Points are stored as world
/// positions so the path stays put even if you move this GameObject. Draw and edit
/// it in the Scene view — each point shows as a draggable handle (see PatrolPathEditor).
/// </summary>
[DisallowMultipleComponent]
public class PatrolPath : MonoBehaviour
{
    [Tooltip("Waypoints in world space, walked in order.")]
    public List<Vector2> points = new List<Vector2>();

    [Tooltip("Loop back to the first point after the last. Off = walk to the end, then turn around and come back (ping-pong).")]
    public bool loop = true;

    [Header("Dev view")]
    public Color pathColor = new Color(1f, 0.85f, 0.2f, 0.9f);
    public float handleSize = 0.18f;

    public int Count => points.Count;
    public bool HasPath => points.Count >= 2;

    public Vector2 Get(int i) => points[Mathf.Clamp(i, 0, points.Count - 1)];

    /// <summary>
    /// Given the current target index and walk direction, returns the next index and
    /// possibly-flipped direction. Handles both loop and ping-pong modes in one place
    /// so the guard doesn't have to know which is which.
    /// </summary>
    public int Advance(int index, ref int dir)
    {
        if (points.Count < 2) return 0;

        if (loop)
            return (index + 1) % points.Count;   // always forward, wrapping

        // Ping-pong: step in the current direction, flip at either end.
        int next = index + dir;
        if (next >= points.Count) { dir = -1; next = points.Count - 2; }
        else if (next < 0)        { dir = 1;  next = 1; }
        return next;
    }

    /// <summary>Where Advance would go next, without changing state. For pre-aiming.</summary>
    public int Peek(int index, int dir)
    {
        if (points.Count < 2) return 0;
        if (loop) return (index + 1) % points.Count;

        int next = index + dir;
        if (next >= points.Count) next = points.Count - 2;
        else if (next < 0)        next = 1;
        return next;
    }

    // Draw the route so you can see it while editing.
    void OnDrawGizmos()
    {
        if (points == null || points.Count == 0) return;

        Gizmos.color = pathColor;

        for (int i = 0; i < points.Count - 1; i++)
            Gizmos.DrawLine(points[i], points[i + 1]);

        if (loop && points.Count > 1)
            Gizmos.DrawLine(points[points.Count - 1], points[0]);

        for (int i = 0; i < points.Count; i++)
            Gizmos.DrawSphere(points[i], handleSize * (i == 0 ? 1.6f : 1f));
    }
}