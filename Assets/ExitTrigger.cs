using UnityEngine;

public class ExitTrigger : MonoBehaviour
{
    [SerializeField] LevelTracker lt;
    [SerializeField] ExitController ec;   // drag the exit object in

    void Awake()
    {
        if (ec == null) ec = FindFirstObjectByType<ExitController>();
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (!collision.CompareTag("Player")) return;

        if (ec == null)
        {
            Debug.LogError("ExitTrigger: no ExitController found", this);
            return;
        }

        if (ec.unlocked)
            lt.winMission();
    }
}