#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Scene-view editor for PatrolPath. Gives you draggable handles for each point, a
/// button to append points by clicking in the Scene, and a clear button. This is why
/// the path can be "drawn" — Unity handles the dragging, we just add/remove points.
/// </summary>
[CustomEditor(typeof(PatrolPath))]
public class PatrolPathEditor : Editor
{
    bool addMode;   // when true, Scene clicks append a new waypoint

    void OnSceneGUI()
    {
        PatrolPath path = (PatrolPath)target;
        if (path.points == null) return;

        // --- draggable handle for every existing point
        for (int i = 0; i < path.points.Count; i++)
        {
            Vector3 world = path.points[i];

            EditorGUI.BeginChangeCheck();
            // FreeMoveHandle lets you drag the point around on the XY plane.
            Vector3 moved = Handles.FreeMoveHandle(
                world, path.handleSize * 3f, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(path, "Move Patrol Point");
                path.points[i] = new Vector2(moved.x, moved.y);   // keep it on the 2D plane
                EditorUtility.SetDirty(path);
            }

            // Label each point with its index so the order is obvious.
            Handles.Label(world + Vector3.up * 0.25f, i.ToString());
        }

        // --- click-to-add mode
        if (addMode)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                // Turn the mouse position into a world point on the Z=0 plane.
                Ray r = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Vector3 p = r.origin;
                p.z = 0f;

                Undo.RecordObject(path, "Add Patrol Point");
                path.points.Add(new Vector2(p.x, p.y));
                EditorUtility.SetDirty(path);
                e.Use();   // swallow the click so it doesn't deselect
            }
        }
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        PatrolPath path = (PatrolPath)target;

        EditorGUILayout.Space();

        // Toggle the click-to-add mode. Coloured so it's obvious when it's on.
        GUI.backgroundColor = addMode ? Color.green : Color.white;
        if (GUILayout.Button(addMode ? "Click in Scene to add points (ON)" : "Add points by clicking"))
            addMode = !addMode;
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("Remove last point") && path.points.Count > 0)
        {
            Undo.RecordObject(path, "Remove Patrol Point");
            path.points.RemoveAt(path.points.Count - 1);
            EditorUtility.SetDirty(path);
        }

        if (GUILayout.Button("Clear all points") && path.points.Count > 0)
        {
            Undo.RecordObject(path, "Clear Patrol Points");
            path.points.Clear();
            EditorUtility.SetDirty(path);
        }

        // Nudge the user to turn add-mode off, since it stays on between selections.
        if (addMode)
            EditorGUILayout.HelpBox("Add mode is ON. Click in the Scene view to drop points. " +
                                    "Press the button again to stop.", MessageType.Info);
    }
}
#endif