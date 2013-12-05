//
//  TWTwitchStreamer.h
//  Twitch Streamer
//
//  Created by Auston Stewart on 11/7/13.
//  Copyright (c) 2013 Justin.tv, Inc. All rights reserved.
//

#import <Foundation/Foundation.h>

@interface TWTwitchStreamer : NSObject

+ (TWTwitchStreamer *)twitchStreamer;

- (uint8_t *)freeFrameBuffer;
- (void)submitVideoFrame:(const uint8_t *)frameBuffer;
- (void)submitAudioSamples;
- (void)prepareToStream;
- (void)stopStream;

@property (nonatomic,readonly) BOOL isStreaming;

@end
