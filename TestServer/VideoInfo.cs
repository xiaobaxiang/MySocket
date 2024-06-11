using System;
using System.IO;
using FFmpeg.AutoGen;

namespace TestServer
{
    ///<summary>
    /// 设备视频信息
    ///</summary>
    public class VideoInfo
    {
        ///<summary>
        /// 设备用户ID
        ///</summary>
        public long User { get; set; }
        ///<summary>
        /// 当前视频流
        ///</summary>
        public MemoryStream VideoStream { get; set; }

        ///<summary>
        /// 当前时刻需要保存的照片
        ///</summary>
        public PicItem PicItem { get; set; }

        ///<summary>
        /// 环形链表保存历史视频流用于生成关键图前后20秒视频使用
        ///</summary>
        public CircleLinkList<VideoFrame> DevFrameList { get; set; } = new CircleLinkList<VideoFrame>(500);
    }
    public class VideoFrame
    {
        ///<summary>
        /// 时间戳
        ///</summary>
        public long Time { get; set; }

        ///<summary>
        /// 视频流AVFrame
        ///</summary>
        public AVFrame AVFrame { get; set; }
        public byte[] AVBytes { get; set; }

        public override string ToString()
        {
            return $"Current frame {Time} {AVFrame.buf.Length}";
        }
    }

    ///<summary>
    /// 每秒保存3张照片
    ///</summary>
    public class PicItem
    {
        public string Sn { get; set; }
        public long Time { get; set; }
        public long User { get; set; }

        /// <summary>
        /// 0-300毫秒图片
        /// </summary>
        public string Pic1 { get; set; }
        /// <summary>
        /// 300-600毫秒图片
        /// </summary>
        public string Pic2 { get; set; }
        /// <summary>
        /// 600-900毫秒图片
        /// </summary>
        public string Pic3 { get; set; }
    }

    public class VideoClip
    {
        ///<summary>
        /// 设备Sn
        ///</summary>
        public string Sn { get; set; }
        ///<summary>
        /// 时间戳
        ///</summary>
        public long Time { get; set; }
        ///<summary>
        /// 宠物id
        ///</summary>
        public long PetId { get; set; }
        ///<summary>
        /// 识别率
        ///</summary>
        public double Distinguish { get; set; }
        ///<summary>
        /// 视频信息
        ///</summary>
        public string File { get; set; }
    }
}