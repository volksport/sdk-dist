using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Twitch;
using ErrorCode = Twitch.ErrorCode;

namespace Twitch.Chat
{
    /// <summary>
    /// The state machine which manages the chat state.  It provides a high level interface to the SDK libraries.  The ChatController (CC) performs many operations
    /// asynchronously and hides the details.  This class can be tweaked if needed but should handle all your chat needs (other than emoticons which may be provided in the future).
    /// 
    /// The typical order of operations a client of CC will take is:
    /// 
    /// - Subscribe for events via delegates on ChatController
    /// - Call CC.Initialize() when starting your game
    /// - Wait for the initialization callback 
    /// - Call CC.Connect()
    /// - Wait for the connection callback 
    /// - Call CC.SendChatMessage() to send messages (if not connected anonymously)
    /// - Receive message callbacks
    /// - Call CC.Disconnect() when done
    /// - Wait for the disconnection callback 
    /// - Call CC.Shutdown() when shutting down your game
    /// 
    /// Events will fired during the call to CC.Update().  When chat messages are received RawMessagesReceived will be fired.
    /// 
    /// NOTE: The implementation of texture emoticon data is not yet complete and currently not available.
    /// </summary>
    public abstract partial class ChatController
    {
        protected const int MIN_INTERVAL_MS = 200;
        protected const int MAX_INTERVAL_MS = 10000;

        #region Types

        /// <summary>
        /// The possible states the ChatController can be in.
        /// </summary>
        public enum ChatState
        {
            Uninitialized,  //!< The component is not yet initialized.
            Initializing,   //!< The component is initializing.
            Initialized,    //!< The component is initialized.
            ShuttingDown, 	//!< The component is shutting down.
        }

        /// <summary>
        /// The possible states a chat channel can be in.
        /// </summary>
        protected enum ChannelState
        {
            Created,
            Connecting,
            Connected,
            Disconnecting,
            Disconnected,
        }

        /// <summary>
        /// The emoticon parsing mode for chat messages.
        /// </summary>
        public enum EmoticonMode
        {
            None,			//!< Do not parse out emoticons in messages.
            Url, 			//!< Parse out emoticons and return urls only for images.
            TextureAtlas 	//!< Parse out emoticons and return texture atlas coordinates.
        }

        /// <summary>
        /// The callback for the event fired when initialization is complete.
        /// </summary>
        /// <param name="result"></param>
        public delegate void InitializationCompleteDelegate(ErrorCode result);

        /// <summary>
        /// The callback for the event fired when shutdown is complete.
        /// </summary>
        /// <param name="result"></param>
        public delegate void ShutdownCompleteDelegate(ErrorCode result);

        /// <summary>
        /// The callback signature for the event fired when the emoticon data has been made available.
        /// </summary>
        public delegate void EmoticonDataAvailableDelegate();

        /// <summary>
        /// The callback signature for the event fired when the emoticon data is no longer valid.
        /// </summary>
        public delegate void EmoticonDataExpiredDelegate();

        /// <summary>
        /// The callback signature for the event which is fired when the ChatController changes state.
        /// </summary>
        /// <param name="state">The new state.</param>
        public delegate void ChatStateChangedDelegate(ChatState state);

        /// <summary>
        /// The callback signature for the event fired when a tokenized set of messages has been received.
        /// </summary>
        /// <param name="messages">The list of messages</param>
        public delegate void TokenizedMessagesReceivedDelegate(string channelName, ChatTokenizedMessage[] messages);

        /// <summary>
        /// The callback signature for the event fired when a set of text-only messages has been received.
        /// </summary>
        /// <param name="messages">The list of messages</param>
        public delegate void RawMessagesReceivedDelegate(string channelName, ChatRawMessage[] messages);

        /// <summary>
        /// The callback signature for the event fired when users join, leave or changes their status in the channel.
        /// </summary>
        /// <param name="joinList">The list of users who have joined the room.</param>
        /// <param name="leaveList">The list of useres who have left the room.</param>
        /// <param name="userInfoList">The list of users who have changed their status.</param>
        public delegate void UsersChangedDelegate(string channelName, ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList);

        /// <summary>
        /// The callback signature for the event fired when the local user has been connected to the channel.
        /// </summary>
        public delegate void ConnectedDelegate(string channelName);

        /// <summary>
        /// The callback signature for the event fired when the local user has been disconnected from the channel.
        /// </summary>
        public delegate void DisconnectedDelegate(string channelName);

        /// <summary>
        /// The callback signature for the event fired when the messages in the room should be cleared.  The UI should be cleared of any previous messages.
        /// If username is null or empty then the entire log was cleared, otherwise only messages for the given user were cleared.
        /// </summary>
        public delegate void ClearMessagesDelegate(string channelName, string username);

        /// <summary>
        /// The callback signature for the event fired when the badge data has been made available.
        /// </summary>
        /// <param name="channelName"></param>
        public delegate void BadgeDataAvailableDelegate(String channelName);

        /// <summary>
        /// The callback signature for the event fired when the badge data is no longer valid.
        /// </summary>
        /// <param name="channelName"></param>
        public delegate void BadgeDataExpiredDelegate(String channelName);

        #endregion

        #region Memeber Variables

        public event InitializationCompleteDelegate InitializationComplete;
        public event ShutdownCompleteDelegate ShutdownComplete;
        public event BadgeDataExpiredDelegate BadgeDataAvailable;
        public event BadgeDataExpiredDelegate BadgeDataExpired;
        public event ChatStateChangedDelegate ChatStateChanged;
        public event TokenizedMessagesReceivedDelegate TokenizedMessagesReceived;
        public event RawMessagesReceivedDelegate RawMessagesReceived;
        public event UsersChangedDelegate UsersChanged;
        public event ConnectedDelegate Connected;
        public event DisconnectedDelegate Disconnected;
        public event ClearMessagesDelegate MessagesCleared;
        public event EmoticonDataAvailableDelegate EmoticonDataAvailable;
        public event EmoticonDataExpiredDelegate EmoticonDataExpired;

        protected string m_UserName = "";
        protected Twitch.Core m_Core = null;
        protected Twitch.Chat.Chat m_Chat = null;

        protected ChatState m_ChatState = ChatState.Uninitialized;
        protected AuthToken m_AuthToken = new AuthToken();

        protected ChatApiListener m_ChatAPIListener = null;
        protected Dictionary<string, ChatChannelListener> m_Channels = new Dictionary<string, ChatChannelListener>();

        protected uint m_MessageHistorySize = 128;
        protected EmoticonMode m_ActiveEmoticonMode = EmoticonMode.None;
        protected ChatEmoticonData m_EmoticonData = null;

        #endregion

        protected abstract class ControllerAccess
        {
            protected ChatController m_ChatController;

            protected ControllerAccess(ChatController controller)
            {
                m_ChatController = controller;
            }

            protected EmoticonMode ActiveEmoticonMode
            {
                get { return m_ChatController.m_ActiveEmoticonMode; }
            }

            protected uint MessageHistorySize
            {
                get { return m_ChatController.MessageHistorySize; }
            }

            protected int MessageFlushInterval
            {
                get { return m_ChatController.MessageFlushInterval; }
            }

            protected int UserChangeEventInterval
            {
                get { return m_ChatController.UserChangeEventInterval; }
            }

            protected string UserName
            {
                get { return m_ChatController.UserName; }
            }
            
            protected string AuthToken
            {
                get { return m_ChatController.AuthToken.Data; }
            }

            protected Dictionary<string, ChatChannelListener> Channels
            {
                get { return m_ChatController.m_Channels; }
            }

            protected Chat Api
            {
                get { return m_ChatController.m_Chat; }
            }

            protected void CheckError(ErrorCode err)
            {
                m_ChatController.CheckError(err);
            }

            protected void ReportError(string err)
            {
                m_ChatController.ReportError(err);
            }

            protected void ReportWarning(string err)
            {
                m_ChatController.ReportWarning(err);
            }

            protected void FireConnected(String channelName)
            {
                try
                {
                    if (m_ChatController.Connected != null)
                    {
                        m_ChatController.Connected(channelName);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireDisconnected(String channelName)
            {
                try
                {
                    if (m_ChatController.Disconnected != null)
                    {
                        m_ChatController.Disconnected(channelName);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireMessagesCleared(string channelName, string username)
            {
                try
                {
                    if (m_ChatController.MessagesCleared != null)
                    {
                        m_ChatController.MessagesCleared(channelName, username);
                    }
                }
                catch (Exception x)
                {
                    ReportError(string.Format("Error clearing chat messages: {0}", x.ToString()));
                }
            }

            protected void FireBadgeDataAvailable(string channelName)
            {
                try
                {
                    if (m_ChatController.BadgeDataAvailable != null)
                    {
                        m_ChatController.BadgeDataAvailable(channelName);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireBadgeDataExpired(string channelName)
            {
                try
                {
                    if (m_ChatController.BadgeDataExpired != null)
                    {
                        m_ChatController.BadgeDataExpired(channelName);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireUsersChanged(string channelName, ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList)
            {
                try
                {
                    if (m_ChatController.UsersChanged != null)
                    {
                        m_ChatController.UsersChanged(channelName, joinList, leaveList, userInfoList);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireRawMessagesReceived(string channelName, ChatRawMessage[] messages)
            {
                try
                {
                    if (m_ChatController.RawMessagesReceived != null)
                    {
                        m_ChatController.RawMessagesReceived(channelName, messages);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            protected void FireTokenizedMessagesReceived(string channelName, ChatTokenizedMessage[] messages)
            {
                try
                {
                    if (m_ChatController.TokenizedMessagesReceived != null)
                    {
                        m_ChatController.TokenizedMessagesReceived(channelName, messages);
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }
        }

        protected class ChatApiListener : ControllerAccess, IChatAPIListener
        {
            public ChatApiListener(ChatController controller) : base(controller)
            {
            }

            #region IChatAPIListener Implementation

            void IChatAPIListener.ChatInitializationCallback(ErrorCode result)
            {
                if (Error.Succeeded(result))
                {
                    Api.SetMessageFlushInterval(this.MessageFlushInterval);
                    Api.SetUserChangeEventInterval(this.UserChangeEventInterval);

                    m_ChatController.DownloadEmoticonData();

                    m_ChatController.SetChatState(ChatState.Initialized);
                }
                else
                {
                    m_ChatController.SetChatState(ChatState.Uninitialized);
                }

        	    try
        	    {
		            if (m_ChatController.InitializationComplete != null)
		            {
		        	    m_ChatController.InitializationComplete(result);
		            }
        	    }
        	    catch (Exception x)
        	    {
        		    m_ChatController.ReportError(x.ToString());
        	    }
            }

            void IChatAPIListener.ChatShutdownCallback(ErrorCode result)
            {
                if (Error.Succeeded(result))
                {
                    ErrorCode ret = m_ChatController.m_Core.Shutdown();
                    if (Error.Failed(ret))
                    {
                        string err = Error.GetString(ret);
                        ReportError(string.Format("Error shutting down the Twitch sdk: {0}", err));
                    }

                    m_ChatController.SetChatState(ChatState.Uninitialized);
                }
                else
                {
            		// if shutdown fails the state will probably be messed up but this should never happen
                    m_ChatController.SetChatState(ChatState.Initialized);
        		
            		ReportError(String.Format("Error shutting down Twith chat: {0}", result));
                }

        	    try
        	    {
		            if (m_ChatController.ShutdownComplete != null)
		            {
		        	    m_ChatController.ShutdownComplete(result);
		            }
        	    }
        	    catch (Exception x)
        	    {
        		    m_ChatController.ReportError(x.ToString());
        	    }
            }

            void IChatAPIListener.ChatEmoticonDataDownloadCallback(ErrorCode error)
            {
                if (Error.Succeeded(error))
                {
                    m_ChatController.SetupEmoticonData();
                }
            }

            #endregion
        }

        protected class ChatChannelListener : ControllerAccess, IChatChannelListener
        {
            protected string m_ChannelName = null;
            protected bool m_Anonymous = false;
        	protected ChannelState m_ChannelState = ChannelState.Created;

            protected List<ChatUserInfo> m_ChannelUsers = new List<ChatUserInfo>();
            protected LinkedList<ChatRawMessage> m_RawMessages = new LinkedList<ChatRawMessage>();
            protected LinkedList<ChatTokenizedMessage> m_TokenizedMessages = new LinkedList<ChatTokenizedMessage>();

            protected ChatBadgeData m_BadgeData = null;

            public ChatChannelListener(ChatController controller, string channelName) : 
                base(controller)
            {
                m_ChannelName = channelName;
            }

            #region Properties

            public ChannelState ChannelState
            {
                get { return m_ChannelState; }
            }

            public bool IsConnected
            {
                get { return m_ChannelState == ChannelState.Connected; }
            }

            public bool IsAnonymous
            {
                get { return m_Anonymous; }
            }

            public LinkedList<ChatRawMessage>.Enumerator RawMessages
            {
                get { return m_RawMessages.GetEnumerator(); }
            }

            public LinkedList<ChatTokenizedMessage>.Enumerator TokenizedMessages
            {
                get { return m_TokenizedMessages.GetEnumerator(); }
            }

            public ChatBadgeData BadgeData
            {
                get { return m_BadgeData; }
            }

            #endregion

            public virtual bool Connect(bool anonymous)
            {
                m_Anonymous = anonymous;

                ErrorCode ret = ErrorCode.TTV_EC_SUCCESS;

                // connect to the channel
                if (anonymous)
                {
                    ret = Api.ConnectAnonymous(m_ChannelName, this);
                }
                else
                {
                    ret = Api.Connect(m_ChannelName, UserName, AuthToken, this);
                }

                if (Error.Failed(ret))
                {
                    String err = Error.GetString(ret);
                    ReportError(String.Format("Error connecting: {0}", err));

                    FireDisconnected(m_ChannelName);

                    return false;
                }
                else
                {
                    SetChannelState(ChannelState.Connecting);
                    DownloadBadgeData();

                    return true;
                }
            }

            public bool Disconnect()
            {
                switch (m_ChannelState)
                {
                    case ChannelState.Connected:
                    case ChannelState.Connecting:
                    {
                        // kick off an async disconnect
                        ErrorCode ret = Api.Disconnect(m_ChannelName);
                        if (Error.Failed(ret))
                        {
                            String err = Error.GetString(ret);
                            ReportError(String.Format("Error disconnecting: {0}", err));

                            return false;
                        }

                        SetChannelState(ChannelState.Disconnecting);
                        return true;
                    }
                    case ChannelState.Created:
                    case ChannelState.Disconnected:
                    case ChannelState.Disconnecting:
                    default:
                    {
                        return false;
                    }
                }
            }

            protected void SetChannelState(ChannelState state)
            {
                if (state == m_ChannelState)
                {
                    return;
                }

                m_ChannelState = state;

                try
                {
                    //if (ChannelStateChanged != null)
                    //{
                    //    this.ChannelStateChanged(state);
                    //}
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }

            public void ClearMessages()
            {
                ClearMessages(null);
            }

            public void ClearMessages(string username)
            {
                if (string.IsNullOrEmpty(username))
                {
                    m_RawMessages.Clear();
                    m_TokenizedMessages.Clear();
                }
                else
                {
                    if (m_RawMessages.Count > 0)
                    {
                        LinkedListNode<ChatRawMessage> node = m_RawMessages.First;
                        while (node != null)
                        {
                            if (node.Value.UserName == username)
                            {
                                LinkedListNode<ChatRawMessage> d = node;
                                node = node.Next;
                                m_RawMessages.Remove(d);
                            }
                            else
                            {
                                node = node.Next;
                            }
                        }
                    }

                    if (m_TokenizedMessages.Count > 0)
                    {
                        LinkedListNode<ChatTokenizedMessage> node = m_TokenizedMessages.First;
                        while (node != null)
                        {
                            if (node.Value.DisplayName == username)
                            {
                                LinkedListNode<ChatTokenizedMessage> d = node;
                                node = node.Next;
                                m_TokenizedMessages.Remove(d);
                            }
                            else
                            {
                                node = node.Next;
                            }
                        }
                    }
                }

                FireMessagesCleared(m_ChannelName, username);
            }

            public bool SendChatMessage(string message)
            {
                if (m_ChannelState != ChannelState.Connected)
                {
                    return false;
                }

                ErrorCode ret = Api.SendMessage(m_ChannelName, message);
                if (Error.Failed(ret))
                {
                    String err = Error.GetString(ret);
                    ReportError(String.Format("Error sending chat message: {0}", err));

                    return false;
                }

                return true;
            }

            #region Badge Handling

            internal virtual void DownloadBadgeData()
            {
                // don't download badges
                if (this.ActiveEmoticonMode == EmoticonMode.None)
                {
                    return;
                }

                if (m_BadgeData == null)
                {
                    ErrorCode ret = Api.DownloadBadgeData(m_ChannelName);
                    if (Error.Failed(ret))
                    {
                        string err = Error.GetString(ret);
                        ReportError(string.Format("Error trying to download badge data: {0}", err));
                    }
                }
            }

            internal virtual void SetupBadgeData()
            {
                if (m_BadgeData != null)
                {
                    return;
                }

                ErrorCode ec = Api.GetBadgeData(m_ChannelName, out m_BadgeData);

                if (Error.Succeeded(ec))
                {
                    FireBadgeDataAvailable(m_ChannelName);
                }
                else
                {
                    ReportError("Error preparing badge data: " + Error.GetString(ec));
                }
            }

            internal virtual void CleanupBadgeData()
            {
                if (m_BadgeData == null)
                {
                    return;
                }

                ErrorCode ec = Api.ClearBadgeData(m_ChannelName);

                if (Error.Succeeded(ec))
                {
                    m_BadgeData = null;

                    FireBadgeDataExpired(m_ChannelName);
                }
                else
                {
                    ReportError("Error releasing badge data: " + Error.GetString(ec));
                }
            }

            #endregion
            
            private void DisconnectionComplete()
            {
                if (m_ChannelState != ChannelState.Disconnected)
                {
                    SetChannelState(ChannelState.Disconnected);
                    FireDisconnected(m_ChannelName);
                    CleanupBadgeData();
                }
            }

            #region IChatChannelListener Implementation

            void IChatChannelListener.ChatStatusCallback(string channelName, ErrorCode result)
            {
                if (Error.Succeeded(result))
                {
                    return;
                }

                // destroy the channel object
                Channels.Remove(m_ChannelName);

                DisconnectionComplete();
            }

            void IChatChannelListener.ChatChannelMembershipCallback(string channelName, ChatEvent evt, ChatChannelInfo channelInfo)
            {
                switch (evt)
                {
                    case ChatEvent.TTV_CHAT_JOINED_CHANNEL:
                    {
                	    SetChannelState(ChannelState.Connected);
                        FireConnected(channelName);
                        break;
                    }
                    case ChatEvent.TTV_CHAT_LEFT_CHANNEL:
                    {
                	    DisconnectionComplete();
                        break;
                    }
                    default:
                    {
                        break;
                    }
                }
            }

            void IChatChannelListener.ChatChannelUserChangeCallback(string channelName, ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList)
            {
                for (int i = 0; i < leaveList.Length; ++i)
                {
                    int index = m_ChannelUsers.IndexOf(leaveList[i]);
                    if (index >= 0)
                    {
                        m_ChannelUsers.RemoveAt(index);
                    }
                }

                for (int i = 0; i < userInfoList.Length; ++i)
                {
                    // this will find the existing user with the same name
                    int index = m_ChannelUsers.IndexOf(userInfoList[i]);
                    if (index >= 0)
                    {
                        m_ChannelUsers.RemoveAt(index);
                    }

                    m_ChannelUsers.Add(userInfoList[i]);
                }

                for (int i = 0; i < joinList.Length; ++i)
                {
                    m_ChannelUsers.Add(joinList[i]);
                }

                FireUsersChanged(m_ChannelName, joinList, leaveList, userInfoList);
            }

            void IChatChannelListener.ChatChannelRawMessageCallback(string channelName, ChatRawMessage[] messageList)
            {
                for (int i = 0; i < messageList.Length; ++i)
                {
                    m_RawMessages.AddLast(messageList[i]);
                }

                FireRawMessagesReceived(m_ChannelName, messageList);

                // cap the number of messages cached
                while (m_RawMessages.Count > MessageHistorySize)
                {
                    m_RawMessages.RemoveFirst();
                }
            }

            void IChatChannelListener.ChatChannelTokenizedMessageCallback(string channelName, ChatTokenizedMessage[] messageList)
            {
                for (int i = 0; i < messageList.Length; ++i)
                {
                    m_TokenizedMessages.AddLast(messageList[i]);
                }

                FireTokenizedMessagesReceived(m_ChannelName, messageList);

                // cap the number of messages cached
                while (m_TokenizedMessages.Count > MessageHistorySize)
                {
                    m_TokenizedMessages.RemoveFirst();
                }
            }

            void IChatChannelListener.ChatClearCallback(string channelName, string username)
            {
                ClearMessages(username);
            }

            void IChatChannelListener.ChatBadgeDataDownloadCallback(string channelName, ErrorCode error)
            {
                // grab the texture and badge data
                if (Error.Succeeded(error))
                {
                    SetupBadgeData();
                }
            }

            #endregion
        }

        #region Properties

        /// <summary>
        /// Whether or not the controller has been initialized.
        /// </summary>
        public bool IsInitialized
        {
            get { return m_ChatState == ChatState.Initialized; }
        }

        /// <summary>
        /// The AuthToken obtained from using the BroadcastController or some other means.
        /// </summary>
        public AuthToken AuthToken
        {
            get { return m_AuthToken; }
            set { m_AuthToken = value; }
        }

        /// <summary>
        /// The Twitch client ID assigned to your application.
        /// </summary>
        public abstract string ClientId
        {
            get;
            set;
        }

        /// <summary>
        /// The secret code gotten from the Twitch site for the client id.
        /// </summary>
        public abstract string ClientSecret
        {
            get;
            set;
        }

        /// <summary>
        /// The emoticon parsing mode for chat messages.  This must be set before connecting to the channel to set the preference until disconnecting.  
        /// If a texture atlas is selected this will trigger a download of emoticon images to create the atlas.
        /// </summary>
        public abstract EmoticonMode EmoticonParsingMode
        {
            get;
            set;
        }

        /// <summary>
        /// The number of milliseconds between message events.
        /// </summary>
        public virtual int MessageFlushInterval
        {
            get { return 0; }
            set
            {
                if (m_ChatState == ChatState.Initialized)
                {
                    m_Chat.SetMessageFlushInterval(value);
                }
            }
        }

        /// <summary>
        /// The number of milliseconds between events for user joins, leaves and changes in channels.
        /// </summary>
        public virtual int UserChangeEventInterval
        {
            get { return 0; }
            set
            {
                if (m_ChatState == ChatState.Initialized)
                {
                    m_Chat.SetUserChangeEventInterval(value);
                }
            }
        }

        /// <summary>
        /// The username to log in with.
        /// </summary>
        public string UserName
        {
            get { return m_UserName; }
            set { m_UserName = value; }
        }

        /// <summary>
        /// The maximum number of messages to be kept in the chat history.
        /// </summary>
        public uint MessageHistorySize
        {
            get { return m_MessageHistorySize; }
            set { m_MessageHistorySize = value; }
        }

        /// <summary>
        /// The current state of the ChatController.
        /// </summary>
        public ChatState CurrentState
        {
            get { return m_ChatState; }
        }

        /// <summary>
        /// Retrieves the emoticon data that can be used to render icons.
        /// </summary>
        public ChatEmoticonData EmoticonData
        {
            get { return m_EmoticonData; }
        }

        /// <summary>
        /// Whether or not currently connected to the channel.
        /// </summary>
        public bool IsConnected(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                return false;
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.IsConnected;
        }

        /// <summary>
        /// Whether or not currently connected to the channel.
        /// </summary>
        public bool IsAnonymous(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                return false;
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.IsAnonymous;
        }
        
        /// <summary>
        /// An iterator for the raw chat messages from oldest to newest.
        /// </summary>
        public LinkedList<ChatRawMessage>.Enumerator GetRawMessages(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Unknown channel: " + channelName);
                return new LinkedList<ChatRawMessage>().GetEnumerator();
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.RawMessages;
        }

        /// <summary>
        /// An iterator for the tokenized chat messages from oldest to newest.
        /// </summary>
        public LinkedList<ChatTokenizedMessage>.Enumerator GetTokenizedMessages(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Unknown channel: " + channelName);
                return new LinkedList<ChatTokenizedMessage>().GetEnumerator();
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.TokenizedMessages;
        }

        /// <summary>
        /// Retrieves the badge data that can be used to render icons.
        /// </summary>
        public ChatBadgeData GetBadgeData(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Unknown channel: " + channelName);
                return null;
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.BadgeData;
        }

        #endregion

        protected ChatController()
        {
            m_ChatAPIListener = new ChatApiListener(this);
        }

        public virtual bool Initialize()
        {
            if (m_ChatState != ChatState.Uninitialized)
            {
                return false;
            }

            SetChatState(ChatState.Initializing);

            ErrorCode ret = m_Core.Initialize(ClientId, null);
            if (Error.Failed(ret))
            {
                SetChatState(ChatState.Uninitialized);

                String err = Error.GetString(ret);
                ReportError(String.Format("Error initializing Twitch sdk: {0}", err));

                return false;
            }

            // initialize chat
            m_ActiveEmoticonMode = this.EmoticonParsingMode;

            TTV_ChatTokenizationOption tokenizationOptions = TTV_ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_NONE;
            switch (m_ActiveEmoticonMode)
            {
                case EmoticonMode.None:
                    tokenizationOptions = TTV_ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_NONE;
                    break;
                case EmoticonMode.Url:
                    tokenizationOptions = TTV_ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_EMOTICON_URLS;
                    break;
                case EmoticonMode.TextureAtlas:
                    tokenizationOptions = TTV_ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_EMOTICON_TEXTURES;
                    break;
            }

            // kick off the async init
            ret = m_Chat.Initialize(tokenizationOptions, m_ChatAPIListener);
            if (Error.Failed(ret))
            {
                m_Core.Shutdown();
                SetChatState(ChatState.Uninitialized);

                String err = Error.GetString(ret);
                ReportError(String.Format("Error initializing Twitch chat: %s", err));

                return false;
            }
            else
            {
                SetChatState(ChatState.Initialized);
                return true;
            }
        }

        /// <summary>
        /// Connects to the given channel.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
        /// </summary>
        /// <param name="channelName">The name of the channel.</param>
        /// <returns>Whether or not the request was successful.</returns>
        public virtual bool Connect(string channelName)
        {
            return Connect(channelName, false);
        }

        /// <summary>
        /// Connects to the given channel anonymously.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
        /// </summary>
        /// <param name="channelName">The name of the channel.</param>
        /// <returns>Whether or not the request was valid.</returns>
        public virtual bool ConnectAnonymous(string channelName)
        {
            return Connect(channelName, true);
        }

        protected virtual bool Connect(string channelName, bool anonymous)
        {
            if (m_ChatState != ChatState.Initialized)
            {
                return false;
            }

            if (m_Channels.ContainsKey(channelName))
            {
                ReportError("Already in channel: " + channelName);
                return false;
            }

            if (string.IsNullOrEmpty(channelName))
            {
                return false;
            }

            ChatChannelListener channel = new ChatChannelListener(this, channelName);
            m_Channels[channelName] = channel;

            bool result = channel.Connect(anonymous);

            if (!result)
            {
                m_Channels.Remove(channelName);
            }

            return result;
        }

        /// <summary>
        /// Disconnects from the channel.  The result of the attempt will be returned in a Disconnected event.
        /// </summary>
        /// <returns>Whether or not the disconnect attempt was valid.</returns>
        public virtual bool Disconnect(string channelName)
        {
            if (m_ChatState != ChatState.Initialized)
            {
                return false;
            }

            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Not in channel: " + channelName);
                return false;
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.Disconnect();
        }

        public virtual bool Shutdown()
        {
            if (m_ChatState != ChatState.Initialized)
            {
                return false;
            }

            // shutdown asynchronously
            ErrorCode ret = m_Chat.Shutdown();
            if (Error.Failed(ret))
            {
                String err = Error.GetString(ret);
                ReportError(String.Format("Error shutting down chat: {0}", err));

                return false;
            }

            CleanupEmoticonData();

            SetChatState(ChatState.ShuttingDown);

            return true;
        }

        /// <summary>
        /// Ensures the controller is fully shutdown before returning.  This may fire callbacks to listeners during the shutdown.
        /// </summary>
        public void ForceSyncShutdown()
        {
            // force a low-level shutdown
            if (this.CurrentState != Twitch.Chat.ChatController.ChatState.Uninitialized)
            {
                this.Shutdown();

                // wait for the shutdown to finish
                if (this.CurrentState == Twitch.Chat.ChatController.ChatState.ShuttingDown)
                {
                    while (this.CurrentState != Twitch.Chat.ChatController.ChatState.Uninitialized)
                    {
                        try
                        {
                            System.Threading.Thread.Sleep(200);
                            this.Update();
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Periodically updates the internal state of the controller.
        /// </summary>
        public virtual void Update()
        {
            // for stress testing to make sure memory is being passed around properly
            //GC.Collect(); 

            if (m_ChatState == ChatState.Uninitialized)
            {
                return;
            }

	        ErrorCode ret = m_Chat.FlushEvents();
            if (Error.Failed(ret))
            {
                string err = Error.GetString(ret);
                ReportError(string.Format("Error flushing chat events: {0}", err));
            }
        }

        /// <summary>
        /// Sends a chat message to the channel.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>Whether or not the attempt was valid.</returns>
        public virtual bool SendChatMessage(string channelName, string message)
        {
            if (m_ChatState != ChatState.Initialized)
            {
                return false;
            }

            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Not in channel: " + channelName);
                return false;
            }

            ChatChannelListener channel = m_Channels[channelName];
            return channel.SendChatMessage(message);
        }

        /// <summary>
        /// Clears the chat message history for all users.
        /// </summary>
        public virtual void ClearMessages(string channelName)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Not in channel: " + channelName);
                return;
            }

            ChatChannelListener channel = m_Channels[channelName];
            channel.ClearMessages();
        }

        /// <summary>
        /// Clears the chat message history for the given user only.
        /// </summary>
        public virtual void ClearMessages(string channelName, string username)
        {
            if (!m_Channels.ContainsKey(channelName))
            {
                ReportError("Not in channel: " + channelName);
                return;
            }

            ChatChannelListener channel = m_Channels[channelName];
            channel.ClearMessages(username);
        }

        internal virtual void SetChatState(ChatState state)
        {
            if (state == m_ChatState)
            {
                return;
            }

            m_ChatState = state;

            try
            {
                if (ChatStateChanged != null)
                {
                    this.ChatStateChanged(state);
                }
            }
            catch (Exception x)
            {
                ReportError(x.ToString());
            }
        }

        #region Emoticon Handling

        internal virtual void DownloadEmoticonData()
        {
            // don't download emoticons
            if (m_ActiveEmoticonMode == EmoticonMode.None)
            {
                return;
            }

            if (m_EmoticonData == null)
            {
                ErrorCode ret = m_Chat.DownloadEmoticonData();
                if (Error.Failed(ret))
                {
                    string err = Error.GetString(ret);
                    ReportError(string.Format("Error trying to download emoticon data: {0}", err));
                }
            }
        }

        internal virtual void SetupEmoticonData()
        {
            if (m_EmoticonData != null)
            {
                return;
            }

            ErrorCode ec = m_Chat.GetEmoticonData(out m_EmoticonData);
            if (Error.Succeeded(ec))
            {
                try
                {
                    if (EmoticonDataAvailable != null)
                    {
                        EmoticonDataAvailable();
                    }
                }
                catch (Exception x)
                {
                    ReportError(x.ToString());
                }
            }
            else
            {
                ReportError("Error preparing emoticon data: " + Error.GetString(ec));
            }
        }

        internal virtual void CleanupEmoticonData()
        {
            if (m_EmoticonData == null)
            {
                return;
            }

            m_EmoticonData = null;

            try
            {
                if (EmoticonDataExpired != null)
                {
                    EmoticonDataExpired();
                }
            }
            catch (Exception x)
            {
                ReportError(x.ToString());
            }
        }

        #endregion

        #region Error Handling

        protected virtual void CheckError(ErrorCode err)
        {
        }

        protected virtual void ReportError(string err)
        {
        }

        protected virtual void ReportWarning(string err)
        {
        }

        #endregion
    }
}
