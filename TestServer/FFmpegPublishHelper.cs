using FFmpeg.AutoGen;
using FFmpegAnalyzer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace TestServer
{
    public   class FFmpegPublishHelper
    {
        public static Dictionary<string, AVFormatContext> publishConnector = new Dictionary<string, AVFormatContext>();

        public static Dictionary<string, List<byte[]>> publishData = new Dictionary<string, List<byte[]>>();


        public static Dictionary<string, List<AVFrame>> publishFrame = new Dictionary<string, List<AVFrame>>();

    }
}
