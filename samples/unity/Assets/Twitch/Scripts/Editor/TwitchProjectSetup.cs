using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Twitch.Broadcast;
using Twitch.Chat;

namespace Twitch
{
	public class TwitchProjectSetup : Editor
	{
        [MenuItem("Twitch/Configure Current Scene")]
        public static void SetupScene()
        {
            SetupTwitchSceneConfigurator();
            UpdateUnityAudioCapturer();
        }

        private static void SetupTwitchSceneConfigurator()
        {
            // Add/find the TwitchSceneConfigurator
            TwitchSceneConfigurator configurator = Object.FindObjectOfType(typeof(TwitchSceneConfigurator)) as TwitchSceneConfigurator;

            if (configurator == null)
            {
                UnityEngine.GameObject prefab = AssetDatabase.LoadAssetAtPath("Assets/Twitch/Examples/Prefabs/TwitchSceneConfigurator.prefab", typeof(GameObject)) as UnityEngine.GameObject;
                if (prefab == null)
                {
                    Debug.LogError("Could not find Assets/Twitch/Examples/Prefabs/TwitchSceneConfigurator.prefab");
                    return;
                }

                UnityEngine.GameObject obj = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                configurator = obj.GetComponent<TwitchSceneConfigurator>();

                Debug.Log("Create new instance of TwitchSceneConfigurator");
            }

            if (configurator == null)
            {
                Debug.LogError("Could not find TwitchSceneConfigurator");
                return;
            }

            // Find all cameras and add them to the configurator
            Camera[] cameras = GameObject.FindObjectsOfType<Camera>();

            // TODO: do some more specific checking to ensure that we don't include the wrong cameras or exclude ones we shouldn't

            for (int i = 0; i < cameras.Length; ++i)
            {
                if (cameras[i].tag != "MainCamera")
                {
                    cameras[i] = null;
                }
                else
                {
                    Debug.Log("Adding camera to TwitchSceneConfigurator: " + cameras[i].gameObject.name);
                }
            }

            configurator.SetCameras(cameras);
        }

        private static void UpdateUnityAudioCapturer()
        {
            AudioListener listener = GameObject.FindObjectOfType<AudioListener>();

            if (listener == null)
            {
                Debug.LogError("There is no AudioListener in the scene");
                return;
            }

            // Add the UnityAudioCapturer component to the main cameras
            UnityAudioCapturer[] arr = Object.FindObjectsOfType(typeof(UnityAudioCapturer)) as UnityAudioCapturer[];

            if (arr.Length == 0)
            {
                listener.gameObject.AddComponent<UnityAudioCapturer>();
                Debug.Log("Adding UnityAudioCapturer to AudioListener: " + listener.gameObject.name);
            }
            else if (arr.Length == 1)
            {
                if (listener.gameObject != arr[0].gameObject)
                {
                    Debug.LogError("The UnityAudioCapturer is not on the AudioListener GameObject.  Please delete the stray one and rerun the setup script.");
                }
            }
            else if (arr.Length > 1)
            {
                Debug.LogError("There is more than one UnityAudioCapturer in the scene and will cause issues.  Please delete one and ensure there is one on your AudioListener GameObject.");
            }
        }
    }
}
