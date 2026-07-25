using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectivePickup : MonoBehaviour
{
    public ExitController door;

    [Header("Pickup sound")]
    public AudioClip pickupClip;
    [Range(0f, 1f)] public float pickupVolume = 0.8f;

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("Player"))
        {
            door.collectObj();

            // Play at the pickup's position so it survives the object being disabled.
            if (pickupClip)
                AudioSource.PlayClipAtPoint(pickupClip, transform.position, pickupVolume);

            gameObject.SetActive(false);
        }
    }

    void Start()
    {
        door = Object.FindFirstObjectByType<ExitController>();
    }

    void Update()
    {

    }
}