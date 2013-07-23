/********************************************************************************************
* Twitch Broadcasting SDK
*
* This software is supplied under the terms of a license agreement with Justin.tv Inc. and
* may not be copied or used except in accordance with the terms of that agreement
* Copyright (c) 2012-2013 Justin.tv Inc.
*********************************************************************************************/

#ifndef TTVSDK_TWITCH_VERSION_H
#define TTVSDK_TWITCH_VERSION_H

#ifdef __cplusplus
extern "C"
{
#endif

// Major version number (manually set)
const int majorVersion = 4;

// Minor version number (automatically set on release)
const int minorVersion = 2;

// Identifies the commit in the release branch in the sdk repo
const char* versionIdentifier = "cb9e8093083b3ea51c439cd3ef0469938c2c30c2";

#ifdef __cplusplus
}
#endif

#endif /* TTVSDK_TWITCH_VERSION_H */
