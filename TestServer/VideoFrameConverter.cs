using FFmpeg.AutoGen;
using SkiaSharp;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FFmpegAnalyzer
{
    public sealed unsafe class VideoFrameConverter : IDisposable
    {
        private readonly IntPtr _convertedFrameBufferPtr;
        private readonly System.Drawing.Size _destinationSize;
        private readonly byte_ptrArray4 _dstData;
        private readonly int_array4 _dstLinesize;
        private readonly SwsContext* _pConvertContext;
        /// <summary>
        /// 帧格式转换
        /// </summary>
        /// <param name="sourceSize"></param>
        /// <param name="sourcePixelFormat"></param>
        /// <param name="destinationSize"></param>
        /// <param name="destinationPixelFormat"></param>
        public VideoFrameConverter(System.Drawing.Size sourceSize, AVPixelFormat sourcePixelFormat,
            System.Drawing.Size destinationSize, AVPixelFormat destinationPixelFormat)
        {
            _destinationSize = destinationSize;
            //分配并返回一个SwsContext。您需要它使用sws_scale()执行伸缩/转换操作
            //主要就是使用SwsContext进行转换！！！
            _pConvertContext = ffmpeg.sws_getContext((int)sourceSize.Width, (int)sourceSize.Height, sourcePixelFormat,
                (int)destinationSize.Width,
                (int)destinationSize.Height
                , destinationPixelFormat,
                ffmpeg.SWS_FAST_BILINEAR //默认算法 还有其他算法
                , null
                , null
                , null //额外参数 在flasgs指定的算法，而使用的参数。如果  SWS_BICUBIC  SWS_GAUSS  SWS_LANCZOS这些算法。  这里没有使用
                );
            if (_pConvertContext == null) throw new ApplicationException("Could not initialize the conversion context.");
            //获取媒体帧所需要的大小
            var convertedFrameBufferSize = ffmpeg.av_image_get_buffer_size(destinationPixelFormat
                , (int)destinationSize.Width, (int)destinationSize.Height
                , 1);
            //申请非托管内存,unsafe代码
            _convertedFrameBufferPtr = Marshal.AllocHGlobal(convertedFrameBufferSize);

            //转换帧的内存指针
            _dstData = new byte_ptrArray4();
            _dstLinesize = new int_array4();

            //挂在帧数据的内存区把_dstData里存的的指针指向_convertedFrameBufferPtr
            ffmpeg.av_image_fill_arrays(ref _dstData, ref _dstLinesize
                , (byte*)_convertedFrameBufferPtr
                , destinationPixelFormat
                , (int)destinationSize.Width, (int)destinationSize.Height
                , 1);
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_convertedFrameBufferPtr);
            ffmpeg.sws_freeContext(_pConvertContext);
        }

        public AVFrame Convert(AVFrame sourceFrame)
        {
            //转换格式
            ffmpeg.sws_scale(_pConvertContext
                , sourceFrame.data
                , sourceFrame.linesize
                , 0, sourceFrame.height
                , _dstData, _dstLinesize);

            var data = new byte_ptrArray8();
            data.UpdateFrom(_dstData);
            var linesize = new int_array8();
            linesize.UpdateFrom(_dstLinesize);

            return new AVFrame
            {
                data = data,
                linesize = linesize,
                width = (int)_destinationSize.Width,
                height = (int)_destinationSize.Height
            };
        }

        public MemoryStream SaveJpg(AVFrame sourceFrame, string fileName, string fileUrl)
        {
            try{

            var frame = &sourceFrame;
            // 设置图像参数（宽度、高度、像素格式等）
            int width = 640;
            int height = 480;
            // 设置 YUV 参数
            AVPixelFormat pixelFormat = AVPixelFormat.AV_PIX_FMT_YUV420P;
            var _dstData = new byte_ptrArray4();
            var _dstLineSize = new int_array4();
            // // 分配输出 RGB 图像的缓冲区
            // ffmpeg.av_malloc((ulong)ffmpeg.av_image_alloc(ref _dstData, ref _dstLineSize, width, height, AVPixelFormat.AV_PIX_FMT_RGB24, 1));

            // // 创建 SwsContext 对象进行颜色空间转换
            // SwsContext* swsContext = ffmpeg.sws_getContext(width, height, pixelFormat,
            //                                                 width, height, AVPixelFormat.AV_PIX_FMT_RGB24,
            //                                                 ffmpeg.SWS_BILINEAR, null, null, null);

            // // 创建 Bitmap 对象并从 RGB 数据中加载图像
            // System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(width, height, width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(_dstData[0]));

            // //stream.StartStreaming(bitmap);

            // // 保存图像
            // //bitmap.Save(fileUrl + "/" + fileName + ".jpg", System.Drawing.Imaging.ImageFormat.Jpeg);
            // var jpegStream = new MemoryStream();
            // bitmap.Save(jpegStream, System.Drawing.Imaging.ImageFormat.Jpeg);

            // 分配输出 RGB 图像的缓冲区
            ffmpeg.av_malloc((ulong)ffmpeg.av_image_alloc(ref _dstData, ref _dstLineSize, width, height, AVPixelFormat.AV_PIX_FMT_RGBA, 1));
            // 创建 SwsContext 对象进行颜色空间转换
            SwsContext* swsContext = ffmpeg.sws_getContext(width, height, pixelFormat,
                                                   width, height, AVPixelFormat.AV_PIX_FMT_RGBA,
                                                   ffmpeg.SWS_BILINEAR, null, null, null);

            // 转换颜色空间
            ffmpeg.sws_scale(swsContext, frame->data, frame->linesize, 0, height, _dstData, _dstLineSize);

            using SKBitmap bitmap = new SKBitmap(width, height);
            SKImageInfo info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            bitmap.InstallPixels(info, new IntPtr(_dstData[0]));

            var jpegStream = new MemoryStream();
            bitmap.Encode(jpegStream, SKEncodedImageFormat.Jpeg, 100);

            // 释放资源
            ffmpeg.av_free(_dstData[0]);
            ffmpeg.sws_freeContext(swsContext);

            return jpegStream;
            }catch(Exception ex){
                Console.WriteLine("保存图像异常"+ex.Message);
            }
            return null;
        }
    }
}