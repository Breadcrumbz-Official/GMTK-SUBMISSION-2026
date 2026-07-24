using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelButton : MonoBehaviour
{

    [SerializeField] LevelTracker lt;
    [SerializeField] Button lb;

    [SerializeField] int leadingLevel;

    // Start is called before the first frame update
    void Awake()
    {
        lb.onClick.AddListener(GoPreview);

        //Debug.Log("hi");
    }

    void GoPreview()
    {
        lt.levelCurrent = leadingLevel;

        Debug.Log(lt.levelCurrent);

        lt.showPreview();

        //Debug.Log(lt.levelCurrent);
        //Debug.Log("ll:   " + leadingLevel);
    }

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
