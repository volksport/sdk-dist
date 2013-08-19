package tv.twitch.chat;

import java.util.*;
import tv.twitch.broadcast.AuthToken;
import tv.twitch.broadcast.ErrorCode;


/**
 * The state machine which manages the chat state.  It provides a high level interface to the SDK libraries.  The ChatController (CC) performs many operations
 * asynchronously and hides the details.  This can be tweaked if needed but should handle all your chat needs (other than emoticons which may be provided in the future).
 * 
 * The typical order of operations a client of CC will take is:
 * 
 * - Subscribe for events via delegates on ChatController
 * - Initialize BroadcastController via BC.InitializeTwitch()
 * - Call CC.Connect() / call CC.ConnectAnonymous()
 * - Wait for the connection callback 
 * - Call CC.SendChatMessage() to send messages (if not connected anonymously)
 * - Receive message callbacks
 * - Call CC.Disconnect() when done
 * 
 * Events will fired during the call to CC.Update().  When chat messages are received RawMessagesReceived will be fired.
 * 
 * NOTE: The implementation of emoticon data is not yet complete and currently not available.
 */
public class ChatController implements IChatCallbacks
{
    //#region Types

	/**
	 * The possible states the ChatController can be in.
	 */
    public enum ChatState
    {
        Uninitialized,  //!< Chat is not yet initialized.
        Initialized,    //!< The component is initialized.
        Connecting,     //!< Currently attempting to connect to the channel.
        Connected,      //!< Connected to the channel.
        Disconnected    //!< Initialized but not connected.
    }

    /**
     * The listener interface for events from the ChatController. 
     */
    public interface Listener
    {
//    	/**
//    	 * The callback signature for the event fired when a tokenized set of messages has been received.
//    	 * @param messages
//    	 */
        //void onTokenizedMessagesReceived(ChatTokenizedMessage[] messages);
    	
    	/**
    	 * The callback signature for the event fired when a set of text-only messages has been received.
    	 * @param messages
    	 */
        void onRawMessagesReceived(ChatMessage[] messages);
        
        /**
         * The callback signature for the event fired when users join, leave or changes their status in the channel.
         * @param joinList The list of users who have joined the room.
         * @param leaveList The list of useres who have left the room.
         * @param userInfoList The list of users who have changed their status.
         */
        void onUsersChanged(ChatUserInfo[] joinList, ChatUserInfo[] leaveList, ChatUserInfo[] userInfoList);
        
        /**
         * The callback signature for the event fired when the local user has been connected to the channel.
         */
        void onConnected();
        
        /**
         * The callback signature for the event fired when the local user has been disconnected from the channel.
         */
        void onDisconnected();
        
        /**
         * The callback signature for the event fired when the messages in the room should be cleared.  The UI should be cleared of any previous messages.
         */
        void onMessagesCleared();
        
        /**
         * The callback signature for the event fired when the emoticon data has been made available.
         */
        void onEmoticonDataAvailable();
        
        /**
         * The callback signature for the event fired when the emoticon data is no longer valid.
         */
        void onEmoticonDataExpired();
    }

    //#endregion

    //#region Memeber Variables

    protected Listener m_Listener = null;

    protected String m_UserName = "";
    protected String m_ChannelName = "";
    
    protected String m_ClientId = "";
    protected String m_ClientSecret = "";
    protected Chat m_Chat = null;

    protected boolean m_ChatInitialized = false;
    protected boolean m_Anonymous = false;
    protected ChatState m_ChatState = ChatState.Uninitialized;
    protected AuthToken m_AuthToken = new AuthToken();

    protected List<ChatUserInfo> m_ChannelUsers = new ArrayList<ChatUserInfo>();
    protected LinkedList<ChatMessage> m_Messages = new LinkedList<ChatMessage>();
    protected int m_MessageHistorySize = 128;

    protected boolean m_UseEmoticons = false;
    protected boolean m_EmoticonDataDownloaded = false;

    //#endregion


    //#region IChatCallbacks

    public void chatStatusCallback(ErrorCode result)
    {
        if (ErrorCode.succeeded(result))
        {
            return;
        }

        m_ChatState = ChatState.Disconnected;
    }

    public void chatChannelMembershipCallback(ChatEvent evt, ChatChannelInfo channelInfo)
    {
        switch (evt)
        {
            case TTV_CHAT_JOINED_CHANNEL:
            {
                m_ChatState = ChatState.Connected;
                fireConnected();
                break;
            }
            case TTV_CHAT_LEFT_CHANNEL:
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

    public void chatChannelUserChangeCallback(ChatUserList joinList, ChatUserList leaveList, ChatUserList userInfoList)
    {
        for (int i=0; i<leaveList.userList.length; ++i)
        {
            int index = m_ChannelUsers.indexOf(leaveList.userList[i]);
            if (index >= 0)
            {
                m_ChannelUsers.remove(index);
            }
        }

        for (int i=0; i<userInfoList.userList.length; ++i)
        {
            // this will find the existing user with the same name
            int index = m_ChannelUsers.indexOf(userInfoList.userList[i]);
            if (index >= 0)
            {
                m_ChannelUsers.remove(index);
            }

            m_ChannelUsers.add(userInfoList.userList[i]);
        }

        for (int i=0; i<joinList.userList.length; ++i)
        {
            m_ChannelUsers.add(joinList.userList[i]);
        }

        try
        {
            if (m_Listener != null)
            {
            	m_Listener.onUsersChanged(joinList.userList, leaveList.userList, userInfoList.userList);
            }
        }
        catch (Exception x)
        {
        	reportError(x.toString());
        }
    }

    public void chatQueryChannelUsersCallback(ChatUserList userList)
    {
        // listening for incremental changes so no need for full query
    }

    public void chatChannelMessageCallback(ChatMessageList messageList)
    {
        for (int i = 0; i < messageList.messageList.length; ++i)
        {
            m_Messages.addLast(messageList.messageList[i]);
        }

        try
        {
        	// TODO: finish emoticons 
//            if (m_UseEmoticons)
//            {
//                if (m_Listener != null)
//                {
//                    List<ChatTokenizedMessage> list = new ArrayList<ChatTokenizedMessage>();
//                    
//                    for (int i = 0; i < messageList.messageList.length; ++i)
//                    {
//                        ChatTokenizedMessage tokenized = null;
//                        ErrorCode ret = m_Chat.TokenizeMessage(messageList.messageList[i], out tokenized);
//                        if (ErrorCode.failed(ret) || tokenized == null)
//                        {
//                            String err = ErrorCode.getString(ret);
//                            reportError(String.format("Error disconnecting: %s", err));
//                        }
//                        else
//                        {
//                            list.add(tokenized);
//                        }
//                    }
//
//                    ChatTokenizedMessage[] arr = (ChatTokenizedMessage[])list.toArray();
//
//                    m_Listener.onTokenizedMessagesReceived(arr);
//                }
//            }
//            else
            {
                if (m_Listener != null)
                {
                	m_Listener.onRawMessagesReceived(messageList.messageList);
                }
            }
        }
        catch (Exception x)
        {
        	reportError(x.toString());
        }

        // cap the number of messages cached
        while (m_Messages.size() > m_MessageHistorySize)
        {
            m_Messages.removeFirst();
        }
    }

    public void chatClearCallback(String channelName)
    {
        clearMessages();
    }

    public void emoticonDataDownloadCallback(ErrorCode error)
    {
        // grab the texture and badge data
        if (ErrorCode.succeeded(error))
        {
            setupEmoticonData();
        }
    }

    //#endregion


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
     * Whether or not the controller has been initialized.
     * @return
     */
    public boolean getIsInitialized()
    {
        return m_ChatInitialized;
    }
    
	/**
	 * Whether or not currently connected to the channel.
	 * @return
	 */
    public boolean getIsConnected()
    {
        return m_ChatState == ChatState.Connected;
    }

    /**
     * Whether or not connected anonymously (listen only).
     * @return
     */
    public boolean getIsAnonymous()
    {
        return m_Anonymous;
    }

    /**
     * The AuthToken obtained from using the BroadcastController.
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
     * The maximum number of messages to be kept in the chat history.
     * @return
     */
    public int getMessageHistorySize()
    {
        return m_MessageHistorySize;
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
     * The current state of the ChatController.
     * @return
     */
    public ChatState getCurrentState()
    {
        return m_ChatState;
    }

    /**
     * An iterator for the chat messages from oldest to newest.
     * @return
     */
    public Iterator<ChatMessage> getMessages()
    {
        return m_Messages.iterator();
    }

    // TODO: finish emoticon support
//    public boolean getUseEmoticons()
//    {
//        return m_UseEmoticons;
//    }
//    public void setUseEmoticons(boolean value)
//    {
//        m_UseEmoticons = value;
//        downloadEmoticonData();
//    }

    //#endregion

    public ChatController()
    {
    	m_Chat = new Chat( new CoreChatAPI() );
    }

    /**
     * Connects to the given channel.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
     * @param channel The name of the channel.
     * @return Whether or not the request was successful.
     */
    public boolean connect(String channel)
    {
        disconnect();

        m_Anonymous = false;
        m_ChannelName = channel;

        return initialize(channel);
    }

    /**
     * Connects to the given channel anonymously.  The actual result of the connection attempt will be returned in the Connected / Disconnected event.
     * @param channel The name of the channel.
     * @return Whether or not the request was successful.
     */
    public boolean connectAnonymous(String channel)
    {
        disconnect();

        m_Anonymous = true;
        m_ChannelName = channel;
    
        return initialize(channel);
    }

    /**
     * Disconnects from the channel.  The result of the attempt will be returned in a Disconnected event.
     * @return Whether or not the disconnect attempt was valid.
     */
    public boolean disconnect()
    {
        if (m_ChatState == ChatState.Connected || 
            m_ChatState == ChatState.Connecting)
        {
            ErrorCode ret = m_Chat.disconnect();
            if (ErrorCode.failed(ret))
            {
                String err = ErrorCode.getString(ret);
                reportError(String.format("Error disconnecting: %s", err));
                
                return false;
            }

            fireDisconnected();
        }
        else if (m_ChatState == ChatState.Disconnected)
        {
            fireDisconnected();
        }

        return shutdown();
    }

    protected boolean initialize(String channel)
    {
        if (m_ChatInitialized)
        {
            return false;
        }

        ErrorCode ret = m_Chat.initialize(channel);
        if (ErrorCode.failed(ret))
        {
            String err = ErrorCode.getString(ret);
            reportError(String.format("Error initializing chat: %s", err));

            fireDisconnected();
            
            return false;
        }
        else
        {
            m_ChatInitialized = true;
            m_Chat.setChatCallbacks(this);
            m_ChatState = ChatState.Initialized;
            
            return true;
        }
    }

    protected boolean shutdown()
    {
        if (m_ChatInitialized)
        {
            ErrorCode ret = m_Chat.shutdown();
            if (ErrorCode.failed(ret))
            {
                String err = ErrorCode.getString(ret);
                reportError(String.format("Error shutting down chat: %s", err));
                
                return false;
            }
        }

        m_ChatState = ChatState.Uninitialized;
        m_ChatInitialized = false;

        cleanupEmoticonData();

        m_Chat.setChatCallbacks(null);
        
        return true;
    }

    /**
     * Periodically updates the internal state of the controller.
     */
    public void update()
    {
        if (!m_ChatInitialized)
        {
            return;
        }

        ErrorCode ret = m_Chat.flushEvents();
        if (ErrorCode.failed(ret))
        {
            String err = ErrorCode.getString(ret);
            reportError(String.format("Error flushing chat events: %s", err));
        }

        switch (m_ChatState)
        {
	        case Uninitialized:
	        {
		        break;
	        }
	        case Initialized:
	        {
                // connect to the channel
                if (m_Anonymous)
                {
                    ret = m_Chat.connectAnonymous();
                }
                else
                {
                    ret = m_Chat.connect(m_ChannelName, m_AuthToken.data);
                }

                if (ErrorCode.failed(ret))
                {
                    String err = ErrorCode.getString(ret);
                    reportError(String.format("Error connecting: %s", err));

                    shutdown();

                    fireDisconnected();
                }
                else
                {
                    m_ChatState = ChatState.Connecting;
                    downloadEmoticonData();
                }

		        break;
	        }
            case Connecting:
            {
                break;
            }
            case Connected:
            {
                break;
            }
            case Disconnected:
            {
                disconnect();
                break;
            }
        }
    }

    /**
     * Sends a chat message to the channel.
     * @param message The message to send.
     * @return Whether or not the attempt was valid.
     */
    public boolean sendChatMessage(String message)
    {
        if (m_ChatState != ChatState.Connected)
        {
            return false;
        }

        ErrorCode ret = m_Chat.sendMessage(message);
        if (ErrorCode.failed(ret))
        {
            String err = ErrorCode.getString(ret);
            reportError(String.format("Error sending chat message: %s", err));
            
            return false;
        }
        
        return true;
    }

    /**
     * Clears the chat message history.
     */
    public void clearMessages()
    {
        m_Messages.clear();

        try
        {
            if (m_Listener != null)
            {
            	m_Listener.onMessagesCleared();
            }
        }
        catch (Exception x)
        {
        	reportError(x.toString());
        }
    }

    //#region Event Helpers

    protected void fireConnected()
    {
        try
        {
            if (m_Listener != null)
            {
            	m_Listener.onConnected();
            }
        }
        catch (Exception x)
        {
        	reportError(x.toString());
        }
    }

    protected void fireDisconnected()
    {
        try
        {
            if (m_Listener != null)
            {
            	m_Listener.onDisconnected();
            }
        }
        catch (Exception x)
        {
        	reportError(x.toString());
        }
    }

    //#endregion

    //#region Emoticon Handling

    protected void downloadEmoticonData()
    {
    	// TODO: 
//        if (m_UseEmoticons &&
//            !m_EmoticonDataDownloaded &&
//            m_ChatInitialized)
//        {
//            ErrorCode ret = m_Chat.downloadEmoticonData();
//            if (ErrorCode.failed(ret))
//            {
//                String err = ErrorCode.getString(ret);
//                reportError(String.format("Error trying to download emoticon data: %s", err));
//            }
//        }
    }

    protected void setupEmoticonData()
    {
    	// TODO: 
//        m_EmoticonDataDownloaded = true;
//
//        if (m_Listener != null)
//        {
//        	m_Listener.onEmoticonDataAvailable();
//        }
    }

    protected void cleanupEmoticonData()
    {
    	// TODO: 
//        m_EmoticonDataDownloaded = false;
//
//        if (m_Listener != null)
//        {
//        	m_Listener.onEmoticonDataExpired();
//        }
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

