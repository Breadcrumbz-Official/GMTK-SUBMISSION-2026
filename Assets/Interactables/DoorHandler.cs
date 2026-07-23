using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Door : MonoBehaviour
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

    [Header("Door parts")]
    [Tooltip("The collider that blocks movement. Turned off while the door is open.")]
    public Collider2D blocker;
    [Tooltip("The door's SpriteRenderer.")]
    public SpriteRenderer art;
    [Tooltip("Optional. If both are set the sprite swaps; if not, the door just hides when open.")]
    public Sprite closedSprite;
    public Sprite openSprite;

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

        // Make sure the visuals and collider match the starting state.
        ApplyState();
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
            UpdateLabel();
        }

        // Only respond to the key while the player is close enough.
        if (inRange && InteractPressed())
            Toggle();
    }

    // Flip the door between open and closed.
    public void Toggle()
    {
        isOpen = !isOpen;
        ApplyState();
        UpdateLabel();

        if (audioSource)
        {
            AudioClip clip = isOpen ? openClip : closeClip;
            if (clip) audioSource.PlayOneShot(clip);
        }
    }

    // Push the current state onto the collider and the sprite.
    void ApplyState()
    {
        // Turning the collider off is what actually lets the player walk through.
        if (blocker) blocker.enabled = !isOpen;

        if (art)
        {
            if (closedSprite && openSprite)
                art.sprite = isOpen ? openSprite : closedSprite;   // swap art
            else
                art.enabled = !isOpen;                             // no art? just vanish
        }
    }

    // Keep the prompt wording honest about what pressing E will do.
    void UpdateLabel()
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