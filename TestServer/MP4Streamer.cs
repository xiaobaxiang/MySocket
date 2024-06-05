using FFmpeg.AutoGen;
using System;
using System.Threading;

namespace FFmpegAnalyzer
{
    /*
                          _ooOoo_
                         o8888888o
                         88" . "88
                         (| -_- |)
                         O\  =  /O
                      ____/`---'\____
                    .'  \\|     |//  `.
                   /  \\|||  :  |||//  \
                  /  _||||| -:- |||||-  \
                  |   | \\\  -  /// |   |
                  | \_|  ''\---/''  |   |
                  \  .-\__  `-`  ___/-. /
                ___`. .'  /--.--\  `. . __
             ."" '<  `.___\_<|>_/___.'  >'"".
            | | :  `- \`.;`\ _ /`;.`/ - ` : | |
            \  \ `-.   \_ __\ /__ _/   .-` /  /
       ======`-.____`-.___\_____/___.-`____.-'======
                          `=---='
       ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
            三天三夜，取百家之所长，得到的一段代码             
                佛祖保佑       永无BUG
    */
    public unsafe class MP4Streamer : IDisposable
    {
        private AVStream* _outputStream = null;
        private AVFormatContext* _outputContext = null;
        private AVCodecContext* _videoCodecContext = null;
        private long framePts = 0;
        private const int RATE = 10;
        public MP4Streamer()
        {
            ffmpeg.avdevice_register_all();
        }

        public void Initialize(string filename)
        {
            AVFormatContext* pOutputFormatContext = null;
            // // Allocate format context for output

            // ffmpeg.avformat_alloc_output_context2(&pOutputFormatContext, null, null, filename);
            // if (pOutputFormatContext == null)
            //     throw new ApplicationException("Could not create output context.");

            // // 设置使用系统时钟作为时间戳
            // pOutputFormatContext->flags |= ffmpeg.AVFMT_FLAG_GENPTS;
            // //ffmpeg.av_dict_set(&options, "use_wallclock_as_timestamps", "1", 0);

            // // 找到编码器
            // var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            // if (codec == null)
            //     throw new ApplicationException("Codec not found.");

            // var videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            // if (videoCodecContext == null)
            //     throw new ApplicationException("Could not allocate video codec context.");

            // // 设置编码参数
            // videoCodecContext->bit_rate = 400000;
            // videoCodecContext->width = 640;
            // videoCodecContext->height = 480;
            // videoCodecContext->time_base = new AVRational { num = 1, den = RATE };
            // videoCodecContext->framerate = new AVRational { num = RATE, den = 1 };
            // videoCodecContext->gop_size = RATE;
            // videoCodecContext->max_b_frames = 1;
            // videoCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

            // ffmpeg.av_opt_set(videoCodecContext->priv_data, "preset", "veryfast", 0);
            // ffmpeg.av_opt_set(videoCodecContext->priv_data, "tune", "zerolatency", 0);

            // // 如果开头有PPS的话，就不需要这个了 咱们是H264裸流，得要这个
            // if ((codec->capabilities & ffmpeg.AV_CODEC_CAP_DELAY) != 0)
            //     videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            // // 打开编码器
            // if (ffmpeg.avcodec_open2(videoCodecContext, codec, null) < 0)
            //     throw new ApplicationException("Could not open codec.");


            // //创建一个流
            // var outStream = ffmpeg.avformat_new_stream(pOutputFormatContext, codec);
            // if (outStream == null)
            //     throw new ApplicationException("Failed to allocate stream.");
            // outStream->time_base = new AVRational { num = 1, den = RATE };
            // ffmpeg.avcodec_parameters_from_context(outStream->codecpar, videoCodecContext);

            // // // 开启FLV
            // // if (ffmpeg.avio_open(&pOutputFormatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE) < 0)
            // //     throw new ApplicationException("Could not open output URL.");

            // //写入输出文件头
            // if (ffmpeg.avformat_write_header(pOutputFormatContext, null) < 0)
            // {
            //     Console.WriteLine("Error occurred when writing output file header.");
            //     return;
            // }

            ffmpeg.avformat_alloc_output_context2(&pOutputFormatContext, null, null, filename);
            if (pOutputFormatContext == null)
            {
                Console.WriteLine("Could not create output context");
                return;
            }

            AVCodec* codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
            {
                Console.WriteLine("Codec not found");
                return;
            }

            var outStream = ffmpeg.avformat_new_stream(pOutputFormatContext, codec);
            if (outStream == null)
            {
                Console.WriteLine("Could not create video stream");
                return;
            }

            AVCodecContext* videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            videoCodecContext->width = 640;
            videoCodecContext->height = 480;
            videoCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            videoCodecContext->time_base = new AVRational { num = 1, den = RATE };
            videoCodecContext->framerate = new AVRational { num = RATE, den = 1 };

            if ((pOutputFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }

            if (ffmpeg.avcodec_open2(videoCodecContext, codec, null) < 0)
            {
                Console.WriteLine("Could not open codec");
                return;
            }

            ffmpeg.avcodec_parameters_from_context(outStream->codecpar, videoCodecContext);
            outStream->time_base = videoCodecContext->time_base;

            if ((pOutputFormatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                if (ffmpeg.avio_open(&pOutputFormatContext->pb, filename, ffmpeg.AVIO_FLAG_WRITE) < 0)
                {
                    Console.WriteLine("Could not open output file");
                    return;
                }
            }

            if (ffmpeg.avformat_write_header(pOutputFormatContext, null) < 0)
            {
                Console.WriteLine("Error occurred when writing header");
                return;
            }

            _outputStream = outStream;
            _outputContext = pOutputFormatContext;
            _videoCodecContext = videoCodecContext;

        }


        public void Stream(AVFrame frame)
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            // //解码时间戳 这个让自增吧 只要小于PTS就可以
            // frame.pkt_dts = framePts;
            // //播放时间戳 如果有音频的话，他们俩的这个值同步就可以并轨了
            // frame.pts = framePts++ * (1000 / RATE);

            // //把AVFrame发送到编码器
            // int error = ffmpeg.avcodec_send_frame(_videoCodecContext, &frame);
            // if (error < 0)
            //     throw new ApplicationException("Error sending a frame for encoding.");

            // //编码后
            // while (ffmpeg.avcodec_receive_packet(_videoCodecContext, pkt) == 0)
            // {
            //     pkt->stream_index = _outputStream->index;
            //     pkt->time_base = new AVRational { num = 1, den = RATE };

            //     //av_interleaved_write_frame 是更严格一些的写入操作
            //     var res = ffmpeg.av_interleaved_write_frame(_outputContext, pkt);

            //     //av_write_frame 直接框框写
            //     //var res = ffmpeg.av_write_frame(_outputContext, pkt);
            //     if (res == 0)
            //     {
            //         Console.WriteLine(framePts + "生成成功," + _outputStream->index);
            //     }
            //     else if (res == -10053)
            //     {

            //     }
            //     ffmpeg.av_packet_unref(pkt);
            // }

            //frame.pkt_dts = framePts++ * (1000 / RATE);
            // frame.pkt_dts = framePts;
            // frame.pts = framePts++ * (1000 / RATE);
            frame.pts = framePts++ * 1000 / RATE;

            if (ffmpeg.avcodec_send_frame(_videoCodecContext, &frame) < 0)
            {
                Console.WriteLine("Error sending frame");
                return;
            }

            while (ffmpeg.avcodec_receive_packet(_videoCodecContext, pkt) == 0)
            {
                var res = ffmpeg.av_interleaved_write_frame(_outputContext, pkt);
                if (res == 0)
                {
                    Console.WriteLine(framePts + "生成成功," + pkt->stream_index);
                }
                else
                {
                    Console.WriteLine(framePts + "生成失败," + pkt->stream_index + "," + res);
                }
                ffmpeg.av_packet_unref(pkt);
            }

            ffmpeg.av_packet_unref(pkt);
            ffmpeg.av_packet_free(&pkt);
        }


        public void Dispose()
        {
            Console.WriteLine("释放资源");
            ffmpeg.av_write_trailer(_outputContext);
            fixed (AVFormatContext** ptrOutputContext = &_outputContext)
            {
                ffmpeg.avformat_close_input(ptrOutputContext);
            }
            //ffmpeg.avcodec_close(_videoCodecContext);
            fixed (AVCodecContext** ptrDecodecContext = &_videoCodecContext)
            {
                ffmpeg.avcodec_free_context(ptrDecodecContext);
            }
        }

    }
}
