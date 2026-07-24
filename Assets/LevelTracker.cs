using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
[CreateAssetMenu(fileName = "NewTracker", menuName = "testmenu")]
public class LevelTracker : ScriptableObject
{


    private string[] levelSceneNames =
    {
      "level 1"
    };
    public int levelCurrent = 0;

    public void winMission()
    {
        showPreview();
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
