using System;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Runtime.Serialization.Json;
using System.Linq;
using Serilog;
using Serilog.Core;

public class BaseSocket
{

	//public static int HeadLength = 8;
	public static int HeadLength = 12;//起始到数据长度
	public static int BuffLength = 1444;
	public static readonly byte[] StartBytes = new byte[] { 0xD5, 0xF0, 0x01, 0x00 };//起始标志

	public static Logger _byteLog;
	static BaseSocket()
	{
		var outputTemplate = "{Timestamp:HH:mm:ss.ffffff} [{Level:u3}] {Message:lj}{NewLine}";
		_byteLog = new LoggerConfiguration()
		.WriteTo.Console(outputTemplate: outputTemplate)
		.WriteTo.File("bytelog/.log", outputTemplate: outputTemplate, rollingInterval: RollingInterval.Day)
		.CreateLogger();
	}
	public static void WriteLog(string log)
	{
		_byteLog.Information(log);
	}
	//public static int BuffLength = 1450;
	public static byte[] Read(Stream stream, byte[] end)
	{
		using (MemoryStream ms = new MemoryStream())
		{
			byte[] data = new byte[1];
			int bytes = data.Length;
			while (bytes > 0 && BaseSocket.findBytes(ms.ToArray(), end, 0) == -1)
			{
				bytes = stream.Read(data, 0, data.Length);
				ms.Write(data, 0, data.Length);
			}
			return ms.ToArray();
		}
	}
	protected void Write(Stream stream, SocketMessager messager)
	{
		var buffer = this.GetWriteBuffer(messager);
		stream.Write(buffer, 0, buffer.Length);
	}
	protected void WriteAsync(Stream stream, SocketMessager messager)
	{
		var buffer = this.GetWriteBuffer(messager);
		stream.WriteAsync(buffer, 0, buffer.Length);
	}
	protected byte[] GetWriteBuffer(SocketMessager messager)
	{
		// using (MemoryStream ms = new MemoryStream())
		// {
		// 	byte[] buff = Encoding.UTF8.GetBytes(messager.GetCanParseString());
		// 	ms.Write(buff, 0, buff.Length);
		// 	if (messager.Arg != null)
		// 	{
		// 		var data = BaseSocket.Serialize(messager.Arg);
		// 		ms.Write(data, 0, data.Length);
		// 		//using (MemoryStream msBuf = new MemoryStream()) {
		// 		//	using (DeflateStream ds = new DeflateStream(msBuf, CompressionMode.Compress)) {
		// 		//		ds.Write(data, 0, data.Length);
		// 		//		buff = msBuf.ToArray();
		// 		//		ms.Write(buff, 0, buff.Length);
		// 		//	}
		// 		//}
		// 	}
		// 	return this.GetWriteBuffer(ms.ToArray());
		// }
		return new byte[0];
	}
	private byte[] GetWriteBuffer(byte[] data)
	{
		using (MemoryStream ms = new MemoryStream())
		{
			byte[] buff = Encoding.UTF8.GetBytes(Convert.ToString(data.Length + BaseSocket.HeadLength, 16).PadRight(BaseSocket.HeadLength));
			ms.Write(buff, 0, buff.Length);
			ms.Write(data, 0, data.Length);
			return ms.ToArray();
		}
	}

	protected SocketMessager Read(Stream stream)
	{
		byte[] data = new byte[BaseSocket.HeadLength];
		int bytes = 0;
		int overs = data.Length;
		string size = string.Empty;
		while (overs > 0)
		{
			bytes = stream.Read(data, 0, overs);
			overs -= bytes;
			size += Encoding.UTF8.GetString(data, 0, bytes);
		}

		if (int.TryParse(size, NumberStyles.HexNumber, null, out overs) == false)
		{
			return null;
		}
		overs -= BaseSocket.HeadLength;
		using (MemoryStream ms = new MemoryStream())
		{
			data = new Byte[1024];
			while (overs > 0)
			{
				bytes = stream.Read(data, 0, overs < data.Length ? overs : data.Length);
				overs -= bytes;
				ms.Write(data, 0, bytes);
			}
			return SocketMessager.Parse(ms.ToArray());
		}
	}

	public static byte[] Serialize(object obj)
	{
		using (MemoryStream ms = new MemoryStream())
		{
			DataContractJsonSerializer js = new DataContractJsonSerializer(typeof(object));
			js.WriteObject(ms, obj);
			return ms.ToArray();
		}
	}
	public static object Deserialize(byte[] stream)
	{
		using (MemoryStream ms = new MemoryStream(stream))
		{
			DataContractJsonSerializer js = new DataContractJsonSerializer(typeof(object));
			return js.ReadObject(ms);
		}
	}

	public static int findBytes(byte[] source, byte[] find, int startIndex)
	{
		if (find == null) return -1;
		if (find.Length == 0) return -1;
		if (source == null) return -1;
		if (source.Length == 0) return -1;
		if (startIndex < 0) startIndex = 0;
		int idx = -1, idx2 = startIndex - 1;
		do
		{
			idx2 = idx = Array.FindIndex<byte>(source, Math.Min(idx2 + 1, source.Length), delegate (byte b)
			{
				return b == find[0];
			});
			if (idx2 != -1)
			{
				for (int a = 1; a < find.Length; a++)
				{
					if (++idx2 >= source.Length || source[idx2] != find[a])
					{
						idx = -1;
						break;
					}
				}
				if (idx != -1) break;
			}
		} while (idx2 != -1);
		return idx;
	}

	public static string formatKBit(int kbit)
	{
		double mb = kbit;
		string unt = "bit";
		if (mb >= 8)
		{
			unt = "Byte";
			mb = mb / 8;
			if (mb >= 1024)
			{
				unt = "KB";
				mb = kbit / 1024;
				if (mb >= 1024)
				{
					unt = "MB";
					mb = mb / 1024;
					if (mb >= 1024)
					{
						unt = "G";
						mb = mb / 1024;
					}
				}
			}
		}
		return Math.Round(mb, 1) + unt;
	}
}

public class SocketMessager
{
	private static int _identity;
	private static long date1970Second = Convert.ToInt64((new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local) - new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Local)).TotalSeconds);
	private int _id;
	private uint _remoteTime;
	private ushort _seq;
	private ushort _dataLen;
	private byte _ID;
	private byte _EOF;
	private byte _currPacket;
	private byte _totalPacket;
	private string _bcd;
	private byte[] _picData;
	private string _sn;

	//public SocketMessager(uint remoteTime, ushort seq, ushort dataLen, byte ID, byte currPacket, byte totalPacket, byte[] bcd, byte[] picData)
	public SocketMessager(uint remoteTime, ushort seq, ushort dataLen, byte ID, byte EOF, byte currPacket, byte totalPacket,string sn, byte[] picData)
	{
		this._id = Interlocked.Increment(ref _identity);
		this._remoteTime = remoteTime;
		this._seq = seq;
		this._dataLen = dataLen;
		this._ID = ID;
		this._sn = sn;
		this._EOF = EOF;
		this._currPacket = currPacket;
		this._totalPacket = totalPacket;
		var bcdSb = new StringBuilder();
		// for (var i = 0; i < bcd.Length; i++)
		// {
		// 	bcdSb.Append(BCDToDecStr(bcd[i]));
		// }
		this._bcd = bcdSb.ToString();
		this._picData = picData;
	}

	/// <summary>
	/// BCD格式byte 转10进制字符串
	/// </summary>
	/// <param name="data"></param>
	/// <returns></returns>
	private string BCDToDecStr(byte data)
	{
		return (((data & 0xF0) >> 4) * 10 + (data & 0X0F)).ToString().PadLeft(2, '0');
	}

	public override string ToString()
	{
		return $"time:{this.RemoteTime.ToString("yyyy-MM-dd HH:mm:ss")}\tid:{this._id}\tseq:{this._seq}\tdataLen:{this._dataLen}\tID:{this._ID}\tEOF:{this._EOF}\tcurrPacket:{this._currPacket}\ttotalPacket:{this._totalPacket}\tbcd:{this._bcd}\tpicDateLen:{this._picData.Length}";
		//return $"time:{this.RemoteTime.ToString("yyyy-MM-dd HH:mm:ss")}\tid:{this._id}\tID:{this._ID}\tEOF:{this._EOF}\tcurrPacket:{this._currPacket}\ttotalPacket:{this._totalPacket}\tbcd:{this._bcd}\tpicDateLen:{this._picData.Length}";
	}

	public static SocketMessager Parse(byte[] data)
	{
		if (data == null) return null;
		if (data.Length < 20) return null;
		//Console.WriteLine(BitConverter.ToString(data));
		//int idx = BaseSocket.findBytes(data, new byte[] { 0xD5, 0xF0, 0x01, 0x00 }, 0);
		int idx = 0;
		SocketMessager messager;
		var dataLen = BitConverter.ToUInt16(data, idx + 10);
		messager = new SocketMessager(
			BitConverter.ToUInt32(data, idx + 4),
			BitConverter.ToUInt16(data, idx + 8),
			dataLen,
			data[idx + 16],
			data[idx + 17],
			data[idx + 18],
			data[idx + 19],
            BCDToString(data, idx + 20,6),
            data.Skip(26).ToArray()
		);

		return messager;
	}


    static string BCDToString(byte[] bytes, int startIndex, int length)
    {
        StringBuilder sb = new StringBuilder();

        for (int i = startIndex; i < startIndex + length; i++)
        {
            // 将高4位和低4位分别转换为十进制数字
            byte highDigit = (byte)((bytes[i] >> 4) & 0x0F);
            byte lowDigit = (byte)(bytes[i] & 0x0F);

            // 将两个数字连接成字符串
            sb.Append(highDigit);
            sb.Append(lowDigit);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 消息ID，每个一消息ID都是惟一的，同步发送时用
    /// </summary>
    public int Id
	{
		get { return _id; }
		set { _id = value; }
	}
	public DateTime RemoteTime
	{
		get { return new DateTime((this._remoteTime + date1970Second) * 1000 * 10000); }
	}
	public object Seq
	{
		get { return _seq; }
	}

    public string Sn
    {
        get { return _sn; }
    }
    public ushort DataLen
	{
		get { return _dataLen; }
	}
	public byte ID
	{
		get { return _ID; }
	}
	public byte EOF
	{
		get { return _EOF; }
	}
	public byte CurrPacket
	{
		get { return _currPacket; }
	}
	public byte TotalPacket
	{
		get { return _totalPacket; }
	}
	public byte[] PicData
	{
		get { return _picData; }
	}
}