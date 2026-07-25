using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExitController : MonoBehaviour
{


    [SerializeField] LevelTracker lt;
    //BoxCollider2D bc;
    
    //variables for total objectives needed and how many are currently collected
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
    }

    public void collectObj()
    {
        objCurrent += 1;
        if(objCurrent >= objReq)
        {
            rb.simulated = false;

            Debug.Log("unlocked");

            unlocked = true;

            //bc.isTrigger = true;

        }
        else
        {
            rb.simulated = true;
            Debug.Log("Fuck you");
        }
    }

    // Start is called before the first frame update
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        //bc = GetComponent<BoxCollider2D>();
    }

    void Start()
    {
        spanwExit(0);
    }

    // Update is called once per frame
    void Update()
    {

    }

    /*void OnTriggerEnter2D(Collider2D collision)
    {
        Debug.Log(collision.tag);

        if (collision.CompareTag("Player"))
        {
            lt.winMission();
        }
    }*/
}
