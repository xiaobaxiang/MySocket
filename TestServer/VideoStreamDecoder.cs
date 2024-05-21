using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using FFmpeg.AutoGen;

namespace FFmpegAnalyzer
{
    public sealed unsafe class VideoStreamDecoder : IDisposable
    {
        private readonly AVCodecContext* _pCodecContext;
        private readonly AVFormatContext* _pFormatContext;
        private readonly int _streamIndex;
        //
        private readonly AVFrame* _pFrame;
        //
        private readonly AVFrame* _receivedFrame;
        private readonly AVPacket* _pPacket;
        /// <summary>
        /// 视频解码器
        /// </summary>
        /// <param name="url">视频流URL</param>
        /// <param name="HWDeviceType">硬件解码器类型（默认AVHWDeviceType.AV_HWDEVICE_TYPE_NONE）</param>
        public VideoStreamDecoder(string url, AVHWDeviceType HWDeviceType = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
        {
            //分配一个AVFormatContext
            _pFormatContext = ffmpeg.avformat_alloc_context();
            //分配一个AVFrame
            _receivedFrame = ffmpeg.av_frame_alloc();

            var pFormatContext = _pFormatContext;
            //将源音视频流传递给ffmpeg即ffmpeg打开源视频流
            ffmpeg.avformat_open_input(&pFormatContext, url, null, null);
            //获取音视频流信息
            ffmpeg.avformat_find_stream_info(_pFormatContext, null);
            AVCodec* codec = null;
            //在源里找到最佳的流，如果指定了解码器，则根据解码器寻找流，将解码器传递给codec
            _streamIndex = ffmpeg.av_find_best_stream(_pFormatContext, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0);
            //根据解码器分配一个AVCodecContext ，仅仅分配工具，还没有初始化。
            _pCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            //如果硬解码
            if (HWDeviceType != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                //根据硬件编码类型创建AVHWDeviceContext，存在AVFormatContext.hw_device_ctx (_pCodecContext->hw_device_ctx)
                ffmpeg.av_hwdevice_ctx_create(&_pCodecContext->hw_device_ctx, HWDeviceType, null, null, 0);
            }
            //将最佳流的格式参数传递给codecContext
            ffmpeg.avcodec_parameters_to_context(_pCodecContext, _pFormatContext->streams[_streamIndex]->codecpar);
            //根据codec初始化pCodecContext 。与_pCodecContext = ffmpeg.avcodec_alloc_context3(codec);对应
            ffmpeg.avcodec_open2(_pCodecContext, codec, null);

            CodecName = ffmpeg.avcodec_get_name(codec->id);
            FrameSize = new System.Drawing.Size(_pCodecContext->width, _pCodecContext->height);
            PixelFormat = _pCodecContext->pix_fmt;
            //分配AVPacket
            /* AVPacket用于存储压缩的数据，分别包括有音频压缩数据，视频压缩数据和字幕压缩数据。
                       它通常在解复用操作后存储压缩数据，然后作为输入传给解码器。或者由编码器输出然后传递给复用器。
                       对于视频压缩数据，一个AVPacket通常包括一个视频帧。对于音频压缩数据，可能包括几个压缩的音频帧。
             */
            _pPacket = ffmpeg.av_packet_alloc();

            //分配AVFrame
            /*AVFrame用于存储解码后的音频或者视频数据。
                    AVFrame必须通过av_frame_alloc进行分配，通过av_frame_free释放。
            */
            _pFrame = ffmpeg.av_frame_alloc();
        }

        public string CodecName { get; }
        public System.Drawing.Size FrameSize { get; }
        public AVPixelFormat PixelFormat { get; }

        public void Dispose()
        {
            ffmpeg.av_frame_unref(_pFrame);
            ffmpeg.av_free(_pFrame);

            ffmpeg.av_packet_unref(_pPacket);
            ffmpeg.av_free(_pPacket);

            //ffmpeg.avcodec_close(_pCodecContext);
            fixed (AVCodecContext** ptrCodecContext = &_pCodecContext)
            {
                ffmpeg.avcodec_free_context(ptrCodecContext);
            }
            var pFormatContext = _pFormatContext;
            ffmpeg.avformat_close_input(&pFormatContext);
        }

        /// <summary>
        /// 解码下一帧帧
        /// </summary>
        /// <param name="frame">参数返回解码后的帧</param>
        /// <returns></returns>
        public bool TryDecodeNextFrame(out AVFrame frame)
        {
            //取消帧的引用。帧将不会被任何资源引用
            ffmpeg.av_frame_unref(_pFrame);
            ffmpeg.av_frame_unref(_receivedFrame);
            int error;
            do
            {
                try
                {
                    #region 读取帧忽略无效帧
                    do
                    {
                        //读取无效帧
                        error = ffmpeg.av_read_frame(_pFormatContext, _pPacket);//根据pFormatContext读取帧，返回到Packet中
                        if (error == ffmpeg.AVERROR_EOF)//如果已经是影视片流末尾则返回
                        {
                            frame = *_pFrame;
                            return false;
                        }
                    } while (_pPacket->stream_index != _streamIndex); //忽略掉音视频流里面与有效流（初始化（构造函数）时标记的_streamIndex）不一致的流
                    #endregion

                    //将帧数据放入解码器
                    ffmpeg.avcodec_send_packet(_pCodecContext, _pPacket);  //将原始数据数据（_pPacket）作为输入提供给解码器（_pCodecContext）
                }
                finally
                {
                    //消除对_pPacket的引用
                    ffmpeg.av_packet_unref(_pPacket);
                }
                //读取解码器里解码（_pCodecContext）后的帧通过参数返回（_pFrame）
                error = ffmpeg.avcodec_receive_frame(_pCodecContext, _pFrame);

            } while (error == ffmpeg.AVERROR(ffmpeg.EAGAIN));//当返回值等于 EAGAIN(再试一次)，
            if (_pCodecContext->hw_device_ctx != null)//如果配置了硬件解码则调用硬件解码器解码
            {
                //将_pFrame通过硬件解码后放入_receivedFrame
                ffmpeg.av_hwframe_transfer_data(_receivedFrame, _pFrame, 0);
                frame = *_receivedFrame;
            }
            else
            {
                frame = *_pFrame;
            }
            return true;
        }

        /// <summary>
        /// 获取媒体TAG信息
        /// </summary>
        /// <returns></returns>
        public IReadOnlyDictionary<string, string> GetContextInfo()
        {
            AVDictionaryEntry* tag = null;
            var result = new Dictionary<string, string>();
            while ((tag = ffmpeg.av_dict_get(_pFormatContext->metadata, "", tag, ffmpeg.AV_DICT_IGNORE_SUFFIX)) != null)
            {
                var key = Marshal.PtrToStringAnsi((IntPtr)tag->key);
                var value = Marshal.PtrToStringAnsi((IntPtr)tag->value);
                result.Add(key, value);
            }

            return result;
        }
    }
}