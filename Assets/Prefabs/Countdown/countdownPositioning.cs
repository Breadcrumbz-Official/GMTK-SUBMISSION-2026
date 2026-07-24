using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class countdownPositioning : MonoBehaviour
{

    private Transform tf;

    public Camera cam;

    public float offset = 5f;


    void Awake()
    {
        tf = GetComponent<Transform>();
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        tf.position = new Vector3(cam.transform.position.x, cam.transform.position.y + offset, .1f);
    }
}
