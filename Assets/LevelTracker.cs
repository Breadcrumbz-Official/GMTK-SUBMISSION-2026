using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
[CreateAssetMenu(fileName = "NewTracker", menuName = "testmenu")]
public class LevelTracker : ScriptableObject
{


    private string[] levelNames =
    {
      "level 1"
    };
    public int levelCurrent = 1;

    public void winMission()
    {
        levelCurrent += 1;

        showPreview();
    }

    /* private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;        
        //yield return new WaitUntil(() => SceneManager.LoadSceneAsync == true);
    }*/

    public void showPreview()
    {
        SceneManager.LoadSceneAsync("LevelPreviewScreen");

        //SceneManager.sceneLoaded += OnSceneLoaded;


    }



    public void previewToLevel()
    {
        SceneManager.LoadScene(levelNames[levelCurrent-1]);
    }

    public void GameOver()
    {
        showPreview();
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
