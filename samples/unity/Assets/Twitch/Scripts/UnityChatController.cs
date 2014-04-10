using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Twitch;
using UnityEngine;
using ErrorCode = Twitch.ErrorCode;

namespace Twitch.Chat
{
    public class UnityChatController : ChatController
    {
        #region Memeber Variables

        [SerializeField]
        protected string m_ClientId = "";
        [SerializeField]
        protected string m_ClientSecret = "";
        [SerializeField]
        protected EmoticonMode m_EmoticonMode = EmoticonMode.None;
        [SerializeField]
        protected int m_MessageFlushInterval = 500;
        [SerializeField]
        protected int m_UserChangeEventInterval = 2000;

        private UnityIosChatApi m_UnityIosChatApi = null;

        #endregion

        #region Properties

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

        public override EmoticonMode EmoticonParsingMode
        {
            get { return m_EmoticonMode; }
            set { m_EmoticonMode = value; }
        }

        public override int MessageFlushInterval
        {
            get { return m_MessageFlushInterval; }
            set
            {
                value = Math.Min(MAX_INTERVAL_MS, Math.Max(value, MIN_INTERVAL_MS));

                m_MessageFlushInterval = value;
                base.MessageFlushInterval = value;
            }
        }

        public override int UserChangeEventInterval
        {
            get { return m_UserChangeEventInterval; }
            set
            {
                value = Math.Min(MAX_INTERVAL_MS, Math.Max(value, MIN_INTERVAL_MS));

                m_UserChangeEventInterval = value;
                base.UserChangeEventInterval = value;
            }
        }

        #endregion

        #region Unity Overrides

        protected void Awake()
        {
            // force the twitch libraries to be loaded
            Twitch.Broadcast.UnityBroadcastController.LoadTwitchLibraries();

            m_CoreApi = CoreApi.Instance;

            if (Application.platform == RuntimePlatform.WindowsEditor ||
                Application.platform == RuntimePlatform.WindowsPlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new StandardCoreApi();
                }

                m_ChatApi = new StandardChatApi();
            }
            else if (Application.platform == RuntimePlatform.OSXEditor ||
                     Application.platform == RuntimePlatform.OSXPlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new StandardCoreApi();
                }

                m_ChatApi = new StandardChatApi();
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                if (m_CoreApi == null)
                {
                    m_CoreApi = new UnityIosCoreApi();
                }

                m_UnityIosChatApi = new UnityIosChatApi();
                m_ChatApi = m_UnityIosChatApi;
            }
            else if (Application.platform == RuntimePlatform.Android)
            {
                // TODO: not supported yet
            }
            else
            {
                // TODO: not supported yet
            }
        }

        protected void OnDestroy()
        {
            ForceSyncShutdown();
        }

        public override void Update()
        {
            // for awareness that the Unity Update hook is actually being called
            base.Update();
        }

        #endregion

        #region Error Handling

        protected override void CheckError(Twitch.ErrorCode err)
        {
            if (err != Twitch.ErrorCode.TTV_EC_SUCCESS)
            {
                Debug.LogError(err.ToString());
            }
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

        #region ChatAPI Callback Proxy Handlers

        private void ProxyInitializationCallback(string json)
        {
            m_UnityIosChatApi.ChatInitializationCallback(json);
        }

        private void ProxyShutdownCallback(string json)
        {
            m_UnityIosChatApi.ChatShutdownCallback(json);
        }

        private void ProxyEmoticonDataDownloadCallback(string json)
        {
            m_UnityIosChatApi.ChatEmoticonDataDownloadCallback(json);
        }

        private void ProxyStatusCallback(string json)
        {
            m_UnityIosChatApi.ChatStatusCallback(json);
        }

        private void ProxyMembershipCallback(string json)
        {
            m_UnityIosChatApi.ChatChannelMembershipCallback(json);
        }

        public void ProxyChannelUserChangeCallback(string json)
        {
            m_UnityIosChatApi.ChatChannelUserChangeCallback(json);
        }

        public void ProxyChannelRawMessageCallback(string json)
        {
            m_UnityIosChatApi.ChatChannelRawMessageCallback(json);
        }

        public void ProxyChannelTokenizedMessageCallback(string json)
        {
            m_UnityIosChatApi.ChatChannelTokenizedMessageCallback(json);
        }

        public void ProxyClearCallback(string json)
        {
            m_UnityIosChatApi.ChatClearCallback(json);
        }

        public void ProxyBadgeDataDownloadCallback(string json)
        {
            m_UnityIosChatApi.ChatBadgeDataDownloadCallback(json);
        }

        #endregion
    }
}
