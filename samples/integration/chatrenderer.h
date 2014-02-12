//////////////////////////////////////////////////////////////////////////////
// This file contains the interface to the chat rendering module.
//////////////////////////////////////////////////////////////////////////////

#ifndef CHATRENDERER_H
#define CHATRENDERER_H

#include "twitchchat.h"

void InitializeChatRenderer(unsigned int windowWidth, unsigned int windowHeight);
void DeinitChatRenderer();

void ProcessEmoticonData(); // Sets up the emoticon data so it can be used during rendering.
void ProcessBadgeData(); // Sets up the badge data so it can be used during rendering.

void AddChatUser(const TTV_ChatUserInfo* user);
void RemoveChatUser(const TTV_ChatUserInfo* user);
void UpdateChatUser(const TTV_ChatUserInfo* user);

void AddChatMessages(const TTV_ChatTokenizedMessageList* messageList);
void ClearChatUsers();
void ClearChatMessages(const utf8char* channel);

void RenderChatText(); // Renders the chat messages to the screen.

void BeginChatInput();
void AppendChatInput(char ch);
void EndChatInput(bool submit);
bool AcceptingChatInput();

#endif
