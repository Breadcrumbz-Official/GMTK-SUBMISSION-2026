using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RetryButton : MonoBehaviour
{
    [SerializeField] LevelTracker lt;
    [SerializeField] Button rb;


    void Awake()
    {
        rb.onClick.AddListener(Retry);
    }

    void Retry()
    {
        lt.showPreview();
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
