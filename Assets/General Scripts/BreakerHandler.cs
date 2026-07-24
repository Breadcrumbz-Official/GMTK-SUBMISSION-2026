using UnityEngine;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Breaker : MonoBehaviour
{
    [Header("Player")]
    [Tooltip("Leave empty and it finds the object tagged 'Player' automatically.")]
    public Transform player;
    [Tooltip("How close the player must be for the prompt to appear, in world units.")]
    public float range = 2f;

    [Header("Prompt")]
    [Tooltip("The child object holding the floating text. Gets shown/hidden automatically.")]
    public GameObject prompt;
    [Tooltip("Optional. If assigned, the text switches between Cam on and Cam off.")]
    public TMP_Text promptLabel;

    [Header("Wired cameras")]
    [Tooltip("Drag in every camera this breaker controls.")]
    public SecurityCamera2D[] cameras;

    [Header("Breaker parts")]
    [Tooltip("The breaker's SpriteRenderer.")]
    public SpriteRenderer art;
    [Tooltip("Optional. If both are set the sprite swaps; if not, it just dims when off.")]
    public Sprite onSprite;
    public Sprite offSprite;

    [Header("Audio (optional)")]
    public AudioSource audioSource;
    public AudioClip onClip;
    public AudioClip offClip;

    // Current state. Untick in the Inspector to start with the cameras dead.
    public bool isOn = true;

    void Awake()
    {
        // Find the player once at startup so you don't have to drag it onto every breaker.
        if (!player)
        {
            GameObject p = GameObject.FindWithTag("Player");
            if (p) player = p.transform;
            else Debug.LogWarning(name + ": no player found. Tag your player 'Player'.", this);
        }

        // Hide the prompt until the player actually walks up.
        if (prompt) prompt.SetActive(false);

        // Push the starting state onto the cameras and the sprite.
        ApplyChange();
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
            UpdatePrompt();
        }

        // Only respond to the key while the player is close enough.
        if (inRange && EPressed())
            Flip();
    }

    // Flip the breaker between powered and cut.
    public void Flip()
    {
        isOn = !isOn;
        ApplyChange();
        UpdatePrompt();

        if (audioSource)
        {
            AudioClip clip = isOn ? onClip : offClip;
            if (clip) audioSource.PlayOneShot(clip);
        }
    }

    // Push the current state onto every wired camera and onto the sprite.
    void ApplyChange()
    {
        // This is the line that actually does the job.
        if (cameras != null)
        {
            foreach (SecurityCamera2D cam in cameras)
            {
                if (cam == null) continue;   // empty slot, or the object was destroyed
                cam.isOn = isOn;
            }
        }

        if (art)
        {
            if (onSprite && offSprite)
                art.sprite = isOn ? onSprite : offSprite;   // swap art
            else
                art.color = isOn ? Color.white : Color.grey; // no art? just dim it
        }
    }

    // Keep the prompt wording honest about what pressing E will do.
    void UpdatePrompt()
    {
        if (promptLabel) promptLabel.text = isOn ? "[E] Cam off" : "[E] Cam on";
    }

    // Returns true on the frame E is pressed. Works under either input backend.
    bool EPressed()
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

    // Green circle = interact range. Green lines = which cameras are wired to it.
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, range);

        if (cameras == null) return;
        foreach (SecurityCamera2D cam in cameras)
            if (cam) Gizmos.DrawLine(transform.position, cam.transform.position);
    }
}