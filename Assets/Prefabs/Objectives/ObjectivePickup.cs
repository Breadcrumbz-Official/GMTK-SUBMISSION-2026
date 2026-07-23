using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectivePickup : MonoBehaviour
{

    public ExitController door;

    private void OnTriggerEnter2D(Collider2D collision)
    {

        Debug.Log(collision.tag);
        if(collision.CompareTag("Player"))
        {

            door.collectObj();

            Debug.Log("collect");

            gameObject.SetActive(false);
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        door = Object.FindFirstObjectByType<ExitController>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
