using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuButton : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    [SerializeField] Button mmb;

    void Awake()
    {
        mmb.onClick.AddListener(GoMainMenu);
    }

    void GoMainMenu()
    {
        SceneManager.LoadScene("MainMenu");
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
