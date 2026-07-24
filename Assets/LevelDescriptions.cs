using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LevelDescriptions : MonoBehaviour
{
    TextMeshProUGUI txt;


    //public int level;

    /*public string l1desc = "Welcome back Mr. Heist. Your first target is a small time art gallery, just to get you started. This one's been robbed about 12 times in the last year.";
    public string l2desc = "";
    public string l3desc = "";
    public string l4desc = "";
    */
    public string[] descs = {
    "Welcome back Mr. Heist. Your first target is a small time art gallery, just to get you started. This one's been robbed about 12 times in the last year.",
    "",
    "",
    };

    public void updateDesc(string desc)
    {
        txt.SetText(desc);
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
        
    }
}
