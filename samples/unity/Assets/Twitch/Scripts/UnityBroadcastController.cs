using System;
using System.Collections.Generic;
using UnityEngine;
using Twitch;
using ErrorCode = Twitch.ErrorCode;

namespace Twitch.Broadcast
{
    /// <summary>
    /// Once broadcasting begins, frames are submitted automatically for broadcasting in the Update() call.  The real time will be used to determine the rate of frame 
    /// submission and is independent of Time.time.  At most one frame will be submitted per Update() call so you need to ensure that it will be called frequently enough to 
    /// satisfy the FPS you have configured for broadcasting.  The maximum allowed time step will be a limiter in determing the maximum FPS in which the frames 
    /// can be submitted for broadcasting.  For 30 FPS (which is the recommended maximum FPS allowed) make sure the project has a value of 0.03333 seconds configured 
    /// for the "Maximum Allowed Timestep" property available via Edit->Project Settings->Time.
    /// 
    /// The general setup for broadcasting is as follows:
    /// - Render your scene to a RenderTexture at the same aspect ratio as the Screen.  Call this RenderTexture the scene RT.  
    /// - Render the scene RT to the screen using a quad.
    /// - After hooking up the scene RT to the BroadcastController (BC), the BC will automatically setup the needed Camera and generate a RenderTexture which will be appropriate
    ///   for generating the image for submitting for broadcasting.
    ///   
    /// Concerning aspect ratios.  There are 3 aspect ratios (ARs) to consider when setting up your game: the screen, the broadcast and the video player on the site.  If any
    /// of these don't match then there will be black borders added to compensate, potentially at each different stage.  As of the time of this writing, the video player has an AR 
    /// of 1.7777777 which is that of 1080p.  If the Screen AR doesn't match the broadcast AR (configured by the width and height parameters in VideoParams) there will be 
    /// bordering introduced to compensate.  Then, if the broadcast AR doesn't match the video player AR then even more black bordering could be introduced.  Thus, ideally you'll 
    /// setup VideoParams to have a 1.777777 AR (to match the video player) and configure the Screen resolution to as closely match the broadcast as possible.
    /// Another possibility could be to render the scene twice: once to the screen for the screen AR and another for the broadast at an AR of 1.77777.  This would produce the best results but
    /// would most likely be too performance intensive so isn't recommended but does illustrate the point.
    /// </summary>
    public class UnityBroadcastController : BroadcastController
    {
        #region Types

        /// <summary>
        /// A proxy object which provides access to important properties concerning passthrough audio.  You can't
        /// directly access GameObjects from a thread other than the main thread and OnAudioFilterRead data comes
        /// in on the audio thread.
        /// </summary>
        public class UnityPassthroughAudioQueue
        {
            private bool m_AcceptSamples = false;

            public bool AcceptSamples
            {
                get { return m_AcceptSamples; }
                set { m_AcceptSamples = value; }
            }
        }

        #endregion

        #region Member Variables

        [SerializeField]
        protected string m_ClientId = "";
        [SerializeField]
        protected string m_ClientSecret = "";
        [SerializeField]
        protected RenderTexture m_SceneRenderTexture = null;        //!< The texture that will be used as the source of the broadcast.
        [SerializeField]
        protected bool m_CaptureGameAudio = true;
        [SerializeField]
        protected bool m_CaptureMicrohpone = true;

        private GameAudioCaptureMethod m_AudioCaptureMethod = GameAudioCaptureMethod.None;

        private float m_LastCaptureTime = 0;						//!< The timestamp of the last frame capture.

        private UnityPassthroughAudioQueue m_AudioQueue = new UnityPassthroughAudioQueue();
        private UnityBroadcastApi m_UnityBroadcastApi = null;

        #endregion

        #region Properties

        public UnityBroadcastApi UnityBroadcastApi
        {
            get { return m_UnityBroadcastApi; }
        }

        public UnityPassthroughAudioQueue PassthroughAudioQueue
        {
            get { return m_AudioQueue; }
        }

        public override string ClientId
        {
            get { return m_ClientId; }
            set { m_ClientId = value; }
        }

        public override string ClientSecret
        {
            get { return m_ClientSecret; }
            set { m_ClientSecret = value; }
        }

        public override bool CaptureMicrophone
        {
            get { return m_CaptureMicrohpone; }
            set { m_CaptureMicrohpone = value; }
        }

        public override GameAudioCaptureMethod AudioCaptureMethod
        {
            get
            {
                // Unity doesn't have the option of multiple audio capture sources
                if (m_CaptureGameAudio)
                {
                    return m_AudioCaptureMethod;
                }
                else
                {
                    return GameAudioCaptureMethod.None;
                }
            }
            // the capture method is determined by the platform
            set { }
        }

        public override bool IsAudioSupported
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor ||
                    Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    // http://stackoverflow.com/questions/2819934/detect-windows-7-in-net

                    OperatingSystem os = System.Environment.OSVersion;

                    if (os != null && os.Platform != PlatformID.Win32Windows)
                    {
                        Version v = os.Version;

                        if (v.Major < 6) // Windows Vista
                        {
                            return false;
                        }

                        return true;
                    }

                    // not a valid Windows
                    return false;
                }
                else if (Application.platform == RuntimePlatform.OSXEditor ||
                         Application.platform == RuntimePlatform.OSXPlayer)
                {
                    return true;
                }
                else
                {
                    return true;
                }
            }
        }

        public override bool IsPlatformSupported
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor ||
                    Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    // http://stackoverflow.com/questions/2819934/detect-windows-7-in-net

                    OperatingSystem os = System.Environment.OSVersion;

                    if (os != null && os.Platform != PlatformID.Win32Windows)
                    {
                        Version v = os.Version;

                        if (v.Major < 6) // Windows Vista
                        {
                            return false;
                        }

                        return true;
                    }

                    // not a valid Windows
                    return false;
                }
                else if (Application.platform == RuntimePlatform.OSXEditor ||
                         Application.platform == RuntimePlatform.OSXPlayer)
                {
                    return true;
                }
                else if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    return true;
                }
                else if (Application.platform == RuntimePlatform.Android)
                {
                    // TODO: not supported yet
                    return false;
                }
                else
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// The RenderTexture which contains the scene to broadcast.
        /// </summary>
        public RenderTexture SceneRenderTexture
        {
            get { return m_SceneRenderTexture; }
            set
            {
                // no change
                if (value == m_SceneRenderTexture)
                {
                    return;
                }

                m_SceneRenderTexture = value;
            }
        }

        #endregion

        #region Library Loading

        [System.Runtime.InteropServices.DllImport("kernel32", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl, EntryPoint = "LoadLibraryW")]
        internal static extern UIntPtr LoadWindowsLibrary([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] string path);

        internal static bool LoadTwitchLibrary(string path)
        {
            if (LoadWindowsLibrary(path) == UIntPtr.Zero)
            {
                Debug.LogError("Failed to load Twitch native library: " + path);
                return false;
            }
            else
            {
                Debug.Log("Loaded Twitch native library: " + path);
                return true;
            }
        }

        internal static void LoadTwitchLibrariesFromBasePath(string path)
        {
            // now manually load all the dependent libraries in reverse order
            switch (IntPtr.Size)
            {
                // 32-bit libraries
                case 4:
                    {
                        LoadTwitchLibrary(path + "\\avutil-ttv-51.dll");
                        LoadTwitchLibrary(path + "\\swresample-ttv-0.dll");
                        LoadTwitchLibrary(path + "\\libmp3lame-ttv.dll");
                        LoadTwitchLibrary(path + "\\libmfxsw32.dll");
                        LoadTwitchLibrary(path + "\\twitchsdk.dll");
                        break;
                    }
                // 64-bit libraries
                case 8:
                    {
                        LoadTwitchLibrary(path + "\\avutil-ttv-51.dll");
                        LoadTwitchLibrary(path + "\\swresample-ttv-0.dll");
                        LoadTwitchLibrary(path + "\\libmp3lame-ttv.dll");
                        LoadTwitchLibrary(path + "\\libmfxsw64.dll");
                        LoadTwitchLibrary(path + "\\twitchsdk.dll");
                        break;
                    }
                default:
                    {
                        Debug.LogError("Unable to load Twitch libraries, unknown Windows platform with word size: " + IntPtr.Size);
                        break;
                    }
            }
        }

        internal static void LoadTwitchLibraries()
        {
#if !UNITY_WEBPLAYER
            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                string path = null;

                // look in the same directory as twitchsdkwrapper.dll
                path = new System.IO.FileInfo(System.Reflection.Assembly.GetAssembly(typeof(Twitch.Broadcast.UnityBroadcastApi)).Location).Directory.FullName;
                if (System.IO.File.Exists(path + System.IO.Path.DirectorySeparatorChar + "twitchsdk.dll"))
                {
                    LoadTwitchLibrariesFromBasePath(path);
                    return;
                }

                // look in the ../Managed directory since this might be a built game
                path = new System.IO.FileInfo(System.Reflection.Assembly.GetAssembly(typeof(Twitch.Broadcast.UnityBroadcastApi)).Location).Directory.Parent.FullName;
                path = path + System.IO.Path.DirectorySeparatorChar + "Plugins";
                if (System.IO.File.Exists(path + System.IO.Path.DirectorySeparatorChar + "twitchsdk.dll"))
                {
                    LoadTwitchLibrariesFromBasePath(path);
                    return;
                }

                // look in the platform-specific directory
                path = new System.IO.FileInfo(System.Reflection.Assembly.GetAssembly(typeof(Twitch.Broadcast.UnityBroadcastApi)).Location).Directory.FullName;
                path = path + System.IO.Path.DirectorySeparatorChar;
                switch (IntPtr.Size)
                {
                    // 32-bit libraries
                    case 4:
                        {
                            path = path + "x86";
                            break;
                        }
                    // 64-bit libraries
                    case 8:
                        {
                            path = path + "x86_64";
                            break;
                        }
                }

                if (System.IO.File.Exists(path + System.IO.Path.DirectorySeparatorChar + "twitchsdk.dll"))
                {
                    LoadTwitchLibrariesFromBasePath(path);
                    return;
                }

                Debug.LogError("Unable to find the directory containing Twitch native libraries");
            }
            else
            {
                // not sure if anything needs to be done on other platforms
            }
#endif
        }

        #endregion

        #region Unity Overrides

        protected void OnApplicationPause(bool pause)
        {
            if (pause)
            {
                if (this.IsBroadcasting)
                {
                    this.StopBroadcasting();
                }
                else if (this.IsIngestTesting)
                {
                    this.CancelIngestTest();
                }
            }
        }

        protected void OnApplicationQuit()
        {
            //ForceSyncShutdown();
        }

        protected void Awake()
        {
            LoadTwitchLibraries();

            m_CoreApi = CoreApi.Instance;

            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new StandardCoreApi();
                }

                m_UnityBroadcastApi = new DesktopUnityBroadcastApi();
                m_AudioCaptureMethod = GameAudioCaptureMethod.SystemCapture;
            }
            else if (Application.platform == RuntimePlatform.OSXEditor ||
                     Application.platform == RuntimePlatform.OSXPlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new StandardCoreApi();
                }

                m_UnityBroadcastApi = new DesktopUnityBroadcastApi();
                m_AudioCaptureMethod = GameAudioCaptureMethod.Passthrough;
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new UnityIosCoreApi();
                }

                m_UnityBroadcastApi = new UnityIosBroadcastApi();
                m_AudioCaptureMethod = GameAudioCaptureMethod.Passthrough;
            }
            else if (Application.platform == RuntimePlatform.Android)
            {
                // TODO: not supported yet
            }
            else
            {
                // TODO: not supported yet
            }

            m_BroadcastApi = m_UnityBroadcastApi;
        }

        protected void OnDestroy()
        {
            Shutdown();

            // force a low-level shutdown
            if (m_UnityBroadcastApi != null)
            {
                ForceSyncShutdown();
            }
        }

        public override void Update()
        {
            base.Update();

            // Submit video frame
            if (m_BroadcastState == BroadcastState.Broadcasting)
            {
                float curTime = Time.realtimeSinceStartup;

                // If you send frames too quickly to the SDK (based on the broadcast FPS you configured) it will not be able 
                // to make use of them all.  In that case, it will simply release buffers without using them which means the
                // game wasted time doing the capture.  To mitigate this, the app should pace the captures to the broadcast FPS.
                float captureDelta = curTime - m_LastCaptureTime;
                bool isTimeForNextCapture = captureDelta >= (1.0f / (float)m_VideoParams.TargetFps);

                if (!isTimeForNextCapture)
                {
                    return;
                }

                IntPtr p = m_SceneRenderTexture.GetNativeTexturePtr();
                if (p != IntPtr.Zero)
                {
                    ErrorCode ret = m_UnityBroadcastApi.SubmitTexturePointer(p, m_SceneRenderTexture.width, m_SceneRenderTexture.height);

                    // mark the time of the first video frame submission (don't need to lock since we're setting a bool)
                    m_AudioQueue.AcceptSamples = m_AudioParams.EnablePassthroughAudio;

                    // if there is a problem when submitting a frame let the client know
                    if (ret != ErrorCode.TTV_EC_SUCCESS)
                    {
                        string err = Error.GetString(ret);
                        if (Error.Succeeded(ret))
                        {
                            ReportWarning(string.Format("Warning in SubmitTexturePointer: {0}\n", err));
                        }
                        else
                        {
                            ReportError(string.Format("Error in SubmitTexturePointer: {0}\n", err));

                            // errors are not recoverable
                            StopBroadcasting();
                        }

                        FireFrameSubmissionIssue(ret);
                    }
                }

                m_LastCaptureTime = curTime;

                // TODO: we may need to handle multi-threaded rendering which means that we can't submit the texture from Update(), we need to use a render event
                //GL.IssuePluginEvent((int)123456);
            }
        }

        #endregion

        public override bool StopBroadcasting()
        {
            bool stopped = false;
            lock (m_AudioQueue)
            {
                stopped = base.StopBroadcasting();
                if (stopped)
                {
                    m_AudioQueue.AcceptSamples = false;
                }
            }

            return stopped;
        }

        public override PixelFormat DeterminePixelFormat()
        {
            PixelFormat format = base.DeterminePixelFormat();

            ErrorCode ret = m_UnityBroadcastApi.GetCapturePixelFormat(ref format);
            if (Error.Failed(ret))
            {
                string err = Error.GetString(ret);
                ReportError(string.Format("Error in GetCapturePixelFormat: {0}", err));
            }

            return format;
        }

        protected override bool AllocateBuffers()
        {
            HandleSceneRenderTextureChange();

            return true;
        }

        protected void HandleSceneRenderTextureChange()
        {
            m_LastCaptureTime = Time.realtimeSinceStartup;
        }

        protected override void CleanupBuffers()
        {
        }

        #region Error Handling

        protected override bool CheckError(Twitch.ErrorCode err)
        {
            if (Error.Failed(err))
            {
                Debug.LogError(err.ToString());
                return false;
            }

            return true;
        }

        protected override void ReportError(string err)
        {
            Debug.LogError(err);
        }

        protected override void ReportWarning(string err)
        {
            Debug.LogError(err);
        }

        #endregion

        #region BroadcastApi Callback Proxy Handlers

        private void ProxyRequestAuthTokenCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).RequestAuthTokenCallback(json);
        }

        private void ProxyLoginCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).LoginCallback(json);
        }

        private void ProxyGetIngestListCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).GetIngestListCallback(json);
        }

        private void ProxyGetUserInfoCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).GetUserInfoCallback(json);
        }

        private void ProxyGetStreamInfoCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).GetStreamInfoCallback(json);
        }

        private void ProxyGetArchivingStateCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).GetArchivingStateCallback(json);
        }

        private void ProxyRunCommercialCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).RunCommercialCallback(json);
        }

        //private void ProxyGetGameLiveStreamsCallback(string json)
        //{
        //              (m_UnityBroadcastApi as UnityIosBroadcastApi).GetGameLiveStreamsCallback(json);
        //}

        private void ProxyStartCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).StartCallback(json);
        }

        private void ProxyStopCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).StopCallback(json);
        }

        private void ProxySendActionMetaDataCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).SendActionMetaDataCallback(json);
        }

        private void ProxySendStartSpanMetaDataCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).SendStartSpanMetaDataCallback(json);
        }

        private void ProxySendEndSpanMetaDataCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).SendEndSpanMetaDataCallback(json);
        }

        private void ProxySetStreamInfoCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).SetStreamInfoCallback(json);
        }

        private void ProxyBufferUnlockCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).BufferUnlockCallback(json);
        }

        private void ProxyStatCallback(string json)
        {
            (m_UnityBroadcastApi as UnityIosBroadcastApi).StatCallback(json);
        }

        #endregion
    }
}
