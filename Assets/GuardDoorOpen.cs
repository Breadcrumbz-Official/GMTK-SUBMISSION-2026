using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GuardDoorOpen : MonoBehaviour
{

    public Door dh;
    public SideDoor sdh;

    // Start is called before the first frame update
    void Start()
    {
        dh = gameObject.GetComponent<Door>();        
        sdh = gameObject.GetComponent<SideDoor>();
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OpenDoor()
    {
        if(dh == null && sdh != null)
        {
            sdh.Toggle();
        }
        if(dh != null && sdh == null)
        {
            dh.Toggle();
        }
    }

}
