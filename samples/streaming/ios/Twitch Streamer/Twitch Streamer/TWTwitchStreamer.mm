//
//  TWTwitchStreamer.m
//  Twitch Streamer
//
//  Created by Auston Stewart on 11/7/13.
//  Copyright (c) 2014 Justin.tv, Inc. All rights reserved.
//

#import "TWTwitchStreamer.h"
#import "TWOALAudioController.h"
#import <AVFoundation/AVFoundation.h>
#import "openalsupport.h"
#include "twitchsdk.h"

#define TW_NUM_FRAMEBUFFERS 3

typedef NS_ENUM(NSInteger, TWStreamingState)
{
	TWStreamingStateInitialized,
	TWStreamingStateAuthenticated,
	TWStreamingStateLoggedIn,
	TWStreamingStateReadyToStream,
	TWStreamingStateStreaming,
	TWStreamingStateError
};

@interface TWTwitchStreamer ()

@property (nonatomic, readwrite) TWStreamingState state;
@property (nonatomic, readwrite) TTV_IngestServer ingestServer;
@property (nonatomic, readwrite) TTV_IngestList ingestList;

- (void)addFrameBufferToFreeList:(uint8_t *)frameBuffer;

@end

void TwitchAuthCompletedCallback(TTV_ErrorCode result, void* userData)
{
	TWTwitchStreamer* streamer = (__bridge TWTwitchStreamer *)userData;
	NSLog(@"TWSTREAMER: In TwitchAuthCompletedCallback");
	if (TTV_SUCCEEDED(result))
	{
		streamer.state = TWStreamingStateAuthenticated;
	}
	else
	{
		NSLog(@"TWSTREAMER: Authentication failed.");
	}
}

void TwitchLoginCallback(TTV_ErrorCode result, void* userData)
{
	TWTwitchStreamer* streamer = (__bridge TWTwitchStreamer *)userData;
	
	if (TTV_SUCCEEDED(result))
	{
		streamer.state = TWStreamingStateLoggedIn;
	}
	else
	{
		NSLog(@"TWSTREAMER: Login failed.");
	}
}

void TwitchIngestListCallback(TTV_ErrorCode result, void* userData)
{
	TWTwitchStreamer* streamer = (__bridge TWTwitchStreamer *)userData;
	
	if (TTV_SUCCEEDED(result))
	{
		for (NSUInteger i = 0; i < streamer.ingestList.ingestCount; ++i)
		{
			// Use the default server for now
			if (streamer.ingestList.ingestList[i].defaultServer)
			{
				streamer.ingestServer = streamer.ingestList.ingestList[i];
				streamer.state = TWStreamingStateReadyToStream;
				return;
			}
		}
		NSLog(@"TWSTREAMER: Failed to find default ingest server.");
	}
	else
	{
		NSLog(@"TWSTREAMER: Ingest server fetch failed.");
	}
}

void TwitchFrameUnlockCallback(const uint8_t* buffer, void* userData)
{
	TWTwitchStreamer* streamer = (__bridge TWTwitchStreamer *)userData;
	
	uint8_t* p = const_cast<uint8_t*>(buffer);
	[streamer addFrameBufferToFreeList:p];
}

@implementation TWTwitchStreamer
{
	ALCvoid *_sampleBuffer;
	NSDictionary *_configDict;
	TWStreamingState _state;
	TTV_AuthToken _authToken;
	TTV_ChannelInfo _channelInfo;
	TTV_IngestList _ingestList;
	uint8_t **_frameBuffers;
	uint8_t **_freeFrameBuffers;
	dispatch_source_t _audioSubmissionSource;
	dispatch_queue_t _audioSubmissionQueue;
	
	NSTimer *_pollTasksTimer;
	NSTimer *_submitAudioTimer;
}

+ (TWTwitchStreamer *) twitchStreamer
{
	static TWTwitchStreamer *twitchStreamer = nil;
    
	static dispatch_once_t pred;
	dispatch_once(&pred, ^{
		twitchStreamer = [[TWTwitchStreamer alloc] init];
	});
    
	return twitchStreamer;
}

- (void)dealloc
{
	[self stopPollingTasks];
	[self stopStream];
}

- (id)init
{
	if ((self = [super init])) {
	
		if (![self loadConfiguration])
		{
			NSLog(@"TWSTREAMER: Configuration file missing or invalid");
			return nil;
		}

		TTV_ErrorCode ret = TTV_Init(NULL, [(NSString *)_configDict[@"clientId"] UTF8String], NULL);
		if (TTV_SUCCEEDED(ret))
		{
			TTV_SetTraceLevel(TTV_ML_ERROR);
			_state = TWStreamingStateInitialized;
			_audioSubmissionSource = NULL;
			_channelInfo.size = sizeof(_channelInfo);
		}
		else
		{
			NSLog(@"TWSTREAMER: Unable to initialize Twitch SDK");
			return nil;
		}
	}
	
	return self;
}

- (BOOL)loadConfiguration
{
	_configDict = [NSDictionary dictionaryWithContentsOfFile:[[NSBundle mainBundle] pathForResource:@"sdkconfig" ofType:@"plist"]];
	
	return (_configDict && (((NSString *)_configDict[@"clientId"]).length &&
						  ((NSString *)_configDict[@"clientSecret"]).length &&
						  ((NSString *)_configDict[@"username"]).length &&
						  ((NSString *)_configDict[@"password"]).length) &&
						  [((NSNumber *)_configDict[@"outputBitrate"]) intValue]);
}

- (void)pollTasks
{
	TTV_ErrorCode err = TTV_PollTasks();
	if (!TTV_SUCCEEDED(err)) NSLog(@"TWSTREAMER: Error when polling tasks");
}

- (void)startPollingTasks
{
	if (_pollTasksTimer == nil)
		_pollTasksTimer = [NSTimer scheduledTimerWithTimeInterval:.25 target:self selector:@selector(pollTasks) userInfo:nil repeats:YES];
}

- (void)stopPollingTasks
{
	if (_pollTasksTimer) {
		
		[_pollTasksTimer invalidate];
		_pollTasksTimer = nil;
	}
}

- (void)setState:(TWStreamingState)state
{
	TTV_ErrorCode ret = TTV_EC_SUCCESS;
	_state = state;
	
	switch (_state) {
		case TWStreamingStateAuthenticated:
			NSLog(@"TWSTREAMER: Authenticated");
			ret = TTV_Login(&_authToken, TwitchLoginCallback,(__bridge void *)self, &_channelInfo);
			break;
		case TWStreamingStateLoggedIn:
			NSLog(@"TWSTREAMER: Logged in");
			ret = TTV_GetIngestServers(&_authToken, TwitchIngestListCallback, (__bridge void *)self, &_ingestList);
			break;
		case TWStreamingStateReadyToStream:
			[self stopPollingTasks];
			[self startStream];
			break;
		default:
			break;
	}
	
	if (!TTV_SUCCEEDED(ret)) NSLog(@"TWSTREAMER: Failed request for update: %d",ret);
}

- (void)prepareToStream
{
	if (_state == TWStreamingStateInitialized) {
		
		[self authenticate];
		[self startPollingTasks];
	}
	// FIXME: Handle streaming preparation from other states
}

- (void)authenticate
{
	TTV_AuthParams authParams;
	authParams.size = sizeof(TTV_AuthParams);
	authParams.userName = [(NSString *)_configDict[@"username"] UTF8String];
	authParams.password = [(NSString *)_configDict[@"password"] UTF8String];
	authParams.clientSecret = [(NSString *)_configDict[@"clientSecret"] UTF8String];
	
	TTV_ErrorCode ret = TTV_RequestAuthToken(&authParams, (TTV_RequestAuthToken_Broadcast | TTV_RequestAuthToken_Chat), TwitchAuthCompletedCallback,(__bridge void *)self, &_authToken);
	if (!TTV_SUCCEEDED(ret))
	{
		NSLog(@"TWSTREAMER: Unable to request authentication.");
		_state = TWStreamingStateInitialized;
	}
	else NSLog(@"TWSTREAMER: Requesting auth token");
}

- (void)startStream
{
	if (_state == TWStreamingStateReadyToStream)
	{
		// Set the game title
		TTV_StreamInfoForSetting streamInfo;
		streamInfo.size = sizeof(TTV_StreamInfoForSetting);
		strcpy(streamInfo.streamTitle, "Playing my awesome game");
		strcpy(streamInfo.gameName, "Twitch Streamer");
		TTV_ErrorCode ret = TTV_SetStreamInfo(&_authToken, _channelInfo.name, &streamInfo, nullptr, nullptr);
		if (!TTV_SUCCEEDED(ret)) return;
		
		TTV_VideoParams videoParams;
		memset(&videoParams, 0, sizeof(TTV_VideoParams));
		videoParams.size = sizeof(TTV_VideoParams);
		videoParams.maxKbps = [(NSNumber *)_configDict[@"outputBitrate"] intValue];
		videoParams.targetFps = 30;
		videoParams.verticalFlip = false;
		videoParams.pixelFormat = TTV_PF_BGRA;
		videoParams.encodingCpuUsage = TTV_ECU_MEDIUM;
		videoParams.outputWidth = [UIScreen mainScreen].bounds.size.height * [UIScreen mainScreen].scale;
		videoParams.outputHeight = [UIScreen mainScreen].bounds.size.width * [UIScreen mainScreen].scale;
		
		NSLog(@"TWSTREAMER: Setting output video size: %dx%d",videoParams.outputWidth,videoParams.outputHeight);
		// Setup the audio parameters
		TTV_AudioParams audioParams;
		memset(&audioParams, 0, sizeof(TTV_AudioParams));
		audioParams.size = sizeof(TTV_AudioParams);
		audioParams.audioEnabled = [_configDict[@"openALCapture"] boolValue] || [_configDict[@"microphoneCapture"] boolValue];
		audioParams.enablePassthroughAudio = [_configDict[@"openALCapture"] boolValue];
		audioParams.enableMicCapture = [_configDict[@"microphoneCapture"] boolValue];
		
		// Allocate frame buffers. We must allocate 3 so that we always have one free for use, since at any
		// given time 2 will be in use by the SDK (one queued up and waiting and another being encoded).
		_frameBuffers = (uint8_t **)malloc(sizeof(uint8_t *) * TW_NUM_FRAMEBUFFERS);
		_freeFrameBuffers = (uint8_t **)malloc(sizeof(uint8_t *) * TW_NUM_FRAMEBUFFERS);
		
		for (int i = 0; i < 3; ++i)
		{
			uint32_t size = videoParams.outputWidth * videoParams.outputHeight * 4;
			_frameBuffers[i] = (uint8_t *)malloc(sizeof(uint8_t) * size);
			_freeFrameBuffers[i] = _frameBuffers[i];
		}
		
		// Start Streaming
		ret = TTV_Start(&videoParams, &audioParams, &_ingestServer, 0, nullptr, nullptr);
		if (TTV_SUCCEEDED(ret)) {
			
			if ([_configDict[@"openALCapture"] boolValue] && [_configDict[@"microphoneCapture"] boolValue]) {
				
				// Set audio levels to avoid clipping if both microphone and OpenAL audio are mixed
				TTV_SetVolume(TTV_RECORDER_DEVICE, 0.9f);
				TTV_SetVolume(TTV_PASSTHROUGH_DEVICE, 0.8f);
			}
			
			// Start OpenAL Capture
			if ([_configDict[@"openALCapture"] boolValue])
				[[TWOALAudioController sharedAudioController] startCapture];
			
			self.state = TWStreamingStateStreaming;
		}
		else NSLog(@"TWSTREAMER: Unable to start streaming.");
	}
}

- (void)stopStream
{
	if (_state == TWStreamingStateStreaming) {
	
		// Stop OpenAL Capture
		[[TWOALAudioController sharedAudioController] stopCapture];
	
		// Free our frame buffers
		for (int i = 0; i < TW_NUM_FRAMEBUFFERS; ++i)
		{
			free(_frameBuffers[i]);
		}
		free(_frameBuffers);
		free(_freeFrameBuffers);
		_frameBuffers = NULL;
		_freeFrameBuffers = NULL;
	}
}

- (BOOL)isStreaming
{
	return (_state == TWStreamingStateStreaming);
}

- (void)submitVideoFrame:(const uint8_t *)frameBuffer
{
	TTV_ErrorCode ret;
	
	if (_state == TWStreamingStateStreaming)
	{
		ret = TTV_SubmitVideoFrame(frameBuffer, TwitchFrameUnlockCallback, (__bridge void *)self);
		if (!TTV_SUCCEEDED(ret)) NSLog(@"TWSTREAMER: Error submitting video frame: %d",ret);
	}
}

- (void)submitAudioSamples
{
	TTV_ErrorCode ret;
	ALint availableFrames;
	
	if (_state == TWStreamingStateStreaming)
	{
		availableFrames = [[TWOALAudioController sharedAudioController] captureFrames];
		if (availableFrames)
		{
			ret = TTV_SubmitAudioSamples((const int16_t*)[TWOALAudioController sharedAudioController].sampleBuffer,(uint)availableFrames * 2);
			if (!TTV_SUCCEEDED(ret)) NSLog(@"TWSTREAMER: Error submitting audio samples: %d",ret);
		}
	}
}

- (uint8_t *)freeFrameBuffer
{
	uint8_t* freeBuffer;
	for (int i = 0; i < TW_NUM_FRAMEBUFFERS; ++i) {
		
		freeBuffer = _freeFrameBuffers[i];
		if (freeBuffer != NULL) {
			
			_freeFrameBuffers[i] = NULL;
			break;
		}
	}
	return freeBuffer;
}

- (void)addFrameBufferToFreeList:(uint8_t *)frameBuffer
{
	uint8_t* freeBuffer;
	for (int i = 0; i < TW_NUM_FRAMEBUFFERS; ++i) {
		
		freeBuffer = _freeFrameBuffers[i];
		if (freeBuffer == NULL) {
			
			_freeFrameBuffers[i] = frameBuffer;
			break;
		}
	}
}

@end
