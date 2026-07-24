using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExitController : MonoBehaviour
{


    [SerializeField] LevelTracker lt;
    BoxCollider2D bc;
    
    //variables for total objectives needed and how many are currently collected
    public int objReq;
    public int objCurrent;

    Rigidbody2D rb;

    public void spanwExit(int totalObj)
    {
        objCurrent = 0;
        objReq = totalObj;
        rb.simulated = true;
    }

    public void collectObj()
    {
        objCurrent += 1;
        if(objCurrent >= objReq)
        {
            rb.simulated = false;

            Debug.Log("unlocked");

            bc.isTrigger = true;

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
        bc = GetComponent<BoxCollider2D>();
    }

    void Start()
    {
        spanwExit(3);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
