using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

public class LevelTitles : MonoBehaviour
{
    TextMeshProUGUI txt;
    [SerializeField] LevelTracker lt;


    //public int level;
    /*
    public string l1tit = "Small Time";
    public string l2tit = "";
    public string l3tit = "";
    public string l4tit = "";
    */
    public string[] tits = {
    "Small Time",
    "",
    ""
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
        Debug.Log(lt.levelCurrent);
        txt.SetText(tits[lt.levelCurrent-1]);
    }
}
