using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class countdownTimer : MonoBehaviour
{

    private float timeCurrent;
    private float timeTotal;

    private TextMeshProUGUI txt;

    public void setMaxTime(float secs)
    {
        timeTotal = secs;
    }

    private void resetTime()
    {
        timeCurrent = timeTotal;
    }

    private void timeGoDown()
    {
        timeCurrent -= 0.1f;
    }

    void Awake()
    {
        txt = GetComponent<TextMeshProUGUI>();
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        txt.SetText(timeCurrent.ToString());
    }
    
}
