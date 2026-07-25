using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "EnemyTracker", menuName = "enemy")]
public class EnemyTracker : ScriptableObject
{

    public List<GameObject> enemyList = new List<GameObject>();

    // Start is called before the first frame update
    void Start()
    {
        
    }

    public void AddToList(GameObject obj)
    {
        enemyList.Add(obj);
    }

    /*public void RemoveFromList(GameObject obj)
    {
        enemyList.Add(obj);
    }*/

    

    // Update is called once per frame
    void Update()
    {
        Debug.Log(enemyList);
    }
}
