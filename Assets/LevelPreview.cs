using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class LevelPreview : MonoBehaviour
{
    [SerializeField] private LevelTracker tracker;   // drag your tracker asset here
    [SerializeField] private Sprite[] levelSprites;  // slot 0 = level 1, slot 1 = level 2, etc.

    private Image img;

    void Awake() => img = GetComponent<Image>();

    void OnEnable() => Apply();

    public void Apply()
    {
        if (img == null) img = GetComponent<Image>();

        if (tracker == null)
        {
            Debug.LogError("LevelPreview: tracker not assigned in the Inspector", this);
            return;
        }

        int i = tracker.levelCurrent - 1;
        bool valid = i >= 0 && i < levelSprites.Length && levelSprites[i] != null;

        if (!valid)
            Debug.LogWarning($"LevelPreview: no sprite for levelCurrent = {tracker.levelCurrent}", this);

        img.sprite = valid ? levelSprites[i] : null;
        img.preserveAspect = true;
        img.enabled = valid;
    }
}