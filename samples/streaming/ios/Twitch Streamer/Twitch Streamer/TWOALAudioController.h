//
//  OpenALAudioController.h
//  sdktester
//
//  Created by Auston Stewart on 9/17/13.
//  Copyright (c) 2013 Twitch. All rights reserved.
//

#import <Foundation/Foundation.h>
#import <OpenAL/al.h>
#import <OpenAL/alc.h>

@interface TWOALAudioController : NSObject

+ (TWOALAudioController *)sharedAudioController;
- (void)startPlayback;
- (void)stopPlayback;
- (void)prepareForCapture;
- (void)startCapture;
- (void)stopCapture;
- (ALint)captureFrames;

@property (nonatomic,readonly) ALCvoid *sampleBuffer;
@property (nonatomic,readonly) BOOL isPlaying;

@end
