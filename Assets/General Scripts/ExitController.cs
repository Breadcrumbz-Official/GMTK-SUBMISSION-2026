using UnityEngine;

public class ExitController : MonoBehaviour
{
    [SerializeField] LevelTracker lt;

    // Variables for total objectives needed and how many are currently collected.
    public int objReq;
    public int objCurrent;

    public bool unlocked = false;

    Rigidbody2D rb;

    public void spanwExit(int totalObj)
    {
        objCurrent = 0;
        objReq = totalObj;
        rb.simulated = true;
        unlocked = false;
        ApplyState();
    }

    public void collectObj()
    {
        objCurrent += 1;
        if (objCurrent >= objReq)
        {
            rb.simulated = false;
            unlocked = true;
            Debug.Log("unlocked");
        }
        else
        {
            rb.simulated = true;
        }
        ApplyState();
    }

    // Show/hide siblings based on lock state.
    // Locked: "Open" hidden, everything else shown.
    // Unlocked: only "Open" and "Detector" shown, everything else (incl. self) hidden.
    void ApplyState()
    {
        foreach (Transform sibling in transform.parent)
        {
            string n = sibling.name;

            if (!unlocked)
            {
                // Start / locked state: keep everything except Open.
                sibling.gameObject.SetActive(n != "Open");
            }
            else
            {
                // Unlocked: keep only Open and Detector.
                sibling.gameObject.SetActive(n == "Open" || n == "detector");
            }
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        spanwExit(0);
    }
}