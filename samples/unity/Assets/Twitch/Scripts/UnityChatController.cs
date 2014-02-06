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

            m_Core = Core.Instance;

            if (m_Core == null)
            {
                m_Core = new Core(new StandardCoreAPI());
            } 
            
            m_Chat = new Chat(new StandardChatAPI());
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
	}
}
