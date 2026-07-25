using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BustedReason : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    TextMeshProUGUI txt;


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
        txt.SetText(lt.bustedReason);
    }
}
