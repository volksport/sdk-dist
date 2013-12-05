//
//  OpenALAudioController.m
//  sdktester
//
//  Created by Auston Stewart on 9/17/13.
//  Copyright (c) 2013 Twitch. All rights reserved.
//

#import "TWOALAudioController.h"
#import <CoreGraphics/CoreGraphics.h>
#include "openalsupport.h"

#define OAL_DEFAULT_DISTANCE 25.0
#define OAL_REFERENCE_DISTANCE 50.0
#define OAL_SAMPLE_BYTES_PER_FRAME 4
#define OAL_SAMPLE_RATE 44100
#define OAL_SAMPLE_BUFFER_FRAMES 8192

@implementation TWOALAudioController
{
	ALuint source;
	ALuint buffer;
	ALCcontext *context;
	ALCdevice *device;
	
	void *data;
	CGPoint sourcePos;
	ALfloat	sourceVolume;
	
	BOOL wasInterrupted;
	BOOL readyForCapture;
}

+ (TWOALAudioController *) sharedAudioController {
	static TWOALAudioController *sharedAudioController = nil;
    
	static dispatch_once_t pred;
	dispatch_once(&pred, ^{
		sharedAudioController = [[TWOALAudioController alloc] init];
	});
    
	return sharedAudioController;
}

- (void) initBuffer
{
	ALenum error = AL_NO_ERROR;
	ALenum format;
	ALsizei size;
	ALsizei freq;
	NSBundle *bundle = [NSBundle mainBundle];
	
	// Load a CAF file for audio playback
	CFURLRef fileURL = CFURLCreateWithFileSystemPath(kCFAllocatorDefault, (__bridge CFStringRef)([bundle pathForResource:@"sound" ofType:@"caf"]), kCFURLPOSIXPathStyle, false);
	
	if (fileURL)
	{
		data = alAudioDataForFile(fileURL, &size, &format, &freq);
		CFRelease(fileURL);
		
		if ((error = alGetError()) != AL_NO_ERROR)
		{
			NSLog(@"TWOAL: Error loading audio file: %x", error);
			exit(1);
		}
		
		// use the static buffer data API
		alBufferDataStaticProc(buffer, format, data, size, freq);
		if ((error = alGetError()) != AL_NO_ERROR)
		{
			NSLog(@"TWOAL: Error attaching audio to buffer: %x", error);
		}
	}
	else
	{
		NSLog(@"TWOAL: Could not find audio file");
	}
}

- (void) initSource
{
	ALenum error = AL_NO_ERROR;
	alGetError(); // Clear the error
    
	// Loop the audio
	alSourcei(source, AL_LOOPING, AL_TRUE);
	
	// Position the source
	float sourcePosAL[] = {sourcePos.x, OAL_DEFAULT_DISTANCE, sourcePos.y};
	alSourcefv(source, AL_POSITION, sourcePosAL);
	
	// Set reference distance
	alSourcef(source, AL_REFERENCE_DISTANCE, OAL_REFERENCE_DISTANCE);
	
	// Attach buffer to source
	alSourcei(source, AL_BUFFER, buffer);
	
	if ((error = alGetError()) != AL_NO_ERROR)
	{
		NSLog(@"TWOAL: Error attaching buffer to source: %x", error);
		exit(1);
	}
}

- (void)initOpenAL
{
	ALenum error;
	
	// Create a new OpenAL Device
	// Pass NULL to specify the system’s default output device
	device = alcOpenDevice(NULL);
	if (device != NULL)
	{
		// Create a new OpenAL Context
		// The new context will render to the OpenAL Device just created
		context = alcCreateContext(device, 0);
		if (context != NULL)
		{
			// Make the new context the Current OpenAL Context
			alcMakeContextCurrent(context);
			
			// Create some OpenAL Buffer Objects
			alGenBuffers(1, &buffer);
			if((error = alGetError()) != AL_NO_ERROR) {
				NSLog(@"TWOAL: Error Generating Buffers: %x", error);
				exit(1);
			}
			
			// Create some OpenAL Source Objects
			alGenSources(1, &source);
			if(alGetError() != AL_NO_ERROR)
			{
				NSLog(@"TWOAL: Error generating sources! %x", error);
				exit(1);
			}
			
		}
	}
	// clear any errors
	alGetError();
	
	[self initBuffer];
	[self initSource];
}

- (void)teardownOpenAL
{
	// Delete the Sources
    alDeleteSources(1, &source);
	// Delete the Buffers
    alDeleteBuffers(1, &buffer);
	
    //Release context
    alcDestroyContext(context);
    //Close device
    alcCloseDevice(device);
}

#pragma mark Play / Pause

- (void)startPlayback
{
	ALenum error;
	
	NSLog(@"TWOAL: Starting audio");
	// Begin playing our source file
	alSourcePlay(source);
	if ((error = alGetError()) != AL_NO_ERROR)
	{
		NSLog(@"TWOAL: Error starting OpenAL source: %x", error);
	} else {
		// Mark our state as playing (the view looks at this)
		_isPlaying = YES;
	}
}

- (void)stopPlayback
{
	ALenum error;
	
	NSLog(@"TWOAL: Stopping audio");
	// Stop playing our source file
	alSourceStop(source);
	if ((error = alGetError()) != AL_NO_ERROR)
	{
		NSLog(@"TWOAL: Error stopping OpenAL source: %x", error);
	} else {
		// Mark our state as not playing (the view looks at this)
		_isPlaying = NO;
	}
}

- (id)init
{
	if ((self = [super init]))
	{
		// Start with our sound source slightly in front of the listener
		sourcePos = CGPointMake(0., -70.);
		
		// Initialize our OpenAL environment
		[self initOpenAL];
	}
	
	return self;
}

- (void)prepareForCapture
{
	ALenum error;
	
	if (readyForCapture) return;
	// Init capture
	_sampleBuffer = (ALCvoid *)malloc(OAL_SAMPLE_BYTES_PER_FRAME * OAL_SAMPLE_BUFFER_FRAMES);
    alcOutputCapturerPrepareProc(OAL_SAMPLE_RATE, AL_FORMAT_STEREO16, OAL_SAMPLE_BUFFER_FRAMES);
    if ((error = alGetError()) != AL_NO_ERROR)
    {
        NSLog(@"TWOAL: Error preparing for capture: %x", error);
        exit(1);
    }
	
	readyForCapture = YES;
}

- (void)startCapture
{
	ALenum error;
	
	if (!readyForCapture)
	{
		[self prepareForCapture];
	}
	
	// Start OpenAL capture
    alcOutputCapturerStartProc();
	
	if ((error = alGetError()) != AL_NO_ERROR) {
		NSLog(@"TWOAL: Error starting capture: %x", error);
	} else {
        NSLog(@"TWOAL: Starting capture.");
	}
}

- (void)stopCapture
{
	ALenum error;
	
	// Stop OpenAL capture
    alcOutputCapturerStopProc();
 
	if ((error = alGetError()) != AL_NO_ERROR) {
		NSLog(@"TWOAL: Error ending capture: %x", error);
	} else {
        NSLog(@"TWOAL: Ending capture.");
	}
}

- (ALint)captureFrames
{
    ALint availableFrames = alcOutputCapturerAvailableSamplesProc();
    
    if (availableFrames)
	{
		// NSLog(@"%d oal frames ready",availableFrames);
		availableFrames = availableFrames > OAL_SAMPLE_BUFFER_FRAMES ? OAL_SAMPLE_BUFFER_FRAMES : availableFrames;
        alcOutputCapturerSamplesProc(_sampleBuffer, availableFrames);
    }
	
	return availableFrames;
}

@end
