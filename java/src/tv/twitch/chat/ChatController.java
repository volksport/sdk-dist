package tv.twitch.chat;

import java.util.*;

import tv.twitch.*;


/**
 * The state machine which manages the chat state.  It provides a high level interface to the SDK libraries.  The ChatController (CC) performs many operations
 * asynchronously and hides the details.  This can be tweaked if needed but should handle all your chat needs (other than emoticons which may be provided in the future).
 * 
 * The typical order of operations a client of CC will take is:
 * 
 * - Set the listener you want to receive events on
 *     CC.setListener(chatListener)
 * - Set relevant properties on the CC
 *     CC.setClientId(clientId)
 *     CC.setClientSecret(clientSecret)
 *     CC.setUserName(username)
 *     CC.setAuthToken(authToken)
 *     CC.setEmoticonParsingModeMode(emoticonParsingMode)
 * - Call CC.initialize() and wait for theasync  initialization callback to be called
 * - Call CC.connect() / call CC.connectAnonymous() for each channel you're interested in
 * - Wait for the connection callbacks
 * - Call CC.sendChatMessage() to send messages (if not connected anonymously)
 * - Receive message callbacks
 * - Call CC.disconnect() on channels when done with them
 * - Call CC.shutdown() and wait for the async shutdown callback to be called
 * 
 * Events will fired during the call to CC.Update().  When chat messages are received RawMessagesReceived will be fired.
 * 
 * NOTE: The implementation of texture emoticon data is not yet complete and currently not available.
 */
public class ChatController
{
    protected static final int MIN_INTERVAL_MS = 200;
    protected static final int MAX_INTERVAL_MS = 10000;

    //#region Types

    /**
     * The possible states the ChatController can be in.
     */
    public enum ChatState
    {
        Uninitialized,  //!< The component is not yet initialized.
        Initializing,   //!< The component is initializing.
        Initialized,    //!< The component is initialized.
        ShuttingDown, 	//!< The component is shutting down.
    }

    /**
     * The possible states a chat channel can be in.
     */
    protected enum ChannelState
    {
        Created,
        Connecting,
        Connected,
        Disconnecting,
        Disconnected,
    }

    /**
     * The emoticon parsing mode for chat messages.
     */
    public enum EmoticonMode
    {
        None,			//!< Do not parse out emoticons in messages.
        Url, 			//!< Parse out emoticons and return urls only for images.
        TextureAtlas 	//!< Parse out emoticons and return texture atlas coordinates.
    }

    /**
     * The listener interface for events from the ChatController. 
     */
    public interface Listener
    {
        /**
         * The callback for the event fired when initialization is complete.
         */
        void onInitializationComplete(ErrorCode result);

        /**
         * The callback for the event fired when shutdown is complete.
         */
        void onShutdownComplete(ErrorCode result);

        /**
         * The callback signature for the event fired when the emoticon data has been made available.
         */
        void onEmoticonDataAvailable();

        /**
         * The callback signature for the event fired when the emoticon data is no longer valid.
         */
        void onEmoticonDataExpired();

        /**
         * The callback signature for the event which is fired when the ChatController changes state.
         * @param state
         */
        void onChatStateChanged(ChatState state);

        /**
         * The callback signature for the event fired when a tokenized set of messages has been received.
         * @param messages
         */
        void onTokenizedMessagesReceived(String channelName, ChatTokenizedMessage[] messages);

        /**
         * The callback signature for the event fired when a set of text-only messages has been received.
         * @param messages
         */
        void onRawMessagesReceived(String channelName, ChatRawMessage[] messages);

        /**
         * The callback signature for the event fired when users join, leave or changes their status in the channel.
         * @param joinList The list of users who have joined the room.
         * @param leaveList The list of useres who have left the room.
         * @param userInfoList The list of users who have changed their status.
         */
        void onUsersChanged(String channelName, ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList);

        /**
         * The callback signature for the event fired when the local user has been connected to the channel.
         */
        void onConnected(String channelName);

        /**
         * The callback signature for the event fired when the local user has been disconnected from the channel.
         */
        void onDisconnected(String channelName);

        /**
         * The callback signature for the event fired when the messages in the room should be cleared.  The UI should be cleared of any previous messages.
         * If username is null or empty then the entire log was cleared, otherwise only messages for the given user were cleared.
         */
        void onMessagesCleared(String channelName, String username);

        /**
         * The callback signature for the event fired when the badge data has been made available.
         */
        void onBadgeDataAvailable(String channelName);

        /**
         * The callback signature for the event fired when the badge data is no longer valid.
         */
        void onBadgeDataExpired(String channelName);
    }

    //#endregion

    //#region Memeber Variables

    protected Listener m_Listener = null;

    protected String m_UserName = "";
    protected String m_ClientId = "";
    protected String m_ClientSecret = "";
    protected Core m_Core = null;
    protected Chat m_Chat = null;

    protected ChatState m_ChatState = ChatState.Uninitialized;
    protected AuthToken m_AuthToken = new AuthToken();

    protected HashMap<String, ChatChannelListener> m_Channels = new HashMap<String, ChatChannelListener>();

    protected int m_MessageHistorySize = 128;
    protected EmoticonMode m_EmoticonMode = EmoticonMode.None;
    protected EmoticonMode m_ActiveEmoticonMode = EmoticonMode.None; 
    protected ChatEmoticonData m_EmoticonData = null;

    protected int m_MessageFlushInterval = 500;
    protected int m_UserChangeEventInterval = 2000;

    //#endregion

    protected IChatAPIListener m_ChatAPIListener = new IChatAPIListener()
    {
        @Override
        public void chatInitializationCallback(ErrorCode result)
        {
            if (ErrorCode.succeeded(result))
            {
                m_Chat.setMessageFlushInterval(m_MessageFlushInterval);
                m_Chat.setUserChangeEventInterval(m_UserChangeEventInterval);

                downloadEmoticonData();

                setChatState(ChatState.Initialized);
            }
            else
            {
                setChatState(ChatState.Uninitialized);
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onInitializationComplete(result);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        @Override
        public void chatShutdownCallback(ErrorCode result)
        {
            if (ErrorCode.succeeded(result))
            {
                ErrorCode ret = m_Core.shutdown();
                if (ErrorCode.failed(ret))
                {
                    String err = ErrorCode.getString(ret);
                    reportError(String.format("Error shutting down the Twitch sdk: %s", err));
                }

                setChatState(ChatState.Uninitialized);
            }
            else
            {
                // if shutdown fails the state will probably be messed up but this should never happen
                setChatState(ChatState.Initialized);

                reportError(String.format("Error shutting down Twith chat: %s", result));
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onShutdownComplete(result);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }        	
        }

        @Override
        public void chatEmoticonDataDownloadCallback(ErrorCode result)
        {
            if (ErrorCode.succeeded(result))
            {
                setupEmoticonData();
            }
        }
    };

    protected class ChatChannelListener implements IChatChannelListener
    {
        protected String m_ChannelName = null;
        protected boolean m_Anonymous = false;
        protected ChannelState m_ChannelState = ChannelState.Created;

        protected List<ChatUserInfo> m_ChannelUsers = new ArrayList<ChatUserInfo>();
        protected LinkedList<ChatRawMessage> m_RawMessages = new LinkedList<ChatRawMessage>();
        protected LinkedList<ChatTokenizedMessage> m_TokenizedMessages = new LinkedList<ChatTokenizedMessage>();

        protected ChatBadgeData m_BadgeData = null;

        public ChatChannelListener(String channelName)
        {
            m_ChannelName = channelName;
        }

        //#region Properties

        public ChannelState getChannelState()
        {
            return m_ChannelState;
        }

        public boolean getIsAnonymous()
        {
            return m_Anonymous;
        }

        public String getChannelName()
        {
            return m_ChannelName;
        }

        public Iterator<ChatRawMessage> getRawMessages()
        {
            return m_RawMessages.iterator();
        }

        public Iterator<ChatTokenizedMessage> getTokenizedMessages()
        {
            return m_TokenizedMessages.iterator();
        }

        public ChatBadgeData getBadgeData()
        {
            return m_BadgeData;
        }

        //#endregion

        public boolean connect(boolean anonymous)
        {
            m_Anonymous = anonymous;

            ErrorCode ret = ErrorCode.TTV_EC_SUCCESS;

            // connect to the channel
            if (anonymous)
            {
                ret = m_Chat.connectAnonymous(m_ChannelName, this);
            }
            else
            {
                ret = m_Chat.connect(m_ChannelName, m_UserName, m_AuthToken.data, this);
            }

            if (ErrorCode.failed(ret))
            {
                String err = ErrorCode.getString(ret);
                reportError(String.format("Error connecting: %s", err));

                fireDisconnected(m_ChannelName);

                return false;
            }
            else
            {
                setChannelState(ChannelState.Connecting);
                downloadBadgeData();

                return true;
            }
        }

        public boolean disconnect()
        {
            switch (m_ChannelState)
            {
            case Connected:
            case Connecting:
            {
                // kick off an async disconnect
                ErrorCode ret = m_Chat.disconnect(m_ChannelName);
                if (ErrorCode.failed(ret))
                {
                    String err = ErrorCode.getString(ret);
                    reportError(String.format("Error disconnecting: %s", err));

                    return false;
                }

                setChannelState(ChannelState.Disconnecting);
                return true;
            }
            case Created:
            case Disconnected:
            case Disconnecting:
            default:
            {
                return false;
            }
            }
        }

        protected void setChannelState(ChannelState state)
        {
            if (state == m_ChannelState)
            {
                return;
            }

            m_ChannelState = state;

            try
            {
                //                if (m_Listener != null)
                //                {
                //                	m_Listener.onChannelStateChanged(m_ChannelName, state);
                //                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        public void clearMessages(String username)
        {
            if (m_ActiveEmoticonMode == EmoticonMode.None)
            {
                m_RawMessages.clear();
                m_TokenizedMessages.clear();
            }
            else
            {
                if (m_RawMessages.size() > 0)
                {
                    ListIterator<ChatRawMessage> iter = m_RawMessages.listIterator();
                    while (iter.hasNext())
                    {
                        ChatRawMessage msg = iter.next();
                        if (msg.userName.equals(username))
                        {
                            iter.remove();
                        }
                    }
                }

                if (m_TokenizedMessages.size() > 0)
                {
                    ListIterator<ChatTokenizedMessage> iter = m_TokenizedMessages.listIterator();
                    while (iter.hasNext())
                    {
                        ChatTokenizedMessage msg = iter.next();
                        if (msg.displayName.equals(username))
                        {
                            iter.remove();
                        }
                    }
                }
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onMessagesCleared(m_ChannelName, username);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        public boolean sendChatMessage(String message)
        {
            if (m_ChannelState != ChannelState.Connected)
            {
                return false;
            }

            ErrorCode ret = m_Chat.sendMessage(m_ChannelName, message);
            if (ErrorCode.failed(ret))
            {
                String err = ErrorCode.getString(ret);
                reportError(String.format("Error sending chat message: %s", err));

                return false;
            }

            return true;
        }

        //#region Badge Handling

        protected void downloadBadgeData()
        {
            // don't download badges
            if (m_ActiveEmoticonMode == EmoticonMode.None)
            {
                return;
            }

            if (m_BadgeData == null)
            {
                ErrorCode ret = m_Chat.downloadBadgeData(m_ChannelName);
                if (ErrorCode.failed(ret))
                {
                    String err = ErrorCode.getString(ret);
                    reportError(String.format("Error trying to download badge data: %s", err));
                }
            }
        }

        protected void setupBadgeData()
        {
            if (m_BadgeData != null)
            {
                return;
            }

            m_BadgeData = new ChatBadgeData();
            ErrorCode ec = m_Chat.getBadgeData(m_ChannelName, m_BadgeData);

            if (ErrorCode.succeeded(ec))
            {
                try
                {
                    if (m_Listener != null)
                    {
                        m_Listener.onBadgeDataAvailable(m_ChannelName);
                    }
                }
                catch (Exception x)
                {
                    reportError(x.toString());
                }
            }
            else
            {
                reportError("Error preparing badge data: " + ErrorCode.getString(ec));
            }
        }

        protected void cleanupBadgeData()
        {
            if (m_BadgeData == null)
            {
                return;
            }

            ErrorCode ec = m_Chat.clearBadgeData(m_ChannelName);

            if (ErrorCode.succeeded(ec))
            {
                m_BadgeData = null;

                try
                {
                    if (m_Listener != null)
                    {
                        m_Listener.onBadgeDataExpired(m_ChannelName);
                    }
                }
                catch (Exception x)
                {
                    reportError(x.toString());
                }
            }
            else
            {
                reportError("Error releasing badge data: " + ErrorCode.getString(ec));
            }
        }

        //#endregion        

        //#region Event Helpers

        protected void fireConnected(String channelName)
        {
            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onConnected(channelName);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        protected void fireDisconnected(String channelName)
        {
            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onDisconnected(channelName);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        //#endregion

        private void disconnectionComplete()
        {
            if (m_ChannelState != ChannelState.Disconnected)
            {
                setChannelState(ChannelState.Disconnected);
                fireDisconnected(m_ChannelName);
                cleanupBadgeData();
            }        	
        }

        //#region IChatChannelListener

        @Override
        public void chatStatusCallback(String channelName, ErrorCode result)
        {
            if (ErrorCode.succeeded(result))
            {
                return;
            }

            // destroy the channel object
            m_Channels.remove(channelName);

            disconnectionComplete();
        }

        @Override
        public void chatChannelMembershipCallback(String channelName, ChatEvent evt, ChatChannelInfo channelInfo)
        {
            switch (evt)
            {
            case TTV_CHAT_JOINED_CHANNEL:
            {
                setChannelState(ChannelState.Connected);
                fireConnected(channelName);
                break;
            }
            case TTV_CHAT_LEFT_CHANNEL:
            {
                disconnectionComplete();
                break;
            }
            default:
            {
                break;
            }
            }
        }

        @Override
        public void chatChannelUserChangeCallback(String channelName, ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList)
        {
            for (int i=0; i<leaveList.length; ++i)
            {
                int index = m_ChannelUsers.indexOf(leaveList[i]);
                if (index >= 0)
                {
                    m_ChannelUsers.remove(index);
                }
            }

            for (int i=0; i<userInfoList.length; ++i)
            {
                // this will find the existing user with the same name
                int index = m_ChannelUsers.indexOf(userInfoList[i]);
                if (index >= 0)
                {
                    m_ChannelUsers.remove(index);
                }

                m_ChannelUsers.add(userInfoList[i]);
            }

            for (int i=0; i<joinList.length; ++i)
            {
                m_ChannelUsers.add(joinList[i]);
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onUsersChanged(m_ChannelName, joinList, leaveList, userInfoList);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }

        @Override
        public void chatChannelRawMessageCallback(String channelName, ChatRawMessage[] messageList)
        {
            for (int i = 0; i < messageList.length; ++i)
            {
                m_RawMessages.addLast(messageList[i]);
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onRawMessagesReceived(m_ChannelName, messageList);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }

            // cap the number of messages cached
            while (m_RawMessages.size() > m_MessageHistorySize)
            {
                m_RawMessages.removeFirst();
            }
        }

        @Override
        public void chatChannelTokenizedMessageCallback(String channelName, ChatTokenizedMessage[] messageList)
        {
            for (int i = 0; i < messageList.length; ++i)
            {
                m_TokenizedMessages.addLast(messageList[i]);
            }

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onTokenizedMessagesReceived(m_ChannelName, messageList);
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }

            // cap the number of messages cached
            while (m_TokenizedMessages.size() > m_MessageHistorySize)
            {
                m_TokenizedMessages.removeFirst();
            }
        }

        @Override
        public void chatClearCallback(String channelName, String username)
        {
            clearMessages(username);
        }

        @Override
        public void chatBadgeDataDownloadCallback(String channelName, ErrorCode error)
        {
            if (ErrorCode.succeeded(error))
            {
                setupBadgeData();
            }
        }

        //#endregion 
    };

    //#region Properties

    public Listener getListener()
    {
        return m_Listener;
    }
    public void setListener(Listener listener)
    {
        m_Listener = listener;
    }

    /**
     * Returns the name of all active channels.
     * @return
     */
    public String[] getActiveChannelNames()
    {
        ArrayList<String> result = new ArrayList<String>();
        for (ChatChannelListener channel : m_Channels.values())
        {
            result.add(channel.getChannelName());
        }

        return result.toArray(new String[result.size()]);
    }

    /**
     * Whether or not the controller has been initialized.
     * @return
     */
    public boolean getIsInitialized()
    {
        return m_ChatState == ChatState.Initialized;
    }

    /**
     * The AuthToken obtained from using the BroadcastController or some other means.
     * @return
     */
    public AuthToken getAuthToken()
    {
        return m_AuthToken;
    }
    /**
     * The AuthToken obtained from using the BroadcastController.
     * @param value
     */
    public void setAuthToken(AuthToken value)
    {
        m_AuthToken = value;
    }

    /**
     * The Twitch client ID assigned to your application.
     * @return
     */
    public String getClientId()
    {
        return m_ClientId;
    }
    /**
     * The Twitch client ID assigned to your application.
     * @param value
     */
    public void setClientId(String value)
    {
        m_ClientId = value;
    }

    /**
     * The secret code gotten from the Twitch site for the client id.
     * @return
     */
    public String getClientSecret()
    {
        return m_ClientSecret;
    }
    /**
     * The secret code gotten from the Twitch site for the client id.
     * @param value
     */
    public void setClientSecret(String value)
    {
        m_ClientSecret = value;
    }

    /**
     * The username to log in with.
     * @return
     */
    public String getUserName()
    {
        return m_UserName;
    }
    /**
     * The username to log in with.
     * @param value
     */
    public void setUserName(String value)
    {
        m_UserName = value;
    }

    /**
     * The current state of the ChatController.
     * @return
     */
    public ChatState getCurrentState()
    {
        return m_ChatState;
    }

    /**
     * The emoticon parsing mode for chat messages.  This must be set before connecting to the channel to set the preference until disconnecting.  
     * If a texture atlas is selected this will trigger a download of emoticon images to create the atlas.
     */
    public EmoticonMode getEmoticonParsingModeMode()
    {
        return m_EmoticonMode;
    }
    /**
     * The emoticon parsing mode for chat messages.  This must be set before connecting to the channel to set the preference until disconnecting.  
     * If a texture atlas is selected this will trigger a download of emoticon images to create the atlas.
     */
    public void setEmoticonParsingModeMode(EmoticonMode mode)
    {
        m_EmoticonMode = mode;
    }

    /**
     * Retrieves the emoticon data that can be used to render icons.
     */
    public ChatEmoticonData getEmoticonData()
    {
        return m_EmoticonData;
    }

    /**
     * The maximum number of messages to be kept in the chat history.
     * @return
     */
    public int getMessageHistorySize()
    {
        return getMessageHistorySize();
    }
    /**
     * The maximum number of messages to be kept in the chat history.
     * @param value
     */
    public void setMessageHistorySize(int value)
    {
        m_MessageHistorySize = value;
    }

    /**
     * The number of milliseconds between message events.
     * @return
     */
    public int getMessageFlushInterval()
    {
        return m_MessageFlushInterval;
    }
    /**
     * The number of milliseconds between message events.
     * @return
     */
    public void setMessageFlushInterval(int milliseconds)
    {
        m_MessageFlushInterval = Math.min(MAX_INTERVAL_MS, Math.max(milliseconds, MIN_INTERVAL_MS));

        if (m_ChatState == ChatState.Initialized)
        {
            m_Chat.setMessageFlushInterval(m_MessageFlushInterval);
        }
    }

    /**
     * The number of milliseconds between events for user joins, leaves and changes in channels.
     * @return
     */
    public int getUserChangeEventInterval()
    {
        return m_UserChangeEventInterval;
    }
    /**
     * The number of milliseconds between events indicating changes in users in channels.
     * @return
     */
    public void setUserChangeEventInterval(int milliseconds)
    {
        m_UserChangeEventInterval = Math.min(MAX_INTERVAL_MS, Math.max(milliseconds, MIN_INTERVAL_MS));

        if (m_ChatState == ChatState.Initialized)
        {
            m_Chat.setUserChangeEventInterval(m_UserChangeEventInterval);
        }
    }

    /**
     * Whether or not currently connected to the channel.
     * @return
     */
    public boolean getIsConnected(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            return false;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getChannelState() == ChannelState.Connected;
    }

    /**
     * The state of the named channel.
     * @return
     */
    public ChannelState getChannelState(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            return ChannelState.Disconnected;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getChannelState();
    }

    /**
     * Whether or not connected anonymously (listen only).
     * @return
     */
    public boolean getIsAnonymous(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            reportError("Unknown channel: " + channelName);
            return false;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getIsAnonymous();
    }

    /**
     * An iterator for the raw chat messages from oldest to newest.
     */
    public Iterator<ChatRawMessage> getRawMessages(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            reportError("Unknown channel: " + channelName);
            return null;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getRawMessages();
    }

    /**
     * An iterator for the tokenized chat messages from oldest to newest.
     */
    public Iterator<ChatTokenizedMessage> getTokenizedMessages(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            reportError("Unknown channel: " + channelName);
            return null;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getTokenizedMessages();
    }

    /**
     * Retrieves the badge data that can be used to render icons.
     */
    public ChatBadgeData getBadgeData(String channelName)
    {
        if (!m_Channels.containsKey(channelName))
        {
            reportError("Unknown channel: " + channelName);
            return null;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.getBadgeData();
    }

    //#endregion

    public ChatController()
    {
        m_Core = Core.getInstance();

        if (m_Core == null)
        {
            m_Core = new Core( new StandardCoreAPI() );
        }

        m_Chat = new Chat( new StandardChatAPI() );
    }

    public boolean initialize()
    {
        if (m_ChatState != ChatState.Uninitialized)
        {
            return false;
        }

        setChatState(ChatState.Initializing);

        ErrorCode ret = m_Core.initialize(m_ClientId, null);
        if (ErrorCode.failed(ret))
        {
            setChatState(ChatState.Uninitialized);

            String err = ErrorCode.getString(ret);
            reportError(String.format("Error initializing Twitch sdk: %s", err));

            return false;
        }

        // initialize chat
        m_ActiveEmoticonMode = m_EmoticonMode;

        HashSet<ChatTokenizationOption> tokenizationOptions = new HashSet<ChatTokenizationOption>();
        switch (m_EmoticonMode)
        {
        case None:
            tokenizationOptions.add(ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_NONE);
            break;
        case Url:
            tokenizationOptions.add(ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_EMOTICON_URLS);
            break;
        case TextureAtlas:
            tokenizationOptions.add(ChatTokenizationOption.TTV_CHAT_TOKENIZATION_OPTION_EMOTICON_TEXTURES);
            break;
        }

        // kick off the async init
        ret = m_Chat.initialize(tokenizationOptions, m_ChatAPIListener);
        if (ErrorCode.failed(ret))
        {
            m_Core.shutdown();
            setChatState(ChatState.Uninitialized);

            String err = ErrorCode.getString(ret);
            reportError(String.format("Error initializing Twitch chat: %s", err));

            return false;
        }
        else
        {
            setChatState(ChatState.Initialized);
            return true;
        }
    }

    /**
     * Connects to the given channel.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
     * @param channelName The name of the channel.
     * @return Whether or not the request was successful.
     */
    public boolean connect(String channelName)
    {
        return connect(channelName, false);
    }

    /**
     * Connects to the given channel anonymously.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
     * @param channelName The name of the channel.
     * @return Whether or not the request was successful.
     */
    public boolean connectAnonymous(String channelName)
    {
        return connect(channelName, true);
    }

    protected boolean connect(String channelName, boolean anonymous)
    {
        if (m_ChatState != ChatState.Initialized)
        {
            return false;
        }

        if (m_Channels.containsKey(channelName))
        {
            reportError("Already in channel: " + channelName);
            return false;
        }

        if (channelName == null || channelName.equals(""))
        {
            return false;
        }

        ChatChannelListener channel = new ChatChannelListener(channelName);
        m_Channels.put(channelName, channel);

        boolean result = channel.connect(anonymous);

        if (!result)
        {
            m_Channels.remove(channelName);
        }

        return result;
    }

    /**
     * Disconnects from the channel.  The result of the attempt will be returned in a Disconnected event.
     * @return Whether or not the disconnect attempt was valid.
     */
    public boolean disconnect(String channelName)
    {
        if (m_ChatState != ChatState.Initialized)
        {
            return false;
        }

        if (!m_Channels.containsKey(channelName))
        {
            reportError("Not in channel: " + channelName);
            return false;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.disconnect();
    }


    public boolean shutdown()
    {
        if (m_ChatState != ChatState.Initialized)
        {
            return false;
        }

        // shutdown asynchronously
        ErrorCode ret = m_Chat.shutdown();
        if (ErrorCode.failed(ret))
        {
            String err = ErrorCode.getString(ret);
            reportError(String.format("Error shutting down chat: %s", err));

            return false;
        }

        cleanupEmoticonData();

        setChatState(ChatState.ShuttingDown);

        return true;
    }

    /**
     * Ensures the controller is fully shutdown before returning.  This may fire callbacks to listeners during the shutdown.
     */
    public void forceSyncShutdown()
    {
        if (this.getCurrentState() != ChatState.Uninitialized)
        {
            this.shutdown();

            // wait for the shutdown to finish
            if (this.getCurrentState() == ChatState.ShuttingDown)
            {
                while (this.getCurrentState() != ChatState.Uninitialized)
                {
                    try
                    {
                        Thread.sleep(200);
                        this.update();
                    }
                    catch (InterruptedException ignored)
                    {
                    }
                }
            }
        }
    }

    /**
     * Periodically updates the internal state of the controller.
     */
    public void update()
    {
        if (m_ChatState == ChatState.Uninitialized)
        {
            return;
        }

        ErrorCode ret = m_Chat.flushEvents();
        if (ErrorCode.failed(ret))
        {
            String err = ErrorCode.getString(ret);
            reportError(String.format("Error flushing chat events: %s", err));
        }
    }

    /**
     * Sends a chat message to the channel.
     * @param channelName The channel to send the message on.
     * @param message The message to send.
     * @return Whether or not the attempt was valid.
     */
    public boolean sendChatMessage(String channelName, String message)
    {
        if (m_ChatState != ChatState.Initialized)
        {
            return false;
        }

        if (!m_Channels.containsKey(channelName))
        {
            reportError("Not in channel: " + channelName);
            return false;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        return channel.sendChatMessage(message);
    }

    /**
     * Clears the entire chat message history for all users.
     */
    public void clearMessages(String channelName)
    {
        clearMessages(channelName, null);
    }

    /**
     * Clears the chat message history for the given user only.
     */
    public void clearMessages(String channelName, String username)
    {
        if (m_ChatState != ChatState.Initialized)
        {
            return;
        }

        if (!m_Channels.containsKey(channelName))
        {
            reportError("Not in channel: " + channelName);
            return;
        }

        ChatChannelListener channel = m_Channels.get(channelName);
        channel.clearMessages(username);
    }

    protected void setChatState(ChatState state)
    {
        if (state == m_ChatState)
        {
            return;
        }

        m_ChatState = state;

        try
        {
            if (m_Listener != null)
            {
                m_Listener.onChatStateChanged(state);
            }
        }
        catch (Exception x)
        {
            reportError(x.toString());
        }
    }

    //#region Emoticon Handling

    protected void downloadEmoticonData()
    {
        // don't download emoticons
        if (m_ActiveEmoticonMode == EmoticonMode.None)
        {
            return;
        }

        if (m_EmoticonData == null)
        {
            ErrorCode ret = m_Chat.downloadEmoticonData();
            if (ErrorCode.failed(ret))
            {
                String err = ErrorCode.getString(ret);
                reportError(String.format("Error trying to download emoticon data: %s", err));
            }
        }
    }

    protected void setupEmoticonData()
    {
        if (m_EmoticonData != null)
        {
            return;
        }

        m_EmoticonData = new ChatEmoticonData();
        ErrorCode ec = m_Chat.getEmoticonData(m_EmoticonData);

        if (ErrorCode.succeeded(ec))
        {
            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onEmoticonDataAvailable();
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }
        }
        else
        {
            reportError("Error preparing emoticon data: " + ErrorCode.getString(ec));
        }
    }

    protected void cleanupEmoticonData()
    {
        if (m_EmoticonData == null)
        {
            return;
        }

        ErrorCode ec = m_Chat.clearEmoticonData();

        if (ErrorCode.succeeded(ec))
        {
            m_EmoticonData = null;

            try
            {
                if (m_Listener != null)
                {
                    m_Listener.onEmoticonDataExpired();
                }
            }
            catch (Exception x)
            {
                reportError(x.toString());
            }        	
        }
        else
        {
            reportError("Error clearing emoticon data: " + ErrorCode.getString(ec));
        }
    }

    //#endregion

    protected boolean checkError(ErrorCode err)
    {
        if (ErrorCode.failed(err))
        {
            reportError(ErrorCode.getString(err));
            return false;
        }

        return true;
    }

    protected void reportError(String err)
    {
        System.out.println(err.toString());
    }

    protected void reportWarning(String err)
    {
        System.out.println(err.toString());
    }
}

