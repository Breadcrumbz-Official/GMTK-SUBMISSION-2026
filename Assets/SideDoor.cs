using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class SideDoor : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Leave empty and it finds the object tagged 'Player' automatically.")]
    public Transform player;
    [Tooltip("How close the player must be for the prompt to appear, in world units.")]
    public float range = 2f;

    [Header("Prompt")]
    [Tooltip("The child object holding the floating text. Gets shown/hidden automatically.")]
    public GameObject prompt;
    [Tooltip("Optional. If assigned, the text switches between Open and Close.")]
    public TMP_Text promptLabel;

    [Header("Door visuals")]
    [Tooltip("Child object shown when the door is CLOSED. Position/rotate/scale it however you like.")]
    public GameObject closedVisual;
    [Tooltip("Child object shown when the door is OPEN. E.g. slid aside, swung open, or dropped.")]
    public GameObject openVisual;

    [Header("Colliders")]
    [Tooltip("Blocks movement AND sight while CLOSED. Turned off when the door opens.")]
    public Collider2D blocker;
    [Tooltip("Active only while OPEN. Put this on the Obstacles layer so the OPEN door still casts fog/occlusion. Should NOT block movement — make it a trigger, or on a layer that doesn't collide with the player.")]
    public Collider2D openObstacle;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip openClip;
    public AudioClip closeClip;

    // Current state. Set this in the Inspector if you want a door to start open.
    public bool isOpen = false;

    void Awake()
    {
        // Find the player once at startup so you don't have to drag it onto every door.
        if (!player)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
            else Debug.LogWarning(name + ": no player found. Tag your player 'Player'.", this);
        }

        // Hide the prompt until the player actually walks up.
        if (prompt) prompt.SetActive(false);

        // Make sure the visuals and colliders match the starting state.
        ApplySideDoorState();
    }

    void Update()
    {
        if (!player) return;

        // Simple distance check — no extra trigger collider needed.
        bool inRange = Vector2.Distance(player.position, transform.position) <= range;

        // Show or hide the floating prompt.
        if (prompt && prompt.activeSelf != inRange)
        {
            prompt.SetActive(inRange);
            UpdateSideDoorLabel();
        }

        // Only respond to the key while the player is close enough.
        if (inRange && InteractPressed())
            Toggle();
    }

    // Flip the door between open and closed.
    public void Toggle()
    {
        isOpen = !isOpen;
        ApplySideDoorState();
        UpdateSideDoorLabel();

        if (audioSource)
        {
            AudioClip clip = isOpen ? openClip : closeClip;
            if (clip) audioSource.PlayOneShot(clip);
        }
    }

    // Push the current state onto the colliders and the two visuals.
    void ApplySideDoorState()
    {
        // Closed: the blocker stops movement and sight.
        // Open:   the blocker is off (so you can walk through), but the open-state
        //         collider switches on so the OPEN door is still solid to the fog of
        //         war / sight raycasts. Keep openObstacle on the Obstacles layer and
        //         non-blocking (a trigger, or a layer the player doesn't collide with)
        //         so it occludes vision without stopping the player.
        if (blocker)      blocker.enabled = !isOpen;
        if (openObstacle) openObstacle.enabled = isOpen;

        // Show exactly one of the two visuals for the current state.
        if (closedVisual) closedVisual.SetActive(!isOpen);
        if (openVisual)   openVisual.SetActive(isOpen);
    }

    // Keep the prompt wording honest about what pressing E will do.
    void UpdateSideDoorLabel()
    {
        if (promptLabel) promptLabel.text = isOpen ? "[E] Close" : "[E] Open";
    }

    // Returns true on the frame E is pressed. Works under either input backend.
    bool InteractPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k != null && k.eKey.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.E)) return true;
#endif
        return false;
    }

    // Green circle in the Scene view showing the trigger range.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, range);
    }
}