//
//  OpenALSupport.h
//  sdktester
//
//  Created by Auston Stewart on 9/17/13.
//  Copyright (c) 2013 Twitch. All rights reserved.
//

#ifndef sdktester_openalsupport_h
#define sdktester_openalsupport_h

#import <OpenAL/al.h>
#import <OpenAL/alc.h>
#import <OpenAL/oalMacOSX_OALExtensions.h>
#import <AudioToolbox/AudioToolbox.h>
#import <AudioToolbox/ExtendedAudioFile.h>

ALvoid alBufferDataStaticProc(const ALint bid, ALenum format, ALvoid* data, ALsizei size, ALsizei freq);
ALvoid alcOutputCapturerPrepareProc(ALCuint frequency, ALCenum format, ALCsizei maxsamplecount);
ALvoid alcOutputCapturerStartProc();
ALvoid alcOutputCapturerStopProc();
ALint alcOutputCapturerAvailableSamplesProc();
ALvoid alcOutputCapturerSamplesProc(ALCvoid *buffer, ALCsizei samplecount);
void *alAudioDataForFile(CFURLRef inFileURL, ALsizei *outDataSize, ALenum *outDataFormat, ALsizei *outSampleRate);

#endif
