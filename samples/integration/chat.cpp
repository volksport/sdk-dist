////////////////////////////////////////////////////////////////////////////////////
// This file contains the implementation of the chat system.
////////////////////////////////////////////////////////////////////////////////////

#include "stdafx.h"
#include "chat.h"
#include "twitchsdk.h"
#include "twitchchat.h"
#include "chatrenderer.h"

#include <assert.h>
#include <string>

#define USE_TEXTURE_ATLAS 1

extern TTV_AuthToken gAuthToken;
extern std::string gUserName;

#define CHAT_STATE(__state__) CS_##__state__

namespace
{
	ChatState gChatState = CHAT_STATE(Uninitialized);
	TTV_ErrorCode gAsyncResult = TTV_EC_SUCCESS;
	bool gRequestedEmoticonData = false;
	bool gRequestedBadgeData = false;

	void InitializationCallback(TTV_ErrorCode result, void* /*userdata*/)
	{
		gAsyncResult = result;

		if (TTV_SUCCEEDED(result))
		{
			gChatState = CHAT_STATE(Initialized);
		}
		else
		{
			gChatState = CHAT_STATE(Uninitialized);
		}
	}

	void ShutdownCallback(TTV_ErrorCode result, void* /*userdata*/)
	{
		gAsyncResult = result;

		if (TTV_SUCCEEDED(result))
		{
			gChatState = CHAT_STATE(Uninitialized);
		}
		else
		{
			gChatState = CHAT_STATE(Initialized);
		}
	}

	void ChatStatusCallback(TTV_ErrorCode result, void* /*userdata*/)
	{
		if (TTV_SUCCEEDED(result))
		{
		}
		else if (result == TTV_EC_CHAT_LOST_CONNECTION)
		{
			gChatState = CHAT_STATE(Disconnected);
		}
		else
		{
			gChatState = CHAT_STATE(Disconnected);
			ASSERT_ON_ERROR(result);
		}
	}

	void ChatMembershipCallback(TTV_ChatEvent evt, const TTV_ChatChannelInfo* channelInfo, void* /*userdata*/)
	{
		switch (evt)
		{
		case TTV_CHAT_JOINED_CHANNEL:
			gChatState = CHAT_STATE(Connected);
			break;
		case TTV_CHAT_LEFT_CHANNEL:
			gChatState = CHAT_STATE(Disconnected);
			break;
		default:
			break;
		}
	}

	void ChatUserCallback (const TTV_ChatUserList* joinList, const TTV_ChatUserList* leaveList, const TTV_ChatUserList* userInfoList, void* /*userdata*/)
	{
		for (uint i = 0; i < leaveList->userCount; ++i)
		{
			RemoveChatUser(&leaveList->userList[i]);
		}

		for (uint i = 0; i < joinList->userCount; ++i)
		{
			AddChatUser(&joinList->userList[i]);
		}

		for (uint i = 0; i < userInfoList->userCount; ++i)
		{
			UpdateChatUser(&userInfoList->userList[i]);
		}

		//////////////////////////////////////////////////////////////////////////
		// Important to free user lists when we are done with them
		//////////////////////////////////////////////////////////////////////////
		TTV_Chat_FreeUserList(joinList);
		TTV_Chat_FreeUserList(leaveList);
		TTV_Chat_FreeUserList(userInfoList);
	}

	void ChatTokenizedMessageCallback(const TTV_ChatTokenizedMessageList* messageList, void* /*userdata*/)
	{
		assert (messageList);

		AddChatMessages(messageList);
	}

	void ChatClearCallback(void* /*userdata*/)
	{
		ClearChatMessages();
	}

	void EmoticonDataDownloadCallback(TTV_ErrorCode error, void* /*userdata*/)
	{
		assert( TTV_SUCCEEDED(error) );

		// grab the texture and badge data
		if (TTV_SUCCEEDED(error))
		{
			ProcessEmoticonData();
		}
		// failed, try again
		else
		{
			gRequestedEmoticonData = false;
		}
	}

	void BadgeDataDownloadCallback(TTV_ErrorCode error, void* /*userdata*/)
	{
		assert( TTV_SUCCEEDED(error) );

		// grab the texture and badge data
		if (TTV_SUCCEEDED(error))
		{
			ProcessBadgeData();
		}
		// failed, try again
		else
		{
			gRequestedBadgeData = false;
		}
	}
}


void InitializeChat(const utf8char* channel)
{
	TTV_ErrorCode ret = TTV_Chat_Init(
		TTV_CHAT_TOKENIZATION_OPTION_EMOTICON_TEXTURES,
		InitializationCallback,
		nullptr);
	ASSERT_ON_ERROR(ret);

	TTV_SetTraceLevel(TTV_ML_CHAT);

	gChatState = CHAT_STATE(Initialized);
}


void ShutdownChat()
{
	gChatState = CHAT_STATE(ShuttingDown);

	(void)TTV_Chat_ClearEmoticonData();
	(void)TTV_Chat_ClearBadgeData();

	TTV_ErrorCode ret = TTV_Chat_Shutdown(ShutdownCallback, nullptr);

	if (TTV_SUCCEEDED(ret))
	{
		while (gChatState != CHAT_STATE(Uninitialized))
		{
			FlushChatEvents();
		}
	}
}


ChatState GetChatState()
{
	return gChatState;
}


void FlushChatEvents()
{
	TTV_Chat_FlushEvents();

	switch (gChatState)
	{
		case CHAT_STATE(Uninitialized):
		case CHAT_STATE(Initializing):
		{
			break;
		}
		case CHAT_STATE(Initialized):
		{
			// start downloading the emoticon data
			TTV_ErrorCode ret = TTV_EC_SUCCESS;
			
			if (!gRequestedEmoticonData)
			{
				gRequestedEmoticonData = true;

				ret = TTV_Chat_DownloadEmoticonData(EmoticonDataDownloadCallback, nullptr);
				ASSERT_ON_ERROR(ret);
			}

			// connect to the channel
			gChatState = CHAT_STATE(Connecting);

			TTV_ChatCallbacks chatCallbacks;
			memset(&chatCallbacks, 0, sizeof(chatCallbacks));
			chatCallbacks.statusCallback = ChatStatusCallback;
			chatCallbacks.membershipCallback = ChatMembershipCallback;
			chatCallbacks.userCallback = ChatUserCallback;
			chatCallbacks.messageCallback = nullptr;
			chatCallbacks.tokenizedMessageCallback = ChatTokenizedMessageCallback;
			chatCallbacks.clearCallback = ChatClearCallback;
			chatCallbacks.unsolicitedUserData = nullptr;

			ret = TTV_Chat_Connect(gUserName.c_str(), gAuthToken.data, &chatCallbacks);
			ASSERT_ON_ERROR(ret);

			break;
		}
		case CHAT_STATE(Connecting):
		{
			break;
		}
		case CHAT_STATE(Connected):
		{
			// start downloading the badge data
			if (!gRequestedBadgeData)
			{
				gRequestedBadgeData = true;

				TTV_ErrorCode ret = TTV_Chat_DownloadBadgeData(BadgeDataDownloadCallback, nullptr);
				ASSERT_ON_ERROR(ret);
			}

			break;
		}
		case CHAT_STATE(Disconnected):
		case CHAT_STATE(ShuttingDown):
		{
			ClearChatUsers();
			ClearChatMessages();
			break;
		}
	}
}
