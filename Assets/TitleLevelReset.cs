using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TitleLevelReset : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    [SerializeField] EnemyTracker et;

    // Start is called before the first frame update
    void Start()
    {
        lt.levelCurrent = 0;
        et.enemyList.Clear();

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
