using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ExitController : MonoBehaviour
{


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
        }
        else
        {
            rb.simulated = true;
            Debug.Log("Fuck you");
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        spanwExit(3);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
