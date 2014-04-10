using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Hooks up the configured cameras to the RenderTextureResizer instance during Start() and unhooks them during OnDestroy().
/// </summary>
public class TwitchSceneConfigurator : MonoBehaviour
{
	[SerializeField]
	protected Camera[] m_SceneCameras = new Camera[0];
    [SerializeField]
    protected bool m_GrabCamerasOnStart = false;

    protected List<Camera> m_CurrentCameras = new List<Camera>();
    protected bool m_SetupComplete = false;


    public void SetCameras(Camera[] cameras)
    {
        if (cameras == null)
        {
            m_SceneCameras = new Camera[0];
        }
        else
        {
            m_SceneCameras = new Camera[cameras.Length];

            for (int i = 0; i < cameras.Length; ++i)
            {
                m_SceneCameras[i] = cameras[i];
            }
        }
    }

    #region Unity Overrides

    protected void Update()
    {
        if (m_SetupComplete)
        {
            return;
        }

        RenderTextureResizer rtr = RenderTextureResizer.Instance;
        if (rtr == null)
        {
            //Debug.LogError("Unable to find RenderTextureResizer, could not hook up cameras");
            return;
        }

        if (m_GrabCamerasOnStart)
        {
            Camera[] cameras = GameObject.FindObjectsOfType<Camera>();

            for (int i = 0; i < cameras.Length; ++i)
            {
                if (cameras[i].tag == "MainCamera")
                {
                    m_CurrentCameras.Add(cameras[i]);
                }
            }
        }

        if (m_SceneCameras != null)
        {
            for (int i = 0; i < m_SceneCameras.Length; ++i)
            {
                if (m_SceneCameras[i] != null)
                {
                    m_CurrentCameras.Add(m_SceneCameras[i]);
                }
            }
        }

        for (int i = 0; i < m_CurrentCameras.Count; ++i)
        {
            rtr.AddCamera(m_CurrentCameras[i]);
        }

        m_SetupComplete = true;
    }

    protected void OnDestroy()
    {
        RenderTextureResizer rtr = RenderTextureResizer.Instance;
        if (rtr == null)
        {
            return;
        }

        for (int i = 0; i < m_CurrentCameras.Count; ++i)
        {
            if (m_CurrentCameras[i] != null)
            {
                rtr.RemoveCamera(m_CurrentCameras[i]);
            }
        }
    }

    #endregion
}
