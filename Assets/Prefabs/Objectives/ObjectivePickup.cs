using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectivePickup : MonoBehaviour
{

    public ExitController exitScript;

    private void OnTriggerEnter2D(Collider2D collision)
    {

        Debug.Log(collision.tag);
        if(collision.CompareTag("Player"))
        {
            ExitController door = Object.FindFirstObjectByType<ExitController>();

            door.collectObj();

            Debug.Log("collect");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
