using UnityEngine;
using System;
using System.Collections;

namespace Twitch.Broadcast
{
    /// <summary>
    /// This component should be placed on the same GameObject as the AudioListener for the scene.
    /// </summary>
    public class UnityAudioCapturer : MonoBehaviour
    {
        #region Member Variables

        private UnityBroadcastController m_BroadcastController = null;
        private UnityBroadcastApi m_Api = null;
        private UnityBroadcastController.UnityPassthroughAudioQueue m_AudioQueue = null;
        private int m_SampleRate = 0;
        private bool m_UsingPassthroughAudio = false;

        #endregion

        #region Unity Overrides

        private void Update()
        {
            // Find the UnityBroadcastController
            if (m_BroadcastController == null)
            {
                UnityEngine.Object[] arr = GameObject.FindObjectsOfType(typeof(UnityBroadcastController));
                if (arr != null && arr.Length > 0)
                {
                    m_BroadcastController = arr[0] as UnityBroadcastController;
                    m_Api = m_BroadcastController.UnityBroadcastApi;
                    m_AudioQueue = m_BroadcastController.PassthroughAudioQueue;
                    m_SampleRate = AudioSettings.outputSampleRate;
                    m_UsingPassthroughAudio = m_BroadcastController.AudioCaptureMethod == BroadcastController.GameAudioCaptureMethod.Passthrough;

                    // We don't need to receive samples if the system isn't configured for passthrough
                    if (!m_UsingPassthroughAudio)
                    {
                        // NOTE: There is currently no way to reenable the capturer if the game changes the capture method at runtime but this should never be the case
                        this.enabled = false;
                    }
                }
            }
        }

        /// <summary>
        /// Receives the audio Unity will be submitting for playback.  When not broadcasting you may disable this component for performance reasons
        /// but you must ensure this is always enabled when broadcasting is starting all the way through until it is stopped.  Failure to do so may cause 
        /// the broadcast to drop since audio must be synced with video.
        ///
        /// NOTE: This is not called on the main thread.
        ///
        /// https://docs.unity3d.com/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            // Submit the samples to the broadcaster
            if (m_AudioQueue != null && m_UsingPassthroughAudio)
            {
                lock (m_AudioQueue)
                {
                    if (m_AudioQueue.AcceptSamples)
                    {
                        // We call the API here directly since we can't access MonoBehaviours on any other thread than the main thread
                        ErrorCode ec = m_Api.SubmitAudioSamples(data, (uint)data.Length, (uint)channels, (uint)m_SampleRate);
                        if (Error.Failed(ec))
                        {
                            // TODO: Maybe we should stop the broadcast?
                            //string err = Error.GetString(ec);
                            //ReportError(err);
                        }
                    }
                }
            }
        }

        #endregion
    }
}
