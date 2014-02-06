using UnityEngine;
using System.Collections.Generic;
using System;
using System.Text;
using Twitch.Chat;
using ErrorCode = Twitch.ErrorCode;


public class ChatGUI : MonoBehaviour
{
	[SerializeField]
	protected string m_UserName = "";
	[SerializeField]
	protected string m_Password = "";
	[SerializeField]
	protected string m_Channel = "";
	[SerializeField]
    protected Twitch.Broadcast.BroadcastController m_BroadcastController = null; //!< We currently depend on getting the auth token from the BroadcastController
	[SerializeField]
	protected UnityChatController m_ChatController = null;
	
	[SerializeField]
	protected String m_Message = string.Empty; //!< Just a hack to allow entering messages for testing quickly
	
	
	public string UserName
	{
		get { return m_UserName; }
		set { m_UserName = value; }
	}
	
	public string Password
	{
		get { return m_Password; }
		set { m_Password = value; }
	}
	
	public string Channel
	{
		get { return m_Channel; }
		set { m_Channel = value; }
	}
	
	void Start()
	{
		DebugOverlay.CreateInstance();
		
		m_ChatController.MessagesCleared += this.HandleClearMessages;
		m_ChatController.Connected += this.HandleConnected;
		m_ChatController.Disconnected += this.HandleDisconnected;
        m_ChatController.RawMessagesReceived += this.HandleRawMessagesReceived;
        m_ChatController.TokenizedMessagesReceived += this.HandleTokenizedMessagesReceived;
        m_ChatController.UsersChanged += this.HandleUsersChanged;
	}
	
	void OnGUI()
	{
		int left = 200;
		int width = 150;
		int height = 30;
		int top = 70;
		int i = 0;

        bool initialize = false;
        bool shutdown = false;
		bool connect = false;
		bool connectAnonymous = false;
		bool disconnect = false;
		bool sendMessage = false;
		
		if (m_ChatController.IsInitialized)
		{
            if (m_ChatController.IsConnected(m_Channel))
            {
                if (!m_ChatController.IsAnonymous(m_Channel))
                {
                    sendMessage = GUI.Button(new Rect(left, top + height * i++, width, height), "Send Message");
                }
                disconnect = GUI.Button(new Rect(left, top + height * i++, width, height), "Disconnect");
            }
            else
            {
                connect = GUI.Button(new Rect(left, top + height * i++, width, height), "Connect");
                connectAnonymous = GUI.Button(new Rect(left, top + height * i++, width, height), "Connect Anonymously");
            }

            shutdown = GUI.Button(new Rect(left, top + height * i++, width, height), "Shutdown");
        }
		else
		{
            initialize = GUI.Button(new Rect(left, top + height * i++, width, height), "Initialize");
        }
		
        if (connect)
		{
			if (string.IsNullOrEmpty(m_Channel.Trim()))
			{
				m_Channel = m_UserName;
			}
			
			m_ChatController.Connect(m_Channel);
		}
        else if (connectAnonymous)
		{
			if (string.IsNullOrEmpty(m_Channel.Trim()))
			{
				m_Channel = m_UserName;
			}
			
            m_ChatController.AuthToken = m_BroadcastController.AuthToken;

			m_ChatController.ConnectAnonymous(m_Channel);
		}
		else if (disconnect)
		{
            m_ChatController.Disconnect(m_Channel);
		}
        else if (sendMessage)
        {
            m_ChatController.SendChatMessage(m_Channel, m_Message);
            m_Message = string.Empty;
        }
        else if (initialize)
        {
            if (m_BroadcastController.AuthToken == null || string.IsNullOrEmpty(m_BroadcastController.AuthToken.Data))
            {
                DebugOverlay.Instance.AddViewportText("Auth token not available");
                return;
            }

            m_ChatController.AuthToken = m_BroadcastController.AuthToken;
            m_ChatController.UserName = m_UserName;

            m_ChatController.Initialize();
        }
        else if (shutdown)
        {
            m_ChatController.Shutdown();
        }
    }
	
	void Update()
	{
		if (DebugOverlay.InstanceExists)
		{
			DebugOverlay.Instance.AddViewportText("Chat: " + m_ChatController.CurrentState.ToString(), 0);
		}
	}
	
	void OnDestroy()
	{
		if (m_ChatController != null)
		{
            m_ChatController.Disconnect(m_Channel);
		}

		m_ChatController.MessagesCleared -= this.HandleClearMessages;
		m_ChatController.Connected -= this.HandleConnected;
		m_ChatController.Disconnected -= this.HandleDisconnected;
		m_ChatController.RawMessagesReceived -= this.HandleRawMessagesReceived;
        m_ChatController.TokenizedMessagesReceived -= this.HandleTokenizedMessagesReceived;
        m_ChatController.UsersChanged -= this.HandleUsersChanged;
	}
	
    #region Callbacks

    protected void HandleRawMessagesReceived(string channelName, ChatRawMessage[] messages)
    {
        if (DebugOverlay.InstanceExists)
        {
            for (int i = 0; i < messages.Length; ++i)
            {
                string line = "[" + messages[i].UserName + "] " + messages[i].Message;
                DebugOverlay.Instance.AddViewportText(line);
            }
        }
    }

    protected void HandleTokenizedMessagesReceived(string channelName, ChatTokenizedMessage[] messages)
    {
        if (DebugOverlay.InstanceExists)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < messages.Length; ++i)
            {
                ChatTokenizedMessage msg = messages[i];
                sb.Append("    <").Append(channelName).Append("> ").Append(msg.DisplayName).Append(": ");

                for (int t = 0; t < msg.Tokens.Length; ++t)
                {
                    ChatMessageToken token = msg.Tokens[t];
                    switch (token.Type)
                    {
                        case TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_TEXT:
                        {
                            ChatTextMessageToken mt = (ChatTextMessageToken)token;
                            sb.Append(mt.Message);
                            break;
                        }
                        case TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_TEXTURE_IMAGE:
                        {
                            ChatTextureImageMessageToken mt = (ChatTextureImageMessageToken)token;
                            sb.Append(String.Format("[{0},{1},{2},{3},{4}]", mt.SheetIndex, mt.X1, mt.Y1, mt.X2, mt.Y2));
                            break;
                        }
                        case TTV_ChatMessageTokenType.TTV_CHAT_MSGTOKEN_URL_IMAGE:
                        {
                            ChatUrlImageMessageToken mt = (ChatUrlImageMessageToken)token;
                            sb.Append("[").Append(mt.Url).Append("]");
                            break;
                        }
                    }
                }

                DebugOverlay.Instance.AddViewportText(sb.ToString());
                sb.Remove(0, sb.Length);
            }
        }
    }

    protected void HandleUsersChanged(string channelName, ChatUserInfo[] joins, ChatUserInfo[] leaves, ChatUserInfo[] infoChanges)
    {
        if (DebugOverlay.InstanceExists)
        {
            for (int i = 0; i < leaves.Length; ++i)
            {
                DebugOverlay.Instance.AddViewportText(leaves[i].DisplayName + " left");
            }

            for (int i = 0; i < infoChanges.Length; ++i)
            {
                // TODO: if we were displaying user attributes we would update them here
            }

            for (int i = 0; i < joins.Length; ++i)
            {
                DebugOverlay.Instance.AddViewportText(joins[i].DisplayName + " joined");
            }
        }
    }

    protected void HandleConnected(string channelName)
    {
        if (DebugOverlay.InstanceExists)
        {
            DebugOverlay.Instance.AddViewportText("Connected");
        }
    }

    protected void HandleDisconnected(string channelName)
    {
        if (DebugOverlay.InstanceExists)
        {
            DebugOverlay.Instance.AddViewportText("Disconnected");
        }
    }

    protected void HandleClearMessages(string channelName)
    {
        if (DebugOverlay.InstanceExists)
        {
            DebugOverlay.Instance.AddViewportText("Messages cleared");
        }
    }

    #endregion
}
