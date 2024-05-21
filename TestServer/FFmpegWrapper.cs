using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FFmpegAnalyzer
{
    public class FFmpegWrapper
    {
        /// <summary>
        /// 默认的编码格式
        /// </summary>
        public AVCodecID DefaultCodecFormat { get; set; } = AVCodecID.AV_CODEC_ID_H264;

        /// <summary>
        /// 注册FFmpeg
        /// </summary>
        public static void RegisterFFmpeg()
        {
            FFmpegHelper.RegisterFFmpegBinaries();

            // // 初始化注册ffmpeg相关的编码器
            // ffmpeg.av_register_all();
            // ffmpeg.avcodec_register_all();
            ffmpeg.avformat_network_init();
            RegisterFFmpegLogger();
        }

        /// <summary>
        /// 注册日志
        /// <exception cref="NotSupportedException">.NET Framework 不支持日志注册</exception>
        /// </summary>
        private static unsafe void RegisterFFmpegLogger()
        {
            // 设置记录ffmpeg日志级别
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr)lineBuffer);
                Console.Write(line);
            };
            ffmpeg.av_log_set_callback(logCallback);
        }

        #region 编码器

        /// <summary>
        /// 创建编码器
        /// </summary>
        /// <param name="frameSize">编码前一帧原始数据的大小</param>
        /// <param name="isRgb">rgb数据</param>
        public void CreateEncoder(Size frameSize, bool isRgb = true)
        {
            _fFmpegEncoder = new FFmpegEncoder(frameSize, isRgb);
            _fFmpegEncoder.CreateEncoder(DefaultCodecFormat);
        }

        /// <summary>
        /// 编码
        /// </summary>
        /// <param name="frameBytes">编码帧数据</param>
        /// <returns></returns>
        public byte[] EncodeFrames(byte[] frameBytes)
        {
            return _fFmpegEncoder.EncodeFrames(frameBytes);
        }

        /// <summary>
        /// 释放编码器
        /// </summary>
        public void DisposeEncoder()
        {
            _fFmpegEncoder.Dispose();
        }
        #endregion

        #region 解码器

        /// <summary>
        /// 创建解码器
        /// </summary>
        /// <param name="decodedFrameSize">解码后数据的大小</param>
        /// <param name="isRgb">Rgb数据</param>
        public void CreateDecoder(Size decodedFrameSize, bool isRgb = true)
        {
            _fFmpegDecoder = new FFmpegDecoder(decodedFrameSize, isRgb);
            _fFmpegDecoder.CreateDecoder(DefaultCodecFormat);
        }

        /// <summary>
        /// 解码
        /// </summary>
        /// <param name="frameBytes">解码帧数据</param>
        /// <returns></returns>
        public Tuple<AVFrame, byte[]> DecodeFrames(byte[] frameBytes)
        {
            return _fFmpegDecoder.DecodeFrames(frameBytes);
        }

        /// <summary>
        /// 释放解码器
        /// </summary>
        public void DisposeDecoder()
        {
            _fFmpegDecoder.Dispose();
        }
        #endregion

        /// <summary>编码器</summary>
        private FFmpegEncoder _fFmpegEncoder;

        /// <summary>解码器</summary>
        private FFmpegDecoder _fFmpegDecoder;
    }
}