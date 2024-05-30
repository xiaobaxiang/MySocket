using FFmpeg.AutoGen;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestServer
{
    public unsafe class YUVDecoder
    {
        private AVCodecContext* codecContext;
        private AVFrame* frame;
        private SwsContext* swsContext;

        public YUVDecoder(int width, int height)
        {

            // 查找 YUV 解码器
            AVCodec* codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            codecContext = ffmpeg.avcodec_alloc_context3(codec);
            ffmpeg.avcodec_open2(codecContext, codec, null);

            // 创建 AVFrame 来存储解码后的图像
            frame = ffmpeg.av_frame_alloc();
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;
            ffmpeg.av_frame_get_buffer(frame, 32);

            // 初始化 SwsContext
            swsContext = ffmpeg.sws_getContext(width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                                                width, height, AVPixelFormat.AV_PIX_FMT_BGR24,
                                                ffmpeg.SWS_BILINEAR, null, null, null);
        }

    }
}
