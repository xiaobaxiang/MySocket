using System;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using SkiaSharp;

namespace FFmpegAnalyzer
{
    /// <summary>
    /// 解码器
    /// </summary>
    internal unsafe class FFmpegDecoder
    {


        /// <param name="decodedFrameSize">解码后数据的大小</param>
        /// <param name="isRgb">Rgb数据</param>
        public FFmpegDecoder(SKImageInfo decodedFrameSize, bool isRgb = true)
        {
            _decodedFrameSize = decodedFrameSize;
            _isRgb = isRgb;

        }

        /// <summary>
        /// 创建解码器
        /// </summary>
        /// <param name="codecFormat">解码格式</param>
        public void CreateDecoder(AVCodecID codecFormat)
        {
            var originPixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
            var destinationPixelFormat = _isRgb ? AVPixelFormat.AV_PIX_FMT_RGB24 : AVPixelFormat.AV_PIX_FMT_YUV420P;

            //获取解码器
            _pDecodec = ffmpeg.avcodec_find_decoder(codecFormat);
            if (_pDecodec == null) throw new InvalidOperationException("Codec not found.");

            _pDecodecContext = ffmpeg.avcodec_alloc_context3(_pDecodec);
            _pDecodecContext->width = _decodedFrameSize.Width;
            _pDecodecContext->height = _decodedFrameSize.Height;
            _pDecodecContext->time_base = new AVRational { num = 1, den = 30 };
            _pDecodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            _pDecodecContext->framerate = new AVRational { num = 30, den = 1 };
            _pDecodecContext->gop_size = 30;
            // 设置预测算法
            _pDecodecContext->flags |= ffmpeg.AV_CODEC_FLAG_PSNR;
            _pDecodecContext->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            _pDecodecContext->max_b_frames = 0;
            //ffmpeg.av_opt_set(_pDecodecContext->priv_data, "strict", "1", 0);
            ffmpeg.av_opt_set(_pDecodecContext->priv_data, "preset", "veryfast", 0);
            ffmpeg.av_opt_set(_pDecodecContext->priv_data, "tune", "zerolatency", 0);
            //打开解码器
            ffmpeg.avcodec_open2(_pDecodecContext, _pDecodec, null);
            _pConvertContext = ffmpeg.sws_getContext(
                _decodedFrameSize.Width,
                _decodedFrameSize.Height,
                originPixelFormat,
                _decodedFrameSize.Width,
                _decodedFrameSize.Height,
                destinationPixelFormat,
               ffmpeg.SWS_FAST_BILINEAR,
                null, null, null);
            if (_pConvertContext == null)
                throw new ApplicationException("Could not initialize the conversion context.");

            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat, _decodedFrameSize.Width, _decodedFrameSize.Height, 1);
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);
            _dstData = new byte_ptrArray4();
            _dstLineSize = new int_array4();

            ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLineSize, (byte*)_convertedFrameBufferPtr, destinationPixelFormat,
                _decodedFrameSize.Width, _decodedFrameSize.Height, 1);
            _isCodecRunning = true;

        }

        int count = -1;

        /// <summary>
        /// 解码
        /// </summary>
        /// <param name="frameBytes"></param>
        /// <returns></returns>
        public Tuple<AVFrame, byte[]> DecodeFrames(byte[] frameBytes)
        {
            if (!_isCodecRunning)
            {
                throw new InvalidOperationException("解码器未运行!");
            }


            var waitDecodePacket = ffmpeg.av_packet_alloc();
            var waitDecoderFrame = ffmpeg.av_frame_alloc();
            ffmpeg.av_frame_unref(waitDecoderFrame);


            fixed (byte* waitDecodeData = frameBytes)
            {
                //Console.WriteLine("decode 1");
                waitDecodePacket->data = waitDecodeData;
                waitDecodePacket->size = frameBytes.Length;
                waitDecodePacket->pts = ffmpeg.av_rescale_q(count++, new AVRational { num = 1, den = 30 }, new AVRational { num = 1, den = 30 }); ;
                waitDecodePacket->dts = waitDecodePacket->pts;
                ffmpeg.av_frame_unref(waitDecoderFrame);
                try
                {
                    int error;
                    do
                    {
                        ffmpeg.avcodec_send_packet(_pDecodecContext, waitDecodePacket);
                        //Console.WriteLine("decode 2");
                        error = ffmpeg.avcodec_receive_frame(_pDecodecContext, waitDecoderFrame);

                        //Console.WriteLine("decode 3");

                        if (error < 0) return null;
                        //RtmpPusher.publishFile();
                        //_pusher.PushFrame(waitDecoderFrame);
                    } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));
                }
                finally
                {
                    ffmpeg.av_packet_unref(waitDecodePacket);
                }

                var decodeAfterFrame = ConvertToRgb(waitDecoderFrame);
                //Console.WriteLine("decode 4");
                var length = _isRgb
                    ? decodeAfterFrame.height * decodeAfterFrame.width * 3
                    : Convert.ToInt32(Math.Floor(decodeAfterFrame.height * decodeAfterFrame.width * 1.5));

                byte[] buffer = new byte[length];
                Marshal.Copy((IntPtr)decodeAfterFrame.data[0], buffer, 0, buffer.Length);

                return new Tuple<AVFrame, byte[]>(decodeAfterFrame, buffer);
            }
        }


        /// <summary>
        /// 释放
        /// </summary>
        public void Dispose()
        {
            _isCodecRunning = false;
            //释放解码器
            //ffmpeg.avcodec_close(_pDecodecContext);
            fixed (AVCodecContext** ptrDecodecContext = &_pDecodecContext)
            {
                ffmpeg.avcodec_free_context(ptrDecodecContext);
            }
            ffmpeg.av_free(_pDecodecContext);
            //释放转换器
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            ffmpeg.sws_freeContext(_pConvertContext);
        }

        /// <summary>
        /// 转换成Rgb
        /// </summary>
        /// <param name="waitDecoderFrame"></param>
        /// <returns></returns>
        private AVFrame ConvertToRgb(AVFrame* waitDecoderFrame)
        {
            ffmpeg.sws_scale(_pConvertContext, waitDecoderFrame->data, waitDecoderFrame->linesize, 0, waitDecoderFrame->height, _dstData, _dstLineSize);
            var decodeAfterData = new byte_ptrArray8();
            decodeAfterData.UpdateFrom(_dstData);
            var lineSize = new int_array8();
            lineSize.UpdateFrom(_dstLineSize);

            ffmpeg.av_frame_unref(waitDecoderFrame);
            return new AVFrame
            {
                data = decodeAfterData,
                linesize = lineSize,
                width = _decodedFrameSize.Width,
                height = _decodedFrameSize.Height,
                pts = waitDecoderFrame->pts,
                pkt_dts = waitDecoderFrame->pkt_dts
            };
        }

        //解码器
        private AVCodec* _pDecodec;
        private AVCodecContext* _pDecodecContext;
        //private RtmpStreamer _pusher;
        //转换缓存区
        private IntPtr _convertedFrameBufferPtr;
        private byte_ptrArray4 _dstData;
        private int_array4 _dstLineSize;
        //格式转换
        private SwsContext* _pConvertContext;
        private SKImageInfo _decodedFrameSize;
        private readonly bool _isRgb;
        //解码器正在运行
        private bool _isCodecRunning;

        //private AVFormatContext* _outputContext;
    }
}