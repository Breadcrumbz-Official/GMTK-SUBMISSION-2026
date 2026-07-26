using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UIElements;

public class countdownTimer : MonoBehaviour
{

    [SerializeField] private LevelTracker lt;

    public bool freezeTime;
    private float timeCurrent;
    private float timeTotal;

    private float spaceTime;

    public bool start;

    private TextMeshProUGUI txt;

    public void setMaxTime(float secs)
    {
        timeTotal = secs * 10;
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
        switch (lt.levelCurrent)
        {
            case 1:
            setMaxTime(60);
            break;

            case 2:
            setMaxTime(60);
            break;

            case 3:
            setMaxTime(60);
            break;
            
            case 4:
            setMaxTime(60);
            break;
        }

        setMaxTime(60);
        resetTime();
        start = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (!freezeTime)
        {
            if (start)
            {
                spaceTime += Time.deltaTime;

                if(spaceTime >= 0.1f)
                {
                    timeCurrent -= 1;
                    spaceTime = 0;
                }
                if(timeCurrent % 10 == 0)
                {
                    txt.SetText((timeCurrent/10).ToString() + ".0");
                }
                else
                {
                    txt.SetText((timeCurrent/10).ToString());  
                }

                if (timeCurrent <= 0)
                {
                    lt.GameOver("Time");
                }
            }
            else
            {
                txt.SetText((timeCurrent/10).ToString() + ".0");
            }
        }
        else
        {
            txt.SetText("Oops!");
        }
    }
    
}
