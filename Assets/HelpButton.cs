using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HelpButton : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    [SerializeField] Button hb;

    // Start is called before the first frame update
    void Awake()
    {
        hb.onClick.AddListener(GoHelp);
    }

    void GoHelp()
    {
        SceneManager.LoadScene("HelpScreen");
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
