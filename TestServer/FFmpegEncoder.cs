using System;
using System.Runtime.InteropServices;
using System.Drawing;
using FFmpeg.AutoGen;

namespace FFmpegAnalyzer
{
    /// <summary>
    /// 编码器
    /// </summary>
    internal unsafe class FFmpegEncoder
    {
        /// <param name="frameSize">编码前一帧原始数据的大小</param>
        /// <param name="isRgb">rgb数据</param>
        public FFmpegEncoder(Size frameSize, bool isRgb = true)
        {
            _frameSize = frameSize;
            _isRgb = isRgb;
            _rowPitch = isRgb ? _frameSize.Width * 3 : _frameSize.Width * 4;
        }

        /// <summary>
        /// 创建编码器
        /// </summary>
        public  void CreateEncoder(AVCodecID codecFormat)
        {
            var originPixelFormat = _isRgb ? AVPixelFormat.AV_PIX_FMT_RGB24 : AVPixelFormat.AV_PIX_FMT_BGRA;
            var destinationPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _pCodec = ffmpeg.avcodec_find_encoder(codecFormat);
             
            if (_pCodec == null)
                throw new InvalidOperationException("Codec not found.");
            _pCodecContext = ffmpeg.avcodec_alloc_context3(_pCodec);
            _pCodecContext->width = _frameSize.Width;
            _pCodecContext->height = _frameSize.Height;

            _pCodecContext->framerate = new AVRational { num = 30, den = 1 };
            _pCodecContext->time_base = new AVRational {num = 1, den = 30};
            _pCodecContext->gop_size = 30;
            _pCodecContext->pix_fmt = destinationPixelFormat;
            // 设置预测算法
            _pCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_PSNR;
            _pCodecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            _pCodecContext->max_b_frames = 0;

            ffmpeg.av_opt_set(_pCodecContext->priv_data, "preset", "veryfast", 0);
            ffmpeg.av_opt_set(_pCodecContext->priv_data, "tune", "zerolatency", 0);

            //打开编码器
            ffmpeg.avcodec_open2(_pCodecContext, _pCodec, null);
            _pConvertContext = ffmpeg.sws_getContext(_frameSize.Width, _frameSize.Height, originPixelFormat, _frameSize.Width, _frameSize.Height, destinationPixelFormat,
            ffmpeg.SWS_FAST_BILINEAR, null, null, null);
            if (_pConvertContext == null)
                throw new ApplicationException("Could not initialize the conversion context.");

            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, _frameSize.Width, _frameSize.Height, 1);
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            _dstData = new byte_ptrArray4();
            _dstLineSize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLineSize, (byte*)_convertedFrameBufferPtr, destinationPixelFormat, _frameSize.Width, _frameSize.Height, 1);
            _isCodecRunning = true;
        }

        /// <summary>
        /// 释放
        /// </summary>
        public  void Dispose()
        {
            if (!_isCodecRunning) return;
            _isCodecRunning = false;
            //释放编码器
            //ffmpeg.avcodec_close(_pCodecContext);
            fixed (AVCodecContext** ptrCodecContext = &_pCodecContext)
            {
                ffmpeg.avcodec_free_context(ptrCodecContext);
            }
            ffmpeg.av_free(_pCodecContext);
            //释放转换器
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            ffmpeg.sws_freeContext(_pConvertContext);
        }

        /// <summary>
        /// 编码
        /// </summary>
        /// <param name="frameBytes"></param>
        /// <returns></returns>
        public  byte[] EncodeFrames(byte[] frameBytes)
        {
            if (!_isCodecRunning)
            {
                 throw new InvalidOperationException("编码器未运行!");
            }
            fixed (byte* pBitmapData = frameBytes)
            {
                var waitToYuvFrame = new AVFrame
                {
                    data = new byte_ptrArray8 { [0] = pBitmapData },
                    linesize = new int_array8 { [0] = _rowPitch },
                    height = _frameSize.Height
                };

                var rgbToYuv = ConvertToYuv(waitToYuvFrame, _frameSize.Width, _frameSize.Height);

                byte[] buffer;
                var pPacket = ffmpeg.av_packet_alloc();
                try
                {
                    int error;
                    do
                    {
                        ffmpeg.avcodec_send_frame(_pCodecContext, &rgbToYuv);
                        error = ffmpeg.avcodec_receive_packet(_pCodecContext, pPacket);
                    } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
                    buffer = new byte[pPacket->size];
                    Marshal.Copy(new IntPtr(pPacket->data), buffer, 0, pPacket->size);
                }
                finally
                {
                    ffmpeg.av_frame_unref(&rgbToYuv);
                    ffmpeg.av_packet_unref(pPacket);
                }

                return buffer;
            }
        }

        /// <summary>
        /// 转换成Yuv格式
        /// </summary>
        /// <param name="waitConvertYuvFrame"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        private  AVFrame ConvertToYuv(AVFrame waitConvertYuvFrame, int width, int height)
        {
            ffmpeg.sws_scale(_pConvertContext, waitConvertYuvFrame.data, waitConvertYuvFrame.linesize, 0, waitConvertYuvFrame.height, _dstData, _dstLineSize);

            var data = new byte_ptrArray8();
            data.UpdateFrom(_dstData);
            var lineSize = new int_array8();
            lineSize.UpdateFrom(_dstLineSize);
            ffmpeg.av_frame_unref(&waitConvertYuvFrame);
            return new AVFrame
            {
                data = data,
                linesize = lineSize,
                width = width,
                height = height
            };
        }

        //编码器
        private AVCodec* _pCodec;
        private AVCodecContext* _pCodecContext;
        //转换缓存区
        private IntPtr _convertedFrameBufferPtr;
        private byte_ptrArray4 _dstData;
        private int_array4 _dstLineSize;
        //格式转换
        private SwsContext* _pConvertContext;
        private Size _frameSize;
        private readonly int _rowPitch;
        private readonly bool _isRgb;

        //编码器正在运行
        private bool _isCodecRunning;
    }
}