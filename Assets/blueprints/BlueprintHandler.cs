using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

public class BlueprintHandler : MonoBehaviour
{


    [SerializeField] private Sprite[] blueprints;
    [SerializeField] LevelTracker lt;
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        sr.sprite = blueprints[lt.levelCurrent-1];
    }
}
