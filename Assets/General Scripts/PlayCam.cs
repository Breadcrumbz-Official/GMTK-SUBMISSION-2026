using UnityEngine;

public class CameraFollow2D : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public Vector2 offset = Vector2.zero;

    [Header("Feel")]
    [Tooltip("Seconds to catch up. 0 = locked rigidly to the target.")]
    public float smoothTime = 0.15f;
    [Tooltip("Target can drift this far from centre before the camera reacts.")]
    public Vector2 deadZone = Vector2.zero;

    [Header("Optional level bounds")]
    public bool useBounds = false;
    public Vector2 boundsMin = new Vector2(-20f, -20f);
    public Vector2 boundsMax = new Vector2(20f, 20f);

    Camera cam;
    Vector3 vel;

    void Awake()
    {
        cam = GetComponent<Camera>();
        if (target) transform.position = Clamp(Desired(target.position));
    }

    void LateUpdate()
    {
        if (!target) return;

        Vector3 want = Desired(target.position);

        if (deadZone.x > 0f || deadZone.y > 0f)
        {
            Vector3 cur = transform.position;
            float dx = want.x - cur.x, dy = want.y - cur.y;
            want.x = Mathf.Abs(dx) > deadZone.x ? cur.x + Mathf.Sign(dx) * (Mathf.Abs(dx) - deadZone.x) : cur.x;
            want.y = Mathf.Abs(dy) > deadZone.y ? cur.y + Mathf.Sign(dy) * (Mathf.Abs(dy) - deadZone.y) : cur.y;
        }

        want = Clamp(want);

        transform.position = smoothTime <= 0f
            ? want
            : Vector3.SmoothDamp(transform.position, want, ref vel, smoothTime);
    }

    Vector3 Desired(Vector3 t) => new Vector3(t.x + offset.x, t.y + offset.y, transform.position.z);

    Vector3 Clamp(Vector3 p)
    {
        if (!useBounds || !cam || !cam.orthographic) return p;
        float h = cam.orthographicSize;
        float w = h * cam.aspect;
        float minX = boundsMin.x + w, maxX = boundsMax.x - w;
        float minY = boundsMin.y + h, maxY = boundsMax.y - h;
        p.x = minX > maxX ? (boundsMin.x + boundsMax.x) * 0.5f : Mathf.Clamp(p.x, minX, maxX);
        p.y = minY > maxY ? (boundsMin.y + boundsMax.y) * 0.5f : Mathf.Clamp(p.y, minY, maxY);
        return p;
    }

    void OnDrawGizmosSelected()
    {
        if (!useBounds) return;
        Gizmos.color = Color.yellow;
        Vector3 c = (boundsMin + boundsMax) * 0.5f;
        Gizmos.DrawWireCube(c, new Vector3(boundsMax.x - boundsMin.x, boundsMax.y - boundsMin.y, 0.1f));
    }
}