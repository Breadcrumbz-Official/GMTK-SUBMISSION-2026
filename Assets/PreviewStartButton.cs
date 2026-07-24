using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PreviewStartButton : MonoBehaviour
{
    [SerializeField] LevelTracker lt;
    [SerializeField] Button sb;

    void Awake()
    {
        sb.onClick.AddListener(whenPressed);
    }

    void whenPressed()
    {
        lt.previewToLevel();
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
