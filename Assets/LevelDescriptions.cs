using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LevelDescriptions : MonoBehaviour
{
    TextMeshProUGUI txt;

    public int level;

    string l1desc = "Welcome back Mr. Heist. Your first target is a small time art gallery, just to get you started.";

    // Start is called before the first frame update
    void Start()
    {
        txt = GetComponent<TextMeshProUGUI>();
    }

    // Update is called once per frame
    void Update()
    {
        if(level == 1)
        {
            txt.SetText(l1desc);
        }
    }
}
