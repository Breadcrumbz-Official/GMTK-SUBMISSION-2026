using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

public class ExitTrigger : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    //[SerializeField] GameObject door;
    //ExitController ec = FindFirstObjectByType<ExitController>();

    // Start is called before the first frame update
    void Awake()
    {
        //ExitController ec = FindFirstObjectByType<ExitController>();
    }

    void Start()
    {
        //ExitController ec = FindFirstObjectByType<ExitController>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log(other.tag);

        ExitController ec = FindFirstObjectByType<ExitController>();

        if (ec.unlocked)
        {
            lt.winMission();
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log(collision.tag);

        ExitController ec = FindFirstObjectByType<ExitController>();

        if (ec.unlocked)
        {
            lt.winMission();
        }
    }
}
