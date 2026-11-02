using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

public static class SceneSetup
{
    public static void Run()
    {
        const string scenePath = "Assets/Scenes/Main.unity";
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        var appObject = GameObject.Find("App");
        if (appObject == null)
        {
            appObject = new GameObject("App");
        }
        if (appObject.GetComponent<App>() == null)
        {
            appObject.AddComponent<App>();
        }

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        if (scene.path != scenePath)
        {
            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, scenePath);
        }
        else
        {
            EditorSceneManager.SaveScene(scene);
        }

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        var found = false;
        for (var i = 0; i < scenes.Count; i++)
        {
            if (scenes[i].path == scenePath)
            {
                scenes[i] = new EditorBuildSettingsScene(scenePath, true);
                found = true;
                break;
            }
        }
        if (!found)
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneSetup] done");
    }
}
