﻿using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FFmpegAnalyzer
{
    public enum AV_BUFFERSRC_FLAG
    {
        NO_CHECK_FORMAT = 1,
        PUSH = 4,
        KEEP_REF = 8,
    }
    public static class FFmpegHelper
    {
        public static void RegisterFFmpegBinaries()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var current = Environment.CurrentDirectory;
                var probe = Path.Combine("ffmpeg", "bin");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);

                    if (Directory.Exists(ffmpegBinaryPath))
                    {
                        Console.WriteLine($"FFmpeg binaries found in: {ffmpegBinaryPath}");
                        ffmpeg.RootPath = ffmpegBinaryPath;
                        
                        #if DEBUG
                        Console.WriteLine("Current directory: " + Environment.CurrentDirectory);
                        Console.WriteLine("Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32");
                        Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");
                        Console.WriteLine($"LIBAVFORMAT Version: {ffmpeg.LIBAVFORMAT_VERSION_MAJOR}.{ffmpeg.LIBAVFORMAT_VERSION_MINOR}");
                        #endif
                        
                        return;
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                ffmpeg.RootPath = "/lib/x86_64-linux-gnu/";
            else
                throw new NotSupportedException(); // fell free add support for platform of your choose
                
        }

        /// <summary>
        /// 配置日志
        /// </summary>
        public static unsafe void SetupLogging()
        {
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_VERBOSE);

            // do not convert to local function
            av_log_set_callback_callback logCallback = (p0, level, format, vl) =>
            {
                if (level > ffmpeg.av_log_get_level()) return;

                var lineSize = 1024;
                var lineBuffer = stackalloc byte[lineSize];
                var printPrefix = 1;
                ffmpeg.av_log_format_line(p0, level, format, vl, lineBuffer, lineSize, &printPrefix);
                var line = Marshal.PtrToStringAnsi((IntPtr) lineBuffer);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write(line);
                Console.ResetColor();
            };

            ffmpeg.av_log_set_callback(logCallback);
        }

        /// <summary>
        /// 配置硬件解码器
        /// </summary>
        /// <param name="HWtype"></param>
        public static void ConfigureHWDecoder(out AVHWDeviceType HWtype)
        {
            HWtype = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
            Console.WriteLine("Use hardware acceleration for decoding?[n]");
            var key = Console.ReadLine();
            var availableHWDecoders = new Dictionary<int, AVHWDeviceType>();
            if (key == "y")
            {
                Console.WriteLine("Select hardware decoder:");
                var type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;
                var number = 0;
                while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
                {
                    Console.WriteLine($"{++number}. {type}");
                    availableHWDecoders.Add(number, type);
                }
                if (availableHWDecoders.Count == 0)
                {
                    Console.WriteLine("Your system have no hardware decoders.");
                    HWtype = 0;
                    return;
                }
                int decoderNumber = availableHWDecoders.SingleOrDefault(t => t.Value == AVHWDeviceType.AV_HWDEVICE_TYPE_DXVA2).Key;
                if (decoderNumber == 0)
                    decoderNumber = availableHWDecoders.First().Key;
                Console.WriteLine($"Selected [{decoderNumber}]");
                int.TryParse(Console.ReadLine(),out var inputDecoderNumber);
                availableHWDecoders.TryGetValue(inputDecoderNumber == 0 ? decoderNumber: inputDecoderNumber, out HWtype);
            }
        }
        
        const int INBUF_SIZE = 800000;
        public static unsafe void video_decode_example(string filename, AVCodecID codec_id, string outfileDirPath)
        {
            video_decode_stream(new FileStream(filename, FileMode.Open), codec_id, outfileDirPath);
        }
        public static unsafe void video_decode_stream(Stream stream, AVCodecID codec_id, string outfileDirPath)
        {
            AVCodec* _pCodec = null;
            AVCodecParserContext* _parser = null;
            AVCodecContext* _pCodecContext = null;
            AVFrame* frame = null;
            AVPacket* pkt = null;
            byte[] inbuf = new byte[INBUF_SIZE + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE];
            int ret = 0;
            stream.Position = 0;

            using BinaryReader inputFile = new BinaryReader(stream);

            do
            {
                pkt = ffmpeg.av_packet_alloc();
                if (pkt == null)
                {
                    break;
                }

                /* find the MPEG-1 video decoder */
                Console.WriteLine($"Decode video stream");
                _pCodec = ffmpeg.avcodec_find_decoder(codec_id);

                if (_pCodec == null)
                {
                    Console.WriteLine($"Codec not found: {codec_id}");
                    break;
                }

                _parser = ffmpeg.av_parser_init((int)_pCodec->id);


                _pCodecContext = ffmpeg.avcodec_alloc_context3(_pCodec);
                if (_pCodecContext == null)
                {
                    Console.WriteLine($"Could not allocate video codec context: {codec_id}");
                    break;
                }

                bool headerRead = false;

                /* open it */
                if (ffmpeg.avcodec_open2(_pCodecContext, _pCodec, null) < 0)
                {
                    Console.WriteLine("Could not open codec");
                    break;
                }

                frame = ffmpeg.av_frame_alloc();

                if (frame == null)
                {
                    Console.WriteLine("Could not allocate video frame");
                    break;
                }

                bool parse_succeed = true;

                while (parse_succeed)
                {
                    int data_size = inputFile.Read(inbuf, 0, INBUF_SIZE);

                    if (data_size == 0)
                    {
                        break;
                    }

                    fixed (byte* ptr = inbuf)
                    {
                        byte* data = ptr;

                        while (data_size > 0)
                        {
                            ret = ffmpeg.av_parser_parse2(_parser, _pCodecContext,
                                &pkt->data, &pkt->size, data, data_size, ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);

                            if (ret < 0)
                            {
                                break;
                            }

                            if (headerRead == false && _pCodecContext->pix_fmt != AVPixelFormat.AV_PIX_FMT_NONE)
                            {
                                Console.WriteLine();
                                Console.WriteLine($"width: {_pCodecContext->width}");
                                Console.WriteLine($"height: {_pCodecContext->height}");
                                Console.WriteLine($"time_base num: {_pCodecContext->time_base.num}, den: {_pCodecContext->time_base.den}");
                                Console.WriteLine($"framerate num: {_pCodecContext->framerate.num}, den: {_pCodecContext->framerate.den}");
                                Console.WriteLine($"gop_size: {_pCodecContext->gop_size}");
                                Console.WriteLine($"max_b_frames: {_pCodecContext->max_b_frames}");
                                Console.WriteLine($"pix_fmt: {_pCodecContext->pix_fmt}");
                                Console.WriteLine($"bit_rate: {_pCodecContext->bit_rate}");
                                Console.WriteLine();
                                headerRead = true;
                            }

                            data += ret;
                            data_size -= ret;

                            if (pkt->size != 0)
                            {
                                parse_succeed = decode(_pCodecContext, frame, pkt, outfileDirPath);
                                if (parse_succeed == false)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                // flush the decoder
                decode(_pCodecContext, frame, null, outfileDirPath);

            } while (false);

            if (_parser != null)
            {
                ffmpeg.av_parser_close(_parser);
            }

            if (_pCodecContext != null)
            {
                ffmpeg.avcodec_free_context(&_pCodecContext);
            }

            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }

            if (pkt != null)
            {
                ffmpeg.av_packet_free(&pkt);
            }
        }

        private static unsafe bool decode(AVCodecContext* pCodecContext, AVFrame* frame, AVPacket* pkt, string outfileDirPath)
        {
            int ret = ffmpeg.avcodec_send_packet(pCodecContext, pkt);
            if (ret < 0)
            {
                Console.WriteLine("Error sending a packet for decoding");
                return false;
            }

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_frame(pCodecContext, frame);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    return true;
                }
                else if (ret < 0)
                {
                    Console.WriteLine("Error during decoding");
                    return false;
                }

                Console.WriteLine($"saving frame {pCodecContext->frame_num}");

                string outputFile = Path.Combine(outfileDirPath, "noname_" + pCodecContext->frame_num + ".pgm");
                //string outputFile = Path.Combine(outfileDirPath, DateTime.Now.Ticks + ".pgm");
                pgm_save(frame->data[0], frame->linesize[0], frame->width, frame->height, outputFile);
            }

            return true;
        }

        public static AVRational AV_TIME_BASE_Q
        {
            get
            {
                return new AVRational { num = 1, den = ffmpeg.AV_TIME_BASE };
            }
        }

        // 2022-03-15 - cdba98bb80 - lavc 59.24.100 - avcodec.h codec_par.h
        //   Update AVCodecContext for the new channel layout API: add ch_layout,
        //   deprecate channels/channel_layout
        public static unsafe int av_channel_layout_copy(/* AVChannelLayout *dst, AVChannelLayout* src */)
        {
            throw new NotImplementedException();
        }

        public static AVRational av_inv_q(AVRational ts)
        {
            return new AVRational { num = ts.den, den = ts.num };
        }

        internal static string av_ts2str(long ts)
        {
            if (ts == ffmpeg.AV_NOPTS_VALUE)
            {
                return "NOPTS";
            }
            else
            {
                return ts.ToString();
            }
        }

        public static unsafe double av_q2d(AVRational* ar)
        {
            return (ar->num / (double)ar->den);
        }

        public static unsafe string av_ts2timestr(long pts, in AVRational av)
        {
            fixed (AVRational* pav = &av)
            {
                return (av_q2d(pav) * pts).ToString();
            }
        }

        public static unsafe string av_ts2timestr(long pts, AVRational* av)
        {
            if (pts == 0)
            {
                return "NOPTS";
            }

            return (av_q2d(av) * pts).ToString("G6");
        }

        public static unsafe string av_err2str(int error)
        {
            return av_strerror(error);
        }

        public static unsafe string av_strerror(int error)
        {
            var bufferSize = 1024;
            var buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
            var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
            return message ?? "";
        }

        public static int ThrowExceptionIfError(this int error)
        {
            if (error < 0) throw new ApplicationException(av_strerror(error));
            return error;
        }

        public static unsafe string AnsiToString(byte* ptr)
        {
            return Marshal.PtrToStringAnsi(new IntPtr(ptr)) ?? "";
        }

        // PGM File Viewer (browser-based)
        // ; https://smallpond.ca/jim/photomicrography/pgmViewer/index.html
        public static unsafe void pgm_save(byte* buf, int wrap, int xsize, int ysize, string filename)
        {
            using FileStream fs = new FileStream(filename, FileMode.Create);

            byte[] header = Encoding.ASCII.GetBytes($"P5\n{xsize} {ysize}\n255\n");
            fs.Write(header);

            // C# - byte * (바이트 포인터)를 FileStream으로 쓰는 방법
            // https://www.sysnet.pe.kr/2/0/12913
            for (int i = 0; i < ysize; i++)
            {
                byte* ptr = buf + (i * wrap);
                ReadOnlySpan<byte> pos = new Span<byte>(ptr, xsize);

                fs.Write(pos);
            }
        }

        public static void PrintHwDevices()
        {
            AVHWDeviceType type = AVHWDeviceType.AV_HWDEVICE_TYPE_NONE;

            while ((type = ffmpeg.av_hwdevice_iterate_types(type)) != AVHWDeviceType.AV_HWDEVICE_TYPE_NONE)
            {
                Console.WriteLine($"{ffmpeg.av_hwdevice_get_type_name(type)}");
            }
        }

        public static unsafe void PrintCodecs()
        {
            const int AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX = 0x01;

            void* iter = null;

            for (; ; )
            {
                AVCodec* cur = ffmpeg.av_codec_iterate(&iter);
                if (cur == null)
                {
                    break;
                }

                Console.WriteLine($"{AnsiToString(cur->name)}({AnsiToString(cur->long_name)})");

                AVCodecHWConfig* config = null;
                for (int n = 0; (config = ffmpeg.avcodec_get_hw_config(cur, n)) != null; n++)
                {

                    if ((config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) == AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX)
                    {
                        Console.WriteLine($"\thw-accel: {config->pix_fmt}, type: {config->device_type}, decoder: {ffmpeg.av_codec_is_decoder(cur)}, encoder: {ffmpeg.av_codec_is_encoder(cur)}");
                    }
                }
            }
        }

        public static int get_format_from_sample_fmt(out string fmt, AVSampleFormat sample_fmt)
        {
            fmt = "";

            foreach (var item in sample_fmt_entry.entries)
            {
                if (item.sample_fmt == sample_fmt)
                {
                    fmt = (BitConverter.IsLittleEndian) ? item.fmt_le : item.fmt_be;
                    return 0;
                }
            }

            return ffmpeg.AVERROR(ffmpeg.EINVAL);
        }

        public class sample_fmt_entry
        {
            public AVSampleFormat sample_fmt;
            public string fmt_be = "";
            public string fmt_le = "";

            public static sample_fmt_entry[] entries = new sample_fmt_entry[]
            {
            new sample_fmt_entry { sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_U8, fmt_be = "u8", fmt_le = "u8" },
            new sample_fmt_entry { sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16, fmt_be = "s16be", fmt_le = "s16le" },
            new sample_fmt_entry { sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S32, fmt_be = "s32be", fmt_le = "s32le" },
            new sample_fmt_entry { sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_FLT, fmt_be = "f32be", fmt_le = "f32le" },
            new sample_fmt_entry { sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_DBL, fmt_be = "f64be", fmt_le = "f64le" },
            };
        }
    }
}
