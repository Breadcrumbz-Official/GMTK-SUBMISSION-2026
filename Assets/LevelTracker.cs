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
    public int levelCurrent = 0;

    public void nextMission()
    {
        levelCurrent += 1;

        showPreview(levelCurrent - 1);
    }

    public void showPreview(int lev)
    {
        SceneManager.LoadScene("LevelPreviewScreen");

        LevelDescriptions desc = Object.FindFirstObjectByType<LevelDescriptions>();
        LevelTitles title = Object.FindFirstObjectByType<LevelTitles>();

        desc.updateDesc(desc.descs[lev]);
        title.updateTit(title.tits[lev]);
    }

    public void previewToLevel(int lev)
    {
        SceneManager.LoadScene(levelNames[lev -1]);
    }

    public void GameOver()
    {
        levelCurrent = 0;
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
