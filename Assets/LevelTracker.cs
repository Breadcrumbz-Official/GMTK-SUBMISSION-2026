using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
[CreateAssetMenu(fileName = "NewTracker", menuName = "testmenu")]
public class LevelTracker : ScriptableObject
{

    [SerializeField] EnemyTracker et;

    private string[] levelSceneNames =
    {
      "Tutorial",
      "Level 1",
      "Level 2",
    };
    public int levelCurrent = 0;

    public string bustedReason;

    //public bool freeze = false;

    public void winMission()
    {
        SceneManager.LoadScene("Win Screen");

        Debug.Log("win");
    }

    /* private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;        
        //yield return new WaitUntil(() => SceneManager.LoadSceneAsync == true);
    }*/

    public void showPreview()
    {
        SceneManager.LoadScene("LevelPreviewScreen");

        //SceneManager.sceneLoaded += OnSceneLoaded;


    }



    public void previewToLevel()
    {


        SceneManager.LoadScene(levelSceneNames[levelCurrent-1]);
    }

    public void GameOver(String source)
    {
        //freeze = true;

        GameObject p = GameObject.Find("Player");
        p.GetComponent<PlayerController>().frozen = true;

        countdownTimer cd = FindFirstObjectByType<countdownTimer>();
        cd.freezeTime = true;

        switch (source)
        {
            case "Camera":
            bustedReason = "You got seen by a camera!";
            break;

            case "Guard":
            bustedReason = "You got caught by a guard!";
            break;

            case "Time":
            bustedReason = "You ran out of time!";
            break;
        }

        //SceneManager.LoadScene("Fail Screen");
    }

    public void Reset()
    {
        SceneManager.LoadScene("Fail Screen");
    }


    void Awake()
    {

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
