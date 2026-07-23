using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class LevelDescriptions : MonoBehaviour
{
    TextMeshPro txt;

    public int level;

    // Start is called before the first frame update
    void Start()
    {
        txt = GetComponent<TextMeshPro>();
    }

    // Update is called once per frame
    void Update()
    {
        if(level == 1)
        {
            txt.text = "goobity";
        }
    }
}
