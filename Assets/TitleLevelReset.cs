using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TitleLevelReset : MonoBehaviour
{

    [SerializeField] LevelTracker lt;

    // Start is called before the first frame update
    void Start()
    {
        lt.levelCurrent = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
