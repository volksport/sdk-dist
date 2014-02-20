#include <cassert>
#include <cstdint>
#include <cstring>

#if TTV_PLATFORM_WINDOWS
#   pragma comment(lib, "libx264.lib")
#endif

extern "C"
{
#ifdef TTV_PLATFORM_WINDOWS
#pragma warning (push,0)
#endif

#include "x264.h"

#ifdef TTV_PLATFORM_WINDOWS
#pragma warning pop
#endif
}

#include "x264plugin.h"

//--------------------------------------------------------------------------
static const char* GetX264Preset(TTV_EncodingCpuUsage encodingCpuUsage)
{

	switch(encodingCpuUsage)
	{
	case TTV_ECU_HIGH:
		return "slower";
	case TTV_ECU_MEDIUM:
		return "medium";
	case TTV_ECU_LOW:
	default:
		return "ultrafast";
	}
}


//--------------------------------------------------------------------------
X264Plugin::X264Plugin() 
: mOutputWidth(0)
, mOutputHeight(0)
, mX264Encoder(0)
{

}

//--------------------------------------------------------------------------
TTV_ErrorCode X264Plugin::Start(const TTV_VideoParams* videoParams)
{
	assert(videoParams);

	mOutputWidth = videoParams->outputWidth;
	mOutputHeight = videoParams->outputHeight;

	x264_param_t param;
	const char* preset = GetX264Preset(videoParams->encodingCpuUsage);

	auto ret = x264_param_default_preset(&param, preset, nullptr);
	assert (ret==0);
	if (ret != 0)
	{
		return TTV_EC_X264_INVALID_PRESET;
	}

	param.i_threads = X264_THREADS_AUTO;
	param.i_width = mOutputWidth;
	param.i_height = mOutputHeight;

	// TODO - these params should be probably passed in
	param.analyse.i_subpel_refine = 7;
	param.rc.i_vbv_max_bitrate = videoParams->maxKbps;
	param.rc.i_vbv_buffer_size = param.rc.i_vbv_max_bitrate * 2;
	param.i_keyint_min = 90;
	param.i_keyint_max = static_cast<int>(videoParams->targetFps) * 5;

	param.b_vfr_input = 1;
	param.i_timebase_num = 1;
	param.i_timebase_den = 1000;

	param.rc.i_rc_method = X264_RC_ABR;
	param.rc.i_bitrate = videoParams->maxKbps;

	ret = x264_param_apply_profile(&param, "baseline");
	assert (ret == 0);
	if (ret != 0)
	{
		return TTV_EC_X264_INVALID_PRESET;
	}

	mX264Encoder = x264_encoder_open(&param);
	assert(mX264Encoder);
	return mX264Encoder ? TTV_EC_SUCCESS : TTV_EC_UNKNOWN_ERROR;
}

//--------------------------------------------------------------------------
TTV_ErrorCode X264Plugin::GetSpsPps(ITTVBuffer* outSps, ITTVBuffer* outPps)
{
	x264_nal_t* headers = nullptr;
	int nals = 0;
	int ret = x264_encoder_headers(mX264Encoder, &headers, &nals);
	if (ret < 0)
	{
		//ttv::trace::Message("X264Encoder", TTV_ML_ERROR, "Inside X264Encoder::GetSpsPps - No SpsPps");
		return TTV_EC_NO_SPSPPS;
	}

	assert(nals >= 2);		// must have both sps and pps	

	outSps->Append(headers[0].p_payload, headers[0].i_payload);
	outPps->Append(headers[1].p_payload, headers[1].i_payload);

	return TTV_EC_SUCCESS;
}

//--------------------------------------------------------------------------
TTV_ErrorCode X264Plugin::EncodeFrame(const EncodeInput& input, EncodeOutput& output)
{
	x264_picture_t x264InputFrame = {};
	x264_picture_t* pInputFrame = nullptr;
	
	if (input.source)
	{
		// Set up the input frame to feed to X264
		//
		x264_image_t x264InputImg;
		
		x264InputImg.i_csp = X264_CSP_NV12;
		x264InputImg.i_plane = 2;
		x264InputImg.i_stride[0] = mOutputWidth;
		x264InputImg.i_stride[1] = mOutputWidth;
		x264InputImg.i_stride[2] = 0;
		x264InputImg.i_stride[3] = 0;
		
		x264InputImg.plane[0] = const_cast<uint8_t*> (input.yuvPlanes[0]);
		x264InputImg.plane[1] = const_cast<uint8_t*> (input.yuvPlanes[1]);
		x264InputImg.plane[2] = nullptr;
		x264InputImg.plane[3] = nullptr;
		
		x264InputFrame.img = x264InputImg;
		
		// Set the frame PTS for VFR
		x264InputFrame.i_pts = input.timeStamp;
		pInputFrame = &x264InputFrame;
	}
	else
	{
		if (x264_encoder_delayed_frames(mX264Encoder) <= 0)
		{
			return TTV_WRN_NOMOREDATA;
		}
	}
	
	x264_nal_t* nals = nullptr;
	int nalCount = 0;
	x264_picture_t x264OutputFrame;
	int nalRet = x264_encoder_encode(mX264Encoder, &nals, &nalCount, pInputFrame, &x264OutputFrame);
	
	if (nalRet > 0 && nalCount > 0)
	{
		output.frameTimeStamp = x264OutputFrame.i_pts;
		output.isKeyFrame = x264OutputFrame.b_keyframe != 0;
		//output.frameData->Resize(nalRet);
		for (int i = 0; i < nalCount; ++i)
		{
			output.frameData->Append(nals[i].p_payload, nals[i].i_payload);
		}
		return TTV_EC_SUCCESS;
	}
	
	return TTV_WRN_NOMOREDATA;
}
