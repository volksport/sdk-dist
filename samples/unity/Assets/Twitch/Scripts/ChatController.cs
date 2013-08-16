using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ErrorCode = Twitch.ErrorCode;
using AuthToken = Twitch.Broadcast.AuthToken;


namespace Twitch.Chat
{
    /// <summary>
    /// The state machine which manages the chat state.  It provides a high level interface to the SDK libraries.  The ChatController (CC) performs many operations
    /// asynchronously and hides the details.  This can be tweaked if needed but should handle all your chat needs (other than emoticons which may be provided in the future).
    /// 
    /// The typical order of operations a client of CC will take is:
    /// 
    /// - Subscribe for events via delegates on ChatController
    /// - Initialize BroadcastController via BC.InitializeTwitch()
    /// - Call CC.Connect() / call CC.ConnectAnonymous()
    /// - Wait for the connection callback 
    /// - Call CC.SendChatMessage() to send messages (if not connected anonymously)
    /// - Receive message callbacks
    /// - Call CC.Disconnect() when done
    /// 
    /// Events will fired during the call to CC.Update().  When chat messages are received RawMessagesReceived will be fired.
    /// 
    /// NOTE: The implementation of emoticon data is not yet complete and currently not available.
    /// </summary>
    public abstract partial class ChatController : IChatCallbacks
    {
        #region Types

        /// <summary>
        /// The possible states the ChatController can be in.
        /// </summary>
        public enum ChatState
        {
            Uninitialized,  //!< Chat is not yet initialized.
            Initialized,    //!< The component is initialized.
            Connecting,     //!< Currently attempting to connect to the channel.
            Connected,      //!< Connected to the channel.
            Disconnected    //!< Initialized but not connected.
        }

        /// <summary>
        /// The callback signature for the event fired when a tokenized set of messages has been received.
        /// </summary>
        /// <param name="messages">The list of messages</param>
        public delegate void TokenizedMessagesReceivedDelegate(ChatTokenizedMessage[] messages);

        /// <summary>
        /// The callback signature for the event fired when a set of text-only messages has been received.
        /// </summary>
        /// <param name="messages">The list of messages</param>
        public delegate void RawMessagesReceivedDelegate(ChatMessage[] messages);

        /// <summary>
        /// The callback signature for the event fired when users join, leave or changes their status in the channel.
        /// </summary>
        /// <param name="joinList">The list of users who have joined the room.</param>
        /// <param name="leaveList">The list of useres who have left the room.</param>
        /// <param name="userInfoList">The list of users who have changed their status.</param>
        public delegate void UsersChangedDelegate(ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList);

        /// <summary>
        /// The callback signature for the event fired when the local user has been connected to the channel.
        /// </summary>
        public delegate void ConnectedDelegate();

        /// <summary>
        /// The callback signature for the event fired when the local user has been disconnected from the channel.
        /// </summary>
        public delegate void DisconnectedDelegate();

        /// <summary>
        /// The callback signature for the event fired when the messages in the room should be cleared.  The UI should be cleared of any previous messages.
        /// </summary>
        public delegate void ClearMessagesDelegate();

        /// <summary>
        /// The callback signature for the event fired when the emoticon data has been made available.
        /// </summary>
        public delegate void EmoticonDataAvailableDelegate();

        /// <summary>
        /// The callback signature for the event fired when the emoticon data is no longer valid.
        /// </summary>
        public delegate void EmoticonDataExpiredDelegate();

        #endregion

        #region Memeber Variables
        
        public event TokenizedMessagesReceivedDelegate TokenizedMessagesReceived;
        public event RawMessagesReceivedDelegate RawMessagesReceived;
        public event UsersChangedDelegate UsersChanged;
        public event ConnectedDelegate Connected;
        public event DisconnectedDelegate Disconnected;
        public event ClearMessagesDelegate MessagesCleared;
        public event EmoticonDataAvailableDelegate EmoticonDataAvailable;
        public event EmoticonDataExpiredDelegate EmoticonDataExpired;

        protected Twitch.Chat.Chat m_Chat = null;

        protected string m_UserName = "";
        protected string m_ChannelName = "";

        protected bool m_ChatInitialized = false;
        protected bool m_Anonymous = false;
        protected ChatState m_ChatState = ChatState.Uninitialized;
        protected AuthToken m_AuthToken = new AuthToken();

        protected List<ChatUserInfo> m_ChannelUsers = new List<ChatUserInfo>();
        protected LinkedList<ChatMessage> m_Messages = new LinkedList<ChatMessage>();
        protected uint m_MessageHistorySize = 128;

        protected bool m_UseEmoticons = false;
        protected bool m_EmoticonDataDownloaded = false;

        #endregion


        #region IChatCallbacks

        void IChatCallbacks.ChatStatusCallback(ErrorCode result)
        {
            if (Error.Succeeded(result))
            {
                return;
            }

            m_ChatState = ChatState.Disconnected;
        }

        void IChatCallbacks.ChatChannelMembershipCallback(TTV_ChatEvent evt, ChatChannelInfo channelInfo)
        {
            switch (evt)
            {
                case TTV_ChatEvent.TTV_CHAT_JOINED_CHANNEL:
                {
                    m_ChatState = ChatState.Connected;
                    FireConnected();
                    break;
                }
                case TTV_ChatEvent.TTV_CHAT_LEFT_CHANNEL:
                {
                    m_ChatState = ChatState.Disconnected;
                    break;
                }
                default:
                {
                    break;
                }
            }
        }

        void IChatCallbacks.ChatChannelUserChangeCallback(ChatUserList joinList, ChatUserList leaveList, ChatUserList userInfoList)
        {
            for (int i=0; i<leaveList.List.Length; ++i)
            {
                int index = m_ChannelUsers.IndexOf(leaveList.List[i]);
                if (index >= 0)
                {
                    m_ChannelUsers.RemoveAt(index);
                }
            }

            for (int i=0; i<userInfoList.List.Length; ++i)
            {
                // this will find the existing user with the same name
                int index = m_ChannelUsers.IndexOf(userInfoList.List[i]);
                if (index >= 0)
                {
                    m_ChannelUsers.RemoveAt(index);
                }

                m_ChannelUsers.Add(userInfoList.List[i]);
            }

            for (int i=0; i<joinList.List.Length; ++i)
            {
                m_ChannelUsers.Add(joinList.List[i]);
            }

            try
            {
                if (UsersChanged != null)
                {
                    this.UsersChanged(joinList.List, leaveList.List, userInfoList.List);
                }
            }
            catch
            {
            }
        }

        void IChatCallbacks.ChatQueryChannelUsersCallback(ChatUserList userList)
        {
            // listening for incremental changes so no need for full query
        }

        void IChatCallbacks.ChatChannelMessageCallback(ChatMessageList messageList)
        {
            for (int i = 0; i < messageList.Messages.Length; ++i)
            {
                m_Messages.AddLast(messageList.Messages[i]);
            }

            try
            {
                if (m_UseEmoticons)
                {
                    if (TokenizedMessagesReceived != null)
                    {
                        List<ChatTokenizedMessage> list = new List<ChatTokenizedMessage>();
                        for (int i = 0; i < messageList.Messages.Length; ++i)
                        {
                            ChatTokenizedMessage tokenized = null;
                            ErrorCode ret = m_Chat.TokenizeMessage(messageList.Messages[i], out tokenized);
                            if (Error.Failed(ret) || tokenized == null)
                            {
                                string err = Error.GetString(ret);
                                ReportError(string.Format("Error disconnecting: {0}", err));
                            }
                            else
                            {
                                list.Add(tokenized);
                            }
                        }

                        ChatTokenizedMessage[] arr = list.ToArray();

                        this.TokenizedMessagesReceived(arr);
                    }
                }
                else
                {
                    if (RawMessagesReceived != null)
                    {
                        this.RawMessagesReceived(messageList.Messages);
                    }
                }
            }
            catch
            {
            }

            // cap the number of messages cached
            while (m_Messages.Count > m_MessageHistorySize)
            {
                m_Messages.RemoveFirst();
            }
        }

        void IChatCallbacks.ChatClearCallback(string channelName)
        {
	        ClearMessages();
        }

        void IChatCallbacks.EmoticonDataDownloadCallback(ErrorCode error)
        {
            // grab the texture and badge data
            if (Error.Succeeded(error))
            {
                SetupEmoticonData();
            }
        }

        #endregion


        #region Properties

        /// <summary>
        /// Whether or not the controller has been initialized.
        /// </summary>
        public bool IsInitialized
        {
            get { return m_ChatInitialized; }
        }
        
        /// <summary>
        /// Whether or not currently connected to the channel.
        /// </summary>
        public bool IsConnected
        {
            get { return m_ChatState == ChatState.Connected; }
        }

        /// <summary>
        /// Whether or not connected anonymously (listen only).
        /// </summary>
        public bool IsAnonymous
        {
            get { return m_Anonymous; }
        }

        /// <summary>
        /// The AuthToken obtained from using the BroadcastController.
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
        /// An iterator for the chat messages from oldest to newest.
        /// </summary>
        public LinkedList<ChatMessage>.Enumerator Messages
        {
            get { return m_Messages.GetEnumerator(); }
        }

        //public bool UseEmoticons
        //{
        //    get { return m_UseEmoticons; }
        //    set 
        //    { 
        //        m_UseEmoticons = value;
        //        DownloadEmoticonData();
        //    }
        //}

        #endregion

        /// <summary>
        /// Connects to the given channel.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
        /// </summary>
        /// <param name="channel">The name of the channel.</param>
        /// <returns>Whether or not the request was successful.</returns>
        public virtual bool Connect(string channel)
        {
            Disconnect();

            m_Anonymous = false;
            m_ChannelName = channel;

            return Initialize(channel);
        }

        /// <summary>
        /// Connects to the given channel anonymously.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
        /// </summary>
        /// <param name="channel">The name of the channel.</param>
        /// <returns>Whether or not the request was valid.</returns>
        public virtual bool ConnectAnonymous(string channel)
        {
            Disconnect();

            m_Anonymous = true;
            m_ChannelName = channel;
        
            return Initialize(channel);
        }

        /// <summary>
        /// Disconnects from the channel.  The result of the attempt will be returned in a Disconnected event.
        /// </summary>
        /// <returns>Whether or not the disconnect attempt was valid.</returns>
        public virtual bool Disconnect()
        {
            if (m_ChatState == ChatState.Connected || 
                m_ChatState == ChatState.Connecting)
            {
                ErrorCode ret = m_Chat.Disconnect();
                if (Error.Failed(ret))
                {
                    string err = Error.GetString(ret);
                    ReportError(string.Format("Error disconnecting: {0}", err));
                }

                FireDisconnected();
            }
            else if (m_ChatState == ChatState.Disconnected)
            {
                FireDisconnected();
            }
            else
            {
                return false;
            }

            return Shutdown();
        }

        protected virtual bool Initialize(string channel)
        {
            if (m_ChatInitialized)
            {
                return false;
            }

            ErrorCode ret = m_Chat.Initialize(channel);
            if (Error.Failed(ret))
            {
                string err = Error.GetString(ret);
                ReportError(string.Format("Error initializing chat: {0}", err));

                FireDisconnected();

                return false;
            }
            else
            {
                m_Chat.ChatCallbacks = this;
                m_ChatInitialized = true;
                m_ChatState = ChatState.Initialized;

                return true;
            }
        }

        protected virtual bool Shutdown()
        {
            if (m_ChatInitialized)
            {
                ErrorCode ret = m_Chat.Shutdown();
                if (Error.Failed(ret))
                {
                    string err = Error.GetString(ret);
                    ReportError(string.Format("Error shutting down chat: {0}", err));

                    return false;
                }
            }

            m_ChatState = ChatState.Uninitialized;
            m_ChatInitialized = false;

            CleanupEmoticonData();

            m_Chat.ChatCallbacks = null;

            return true;
        }

        /// <summary>
        /// Periodically updates the internal state of the controller.
        /// </summary>
        public virtual void Update()
        {
            // for stress testing to make sure memory is being passed around properly
            //GC.Collect(); 
        
            if (!m_ChatInitialized)
            {
                return;
            }

	        ErrorCode ret = m_Chat.FlushEvents();
            if (Error.Failed(ret))
            {
                string err = Error.GetString(ret);
                ReportError(string.Format("Error flushing chat events: {0}", err));
            }

	        switch (m_ChatState)
	        {
		        case ChatState.Uninitialized:
		        {
			        break;
		        }
		        case ChatState.Initialized:
		        {
                    // connect to the channel
                    if (m_Anonymous)
                    {
                        ret = m_Chat.ConnectAnonymous();
                    }
                    else
                    {
                        ret = m_Chat.Connect(m_ChannelName, m_AuthToken.Data);
                    }

                    if (Error.Failed(ret))
                    {
                        string err = Error.GetString(ret);
                        ReportError(string.Format("Error connecting: {0}", err));

                        Shutdown();

                        FireDisconnected();
                    }
                    else
                    {
                        m_ChatState = ChatState.Connecting;
                        DownloadEmoticonData();
                    }

			        break;
		        }
                case ChatState.Connecting:
                {
                    break;
                }
                case ChatState.Connected:
                {
                    break;
                }
                case ChatState.Disconnected:
                {
                    Disconnect();
                    break;
                }
	        }
        }

        /// <summary>
        /// Sends a chat message to the channel.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <returns>Whether or not the attempt was valid.</returns>
        public virtual bool SendChatMessage(string message)
        {
            if (m_ChatState != ChatState.Connected)
            {
                return false;
            }

            ErrorCode ret = m_Chat.SendMessage(message);
            if (Error.Failed(ret))
            {
                string err = Error.GetString(ret);
                ReportError(string.Format("Error sending chat message: {0}", err));

                return false;
            }

            return true;
        }

        /// <summary>
        /// Clears the chat message history.
        /// </summary>
        public virtual void ClearMessages()
        {
            m_Messages.Clear();

            try
            {
                if (MessagesCleared != null)
                {
                    this.MessagesCleared();
                }
            }
            catch (Exception x)
            {
                ReportError(string.Format("Error clearing chat messages: {0}", x.ToString()));
            }
        }

        #region Event Helpers

        protected void FireConnected()
        {
            try
            {
                if (Connected != null)
                {
                    this.Connected();
                }
            }
            catch (Exception x)
            {
                ReportError(x.ToString());
            }
        }

        protected void FireDisconnected()
        {
            try
            {
                if (Disconnected != null)
                {
                    this.Disconnected();
                }
            }
            catch (Exception x)
            {
                ReportError(x.ToString());
            }
        }

        #endregion

        #region Emoticon Handling

        protected virtual void DownloadEmoticonData()
        {
            if (m_UseEmoticons &&
                !m_EmoticonDataDownloaded &&
                m_ChatInitialized)
            {
                ErrorCode ret = m_Chat.DownloadEmoticonData();
                if (Error.Failed(ret))
                {
                    string err = Error.GetString(ret);
                    ReportError(string.Format("Error trying to download emoticon data: {0}", err));
                }
            }
        }

        protected virtual void SetupEmoticonData()
        {
            m_EmoticonDataDownloaded = true;

            if (EmoticonDataAvailable != null)
            {
                EmoticonDataAvailable();
            }
        }

        protected virtual void CleanupEmoticonData()
        {
            m_EmoticonDataDownloaded = false;

            if (EmoticonDataExpired != null)
            {
                EmoticonDataExpired();
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
