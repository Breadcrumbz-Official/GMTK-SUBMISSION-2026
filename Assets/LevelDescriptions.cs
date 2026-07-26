using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LevelDescriptions : MonoBehaviour
{
    TextMeshProUGUI txt;
    
    [SerializeField] LevelTracker lt;


    //public int level;

    /*public string l1desc = "Welcome back Mr. Heist. Your first target is a small time art gallery, just to get you started. This one's been robbed about 12 times in the last year.";
    public string l2desc = "";
    public string l3desc = "";
    public string l4desc = "";
    */

    public string[] descs = {
    "Welcome back Mr. McHeist. It's been a while since you've last McHeisted, so here is a quick tutorial to get you started. Feel free to check out the Help page if you keep getting stuck on a level.",
    "Great, now it's time to step things up a bit. Your first target is a small, failing museum. They have already been robbed five times in the past month. Should be easy pickings for a master like you.",
    "Excellent job. Lets crank things up to the next level with this next heist - this time it is a fortified office, home to three of the world's most valuable secrets. Steal them and get out without getting caught.",
    "We have got to say, Mr. McHeist - you have certainly upped your ante, and so have we. Good luck robbing this superbank - you'll most certainly need it...",
    };

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
        //Debug.Log("the desc:   " + descs[lt.levelCurrent-1]);

        txt.SetText(descs[lt.levelCurrent-1]);
        //Debug.Log("description index:  " + (lt.levelCurrent));
        //Debug.Log(descs[lt.levelCurrent]);
    }
}
