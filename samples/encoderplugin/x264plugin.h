#include "twitchinterfaces.h"

struct x264_t;

class X264Plugin: public ITTVPluginVideoEncoder
{
public:
	X264Plugin();

	TTV_ErrorCode Start(const TTV_VideoParams* videoParams) override;
	TTV_ErrorCode GetSpsPps(ITTVBuffer* outSps, ITTVBuffer* outPps) override;
	TTV_ErrorCode EncodeFrame(const EncodeInput& input, EncodeOutput& output) override;

	TTV_YUVFormat GetRequiredYUVFormat() const override { return TTV_YUV_NV12; }
private:
	uint mOutputWidth;
	uint mOutputHeight;
	x264_t* mX264Encoder;

};