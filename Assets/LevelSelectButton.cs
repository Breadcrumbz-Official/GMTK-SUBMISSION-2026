using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LevelSelectButton : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    [SerializeField] Button lsb;

    // Start is called before the first frame update
    void Awake()
    {
        lsb.onClick.AddListener(GoLevelSelect);
    }

    void GoLevelSelect()
    {
        SceneManager.LoadScene("LevelSelect");
    }

    void Start()
    {
        lt.levelCurrent = 0;
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
