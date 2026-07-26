using UnityEngine;

/// <summary>
/// Loops a track forever and survives scene loads. Put this on one GameObject in
/// your first scene, assign a clip, done. If another copy exists in a later scene,
/// it destroys itself so the music never restarts or doubles up.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MusicPlayer : MonoBehaviour
{
    [Tooltip("The track to loop.")]
    public AudioClip music;
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("Keep playing across scene changes.")]
    public bool persistAcrossScenes = true;

    static MusicPlayer instance;
    AudioSource src;

    void Awake()
    {
        // Only one music player allowed. A duplicate in a new scene kills itself.
        if (persistAcrossScenes)
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        src = GetComponent<AudioSource>();
        src.clip = music;
        src.loop = true;          // this is the line that makes it repeat forever
        src.volume = volume;
        src.playOnAwake = false;
        src.Play();
    }

    // Handy hooks you can call from other scripts or UI buttons.
    public void SetVolume(float v) { volume = v; if (src) src.volume = v; }

    public void Play()  { if (src && !src.isPlaying) src.Play(); }
    public void Stop()  { if (src) src.Stop(); }
    public void Pause() { if (src) src.Pause(); }

    // Swap to a different looping track at runtime.
    public void ChangeTrack(AudioClip clip)
    {
        if (!src || clip == null) return;
        src.clip = clip;
        src.loop = true;
        src.Play();
    }
}