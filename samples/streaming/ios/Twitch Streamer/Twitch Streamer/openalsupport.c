//
//  OpenALSupport.c
//  sdktester
//
//  Created by Auston Stewart on 9/17/13.
//  Copyright (c) 2013 Twitch. All rights reserved.
//

#include "openalsupport.h"

typedef ALvoid AL_APIENTRY (*alcOutputCapturerPrepareProcPtr)(ALCuint frequency, ALCenum format, ALCsizei maxsamplecount);
typedef ALvoid AL_APIENTRY (*alcOutputCapturerStartProcPtr)();
typedef ALvoid AL_APIENTRY (*alcOutputCapturerStopProcPtr)();
typedef ALint  AL_APIENTRY (*alcOutputCapturerAvailableSamplesProcPtr)();
typedef ALvoid AL_APIENTRY (*alcOutputCapturerSamplesProcPtr)(ALCvoid *buffer, ALCsizei samplecount);
typedef ALvoid AL_APIENTRY (*alBufferDataStaticProcPtr)(const ALint bid, ALenum format, ALvoid* data, ALsizei size, ALsizei freq);

#pragma mark Capture Functions

ALvoid  alcOutputCapturerPrepareProc(ALCuint frequency, ALCenum format, ALCsizei maxsamplecount)
{
	static alcOutputCapturerPrepareProcPtr proc = NULL;
	
	if (proc == NULL)
	{
		proc = (alcOutputCapturerPrepareProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alcOutputCapturerPrepare");
	}

	if (proc)
	{
		proc(frequency,format,maxsamplecount);
	}

	return;
}

ALvoid  alcOutputCapturerStartProc()
{
	static alcOutputCapturerStartProcPtr proc = NULL;

	if (proc == NULL)
	{
		proc = (alcOutputCapturerStartProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alcOutputCapturerStart");
	}
    
	if (proc)
	{
		proc();
	}

	return;
}

ALvoid  alcOutputCapturerStopProc()
{
	static alcOutputCapturerStopProcPtr proc = NULL;
    
	if (proc == NULL)
	{
		proc = (alcOutputCapturerStopProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alcOutputCapturerStop");
	}
    
	if (proc)
	{
		proc();
	}

	return;
}

ALint  alcOutputCapturerAvailableSamplesProc()
{
	static alcOutputCapturerAvailableSamplesProcPtr proc = NULL;

	if (proc == NULL)
	{
		proc = (alcOutputCapturerAvailableSamplesProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alcOutputCapturerAvailableSamples");
	}

	if (proc)
	{
		return proc();
	}
	
	return 0;
}

ALvoid alcOutputCapturerSamplesProc(ALCvoid *buffer, ALCsizei samplecount)
{
	static alcOutputCapturerSamplesProcPtr proc = NULL;
    
    if (proc == NULL)
	{
		proc = (alcOutputCapturerSamplesProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alcOutputCapturerSamples");
	}
    
	if (proc)
	{
		proc(buffer,samplecount);
	}
	
	return;
}

#pragma mark Sample Loading and Buffering

ALvoid  alBufferDataStaticProc(const ALint bid, ALenum format, ALvoid* data, ALsizei size, ALsizei freq)
{
	static alBufferDataStaticProcPtr proc = NULL;
    
	if (proc == NULL)
	{
		proc = (alBufferDataStaticProcPtr) alcGetProcAddress(NULL, (const ALCchar*) "alBufferDataStatic");
	}
    
	if (proc)
	{
		proc(bid, format, data, size, freq);
	}
    
	return;
}

void* alAudioDataForFile(CFURLRef inFileURL, ALsizei *outDataSize, ALenum *outDataFormat, ALsizei *outSampleRate)
{
	OSStatus error = noErr;
	SInt64 audioFramesInFile = 0;
	AudioStreamBasicDescription theFileFormat;
	UInt32 thePropertySize = sizeof(theFileFormat);
	ExtAudioFileRef extRef = NULL;
	void* data = NULL;
	AudioStreamBasicDescription outputASBD;
	
	// Open a file with ExtAudioFileOpen()
	error = ExtAudioFileOpenURL(inFileURL, &extRef);
	if (error)
	{
		printf("ExtAudioFileOpenURL FAILED, Error = %ld", error);
		goto Exit;
	}
	
	// Get the audio data format
	error = ExtAudioFileGetProperty(extRef, kExtAudioFileProperty_FileDataFormat, &thePropertySize, &theFileFormat);
	if (error)
	{
		printf("ExtAudioFileGetProperty(kExtAudioFileProperty_FileDataFormat) FAILED, Error = %ld", error);
		goto Exit;
	}
	
	if (theFileFormat.mChannelsPerFrame > 2)
	{
		printf("Unsupported Format, channel count is greater than stereo");
		goto Exit;
	}
	
	// Set the client format to 16 bit signed integer (native-endian) data
	// Maintain the channel count and sample rate of the original source format
	outputASBD.mSampleRate = theFileFormat.mSampleRate;
	outputASBD.mChannelsPerFrame = theFileFormat.mChannelsPerFrame;
	
	outputASBD.mFormatID = kAudioFormatLinearPCM;
	outputASBD.mBytesPerPacket = 2 * outputASBD.mChannelsPerFrame;
	outputASBD.mFramesPerPacket = 1;
	outputASBD.mBytesPerFrame = 2 * outputASBD.mChannelsPerFrame;
	outputASBD.mBitsPerChannel = 16;
	outputASBD.mFormatFlags = kAudioFormatFlagsNativeEndian | kAudioFormatFlagIsPacked | kAudioFormatFlagIsSignedInteger;
	
	// Set the desired client (output) data format
	error = ExtAudioFileSetProperty(extRef, kExtAudioFileProperty_ClientDataFormat, sizeof(outputASBD), &outputASBD);
	if (error)
	{
		printf("ExtAudioFileSetProperty(kExtAudioFileProperty_ClientDataFormat) FAILED, Error = %ld", error);
		goto Exit;
	}
	
	// Get the total frame count
	thePropertySize = sizeof(audioFramesInFile);
	error = ExtAudioFileGetProperty(extRef, kExtAudioFileProperty_FileLengthFrames, &thePropertySize, &audioFramesInFile);
	if (error)
	{
		printf("ExtAudioFileGetProperty(kExtAudioFileProperty_FileLengthFrames) FAILED, Error = %ld", error);
		goto Exit;
	}
	
	// Read all the data into memory
	UInt32 dataSize = audioFramesInFile * outputASBD.mBytesPerFrame;
	data = malloc(dataSize);
	if (data)
	{
		AudioBufferList dataBufferList;
		dataBufferList.mNumberBuffers = 1;
		dataBufferList.mBuffers[0].mDataByteSize = dataSize;
		dataBufferList.mBuffers[0].mNumberChannels = outputASBD.mChannelsPerFrame;
		dataBufferList.mBuffers[0].mData = data;
		
		// Read the data into an AudioBufferList
		error = ExtAudioFileRead(extRef, (UInt32*)&audioFramesInFile, &dataBufferList);
		if (error == noErr)
		{
			// success
			*outDataSize = (ALsizei)dataSize;
			*outDataFormat = (outputASBD.mChannelsPerFrame > 1) ? AL_FORMAT_STEREO16 : AL_FORMAT_MONO16;
			*outSampleRate = (ALsizei)outputASBD.mSampleRate;
		}
		else
		{
			// failure
			free(data);
			data = NULL; // make sure to return NULL
			printf("ExtAudioFileRead FAILED, Error = %ld", error); goto Exit;
		}
	}
	
Exit:
	// Dispose the ExtAudioFileRef, it is no longer needed
	if (extRef) ExtAudioFileDispose(extRef);
	return data;
}
