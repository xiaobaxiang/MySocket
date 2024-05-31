using FFmpeg.AutoGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestServer
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
    public unsafe class RtmpStreamer
    {
        private AVFormatContext* _outputContext = null;
        private AVCodecContext* _videoCodecContext = null;
        private long framePts = 0;
        private const int RATE = 10;
        public RtmpStreamer()
        {
            ffmpeg.avformat_network_init();
        }

        public void Initialize(string rtmpUrl)
        {
            AVFormatContext* pOutputFormatContext = null;
            // Allocate format context for output
            ffmpeg.avformat_alloc_output_context2(&pOutputFormatContext, null, "flv", rtmpUrl);
            if (pOutputFormatContext == null)
                throw new ApplicationException("Could not create output context.");

            // 设置使用系统时钟作为时间戳
            pOutputFormatContext->flags |= ffmpeg.AVFMT_FLAG_GENPTS;
            //ffmpeg.av_dict_set(&options, "use_wallclock_as_timestamps", "1", 0);

            // 找到编码器
            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
                throw new ApplicationException("Codec not found.");

            _videoCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (_videoCodecContext == null)
                throw new ApplicationException("Could not allocate video codec context.");

            // 设置编码参数
            _videoCodecContext->bit_rate = 400000;
            _videoCodecContext->width = 640;
            _videoCodecContext->height = 480;
            _videoCodecContext->time_base = new AVRational { num = 1, den = RATE };
            _videoCodecContext->framerate = new AVRational { num = RATE, den = 1 };
            _videoCodecContext->gop_size = RATE;
            _videoCodecContext->max_b_frames = 1;
            _videoCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

            ffmpeg.av_opt_set(_videoCodecContext->priv_data, "preset", "veryfast", 0);
            ffmpeg.av_opt_set(_videoCodecContext->priv_data, "tune", "zerolatency", 0);

            // 如果开头有PPS的话，就不需要这个了 咱们是H264裸流，得要这个
            if ((codec->capabilities & ffmpeg.AV_CODEC_CAP_DELAY) != 0)
                _videoCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

            // 打开编码器
            if (ffmpeg.avcodec_open2(_videoCodecContext, codec, null) < 0)
                throw new ApplicationException("Could not open codec.");

            // 打开一个流
            AVStream* outStream = ffmpeg.avformat_new_stream(pOutputFormatContext, codec);
            if (outStream == null)
                throw new ApplicationException("Failed to allocate stream.");
            outStream->time_base = new AVRational { num = 1, den = RATE };
            ffmpeg.avcodec_parameters_from_context(outStream->codecpar, _videoCodecContext);

            // 开启RTMP
            if (ffmpeg.avio_open(&pOutputFormatContext->pb, rtmpUrl, ffmpeg.AVIO_FLAG_WRITE) < 0)
                throw new ApplicationException("Could not open output URL.");


            //写入输出文件头
            if (ffmpeg.avformat_write_header(pOutputFormatContext, null) < 0)
            {
                Console.WriteLine("Error occurred when writing output file header.");
                return;
            }

            _outputContext = pOutputFormatContext;
        }

        public void Stream(AVFrame frame)
        {
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            //解码时间戳 这个让自增吧 只要小于PTS就可以
            frame.pkt_dts = framePts;
            //播放时间戳 如果有音频的话，他们俩的这个值同步就可以并轨了
            frame.pts = framePts++ * (1000 / RATE);

            //把AVFrame发送到编码器
            int error = ffmpeg.avcodec_send_frame(_videoCodecContext, &frame);
            if (error < 0)
                throw new ApplicationException("Error sending a frame for encoding.");

            //编码后
            while (ffmpeg.avcodec_receive_packet(_videoCodecContext, pkt) == 0)
            {
                pkt->stream_index = 0;
                pkt->time_base = new AVRational { num = 1, den = RATE };

                //av_interleaved_write_frame 是更严格一些的写入操作
                var res = ffmpeg.av_interleaved_write_frame(_outputContext, pkt);

                //av_write_frame 直接框框写
                //var res = ffmpeg.av_write_frame(_outputContext, pkt);
                if (res == 0)
                {
                    Console.WriteLine(framePts + "发送成功");
                }
                else
                {
                    Console.WriteLine(framePts + "发送失败," + res);
                }
                ffmpeg.av_packet_unref(pkt);
            }
            ffmpeg.av_packet_unref(pkt);
            ffmpeg.av_packet_free(&pkt);
        }

        public void Finish()
        {
            ffmpeg.av_write_trailer(_outputContext);
            //ffmpeg.avcodec_close(_videoCodecContext);
            fixed (AVCodecContext** ptrDecodecContext = &_videoCodecContext)
            {
                ffmpeg.avcodec_free_context(ptrDecodecContext);
            }
        }
    }
}
