using FFmpeg.AutoGen;
using System;

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
    public unsafe class H2642Mp4Streamer : IDisposable
    {
        private AVFormatContext* _outputContext = null;
        private AVCodecContext* _videoCodecContext = null;
        private AVStream* stream = null;
        private long framePts = 0;
        private const int RATE = 10;
        public H2642Mp4Streamer()
        {
            ffmpeg.avformat_network_init();
        }

        public void Initialize(string filename)
        {
            // 设置输出文件名和格式
            var outputFormat = ffmpeg.av_guess_format("mp4", filename, null);
            var formatContext = ffmpeg.avformat_alloc_context();
            formatContext->oformat = outputFormat;
            var outputPath = filename;
            if ((formatContext->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            {
                ffmpeg.avio_open(&formatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            }

            // 找到编码器
            var codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
                throw new ApplicationException("Codec not found.");

            // 添加视频流
            stream = ffmpeg.avformat_new_stream(formatContext, codec);

            var codecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (codecContext == null)
                throw new ApplicationException("Could not allocate video codec context.");

            // 配置编码参数
            codecContext->codec_id = AVCodecID.AV_CODEC_ID_H264;
            codecContext->bit_rate = 4000000; // 比特率
            codecContext->width = 640;       // 视频宽度
            codecContext->height = 480;      // 视频高度
            codecContext->time_base = new AVRational { num = 1, den = 25 }; // 时间基准
            codecContext->gop_size = 10;      // 关键帧间隔
            codecContext->max_b_frames = 1;   // B帧最大数
            codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P; // 像素格式

            ffmpeg.avcodec_open2(codecContext, codec, null);
            ffmpeg.avformat_write_header(formatContext, null);
            _videoCodecContext = codecContext;
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
                // 校正时间戳
                ffmpeg.av_packet_rescale_ts(pkt, _videoCodecContext->time_base, stream->time_base);
                pkt->stream_index = stream->index;
                // pkt->stream_index = 0;
                // pkt->time_base = new AVRational { num = 1, den = RATE };

                //av_write_frame 直接框框写
                var res = ffmpeg.av_write_frame(_outputContext, pkt);
                if (res == 0)
                {
                    Console.WriteLine(framePts + "生成成功");
                }
                else
                {
                    Console.WriteLine(framePts + "生成失败," + res);
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

        public void Dispose()
        {
            Console.WriteLine("释放资源");
            ffmpeg.av_write_trailer(_outputContext);
            //ffmpeg.avcodec_close(_videoCodecContext);
            fixed (AVCodecContext** ptrDecodecContext = &_videoCodecContext)
            {
                ffmpeg.avcodec_free_context(ptrDecodecContext);
            }
        }
    }
}
