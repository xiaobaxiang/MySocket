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
	public static int HeadLength = 12;//起始到数据长度10 需要改大
	public static int BuffLength = 8000;//缓冲区大小6600 需要改大
	public static readonly byte[] StartBytes = new byte[] { 0xD6, 0xF0, 0x01, 0x02 };//起始标志

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
		return messager.ToBytes();
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

	public static int FindPattern(byte[] buffer, byte[] pattern)
	{
		int iMax = buffer.Length - pattern.Length + 1;
		for (int i = 0; i < iMax; i++)
		{
			bool found = true;
			for (int j = 0; j < pattern.Length; j++)
			{
				if (buffer[i + j] != pattern[j])
				{
					found = false;
					break;
				}
			}
			if (found)
			{
				return i;
			}
		}
		return -1; // Not found
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
	private int _dataLen;
	private string _sn;
	private byte[] _picData;

	public SocketMessager(uint remoteTime, int dataLen, string sn, byte[] picData)
	{
		this._id = Interlocked.Increment(ref _identity);
		this._remoteTime = remoteTime;
		this._dataLen = dataLen;
		this._sn = sn;
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
		return $"time:{this.RemoteTime.ToString("yyyy-MM-dd HH:mm:ss")}\tid:{this._id}\t\tdataLen:{this._dataLen}\tsn:{this._sn}\tpicDateLen:{this._picData.Length}";
	}

	public static SocketMessager Parse(byte[] data)
	{
		if (data == null) return null;
		if (data.Length < 19) return null;
		//Console.WriteLine(BitConverter.ToString(data));
		//int idx = BaseSocket.findBytes(data, new byte[] { 0xD5, 0xF0, 0x01, 0x00 }, 0);
		int idx = 0;
		SocketMessager messager;
		var dataLen = BitConverter.ToInt32(data, idx + 8);
		messager = new SocketMessager(
			BitConverter.ToUInt32(data, idx + 4),
			dataLen,
			BCDToString(data, idx + 13, 6),
			data.Skip(19).ToArray()
		);

		return messager;
	}

	public byte[] ToBytes()
	{
		return new byte[] { 0xD6, 0xF0, 0x01, 0x02 }
		.Concat(BitConverter.GetBytes(this.TimeToken))
		.Concat(new byte[] { (byte)this.PicData.Length })
		.Concat(new byte[] { HndCrc8(this.PicData) })
		.Concat(StringToBCD(this.Sn))
		.Concat(this.PicData).ToArray();
	}

	private static byte[] crc8Table =
	{
		0x00, 0xF7, 0xB9, 0x4E, 0x25, 0xD2, 0x9C, 0x6B, 0x4A, 0xBD, 0xF3, 0x04, 0x6F, 0x98, 0xD6, 0x21, 0x94, 0x63, 0x2D, 0xDA, 0xB1, 0x46, 0x08, 0xFF, 0xDE, 0x29, 0x67, 0x90, 0xFB, 0x0C, 0x42, 0xB5, 0x7F, 0x88, 0xC6, 0x31, 0x5A, 0xAD, 0xE3, 0x14, 0x35, 0xC2, 0x8C, 0x7B, 0x10, 0xE7, 0xA9, 0x5E, 0xEB, 0x1C, 0x52, 0xA5, 0xCE, 0x39, 0x77, 0x80, 0xA1, 0x56, 0x18, 0xEF, 0x84, 0x73, 0x3D, 0xCA, 0xFE, 0x09, 0x47, 0xB0, 0xDB, 0x2C, 0x62, 0x95, 0xB4, 0x43, 0x0D, 0xFA, 0x91, 0x66, 0x28, 0xDF, 0x6A, 0x9D, 0xD3, 0x24, 0x4F, 0xB8, 0xF6, 0x01, 0x20, 0xD7, 0x99, 0x6E, 0x05, 0xF2, 0xBC, 0x4B, 0x81, 0x76, 0x38, 0xCF, 0xA4, 0x53, 0x1D, 0xEA, 0xCB, 0x3C, 0x72, 0x85, 0xEE, 0x19, 0x57, 0xA0, 0x15, 0xE2, 0xAC, 0x5B, 0x30, 0xC7, 0x89, 0x7E, 0x5F, 0xA8, 0xE6, 0x11, 0x7A, 0x8D, 0xC3, 0x34, 0xAB, 0x5C, 0x12, 0xE5, 0x8E, 0x79, 0x37, 0xC0, 0xE1, 0x16, 0x58, 0xAF, 0xC4, 0x33, 0x7D, 0x8A, 0x3F, 0xC8, 0x86, 0x71, 0x1A, 0xED, 0xA3, 0x54, 0x75, 0x82, 0xCC, 0x3B, 0x50, 0xA7, 0xE9, 0x1E, 0xD4, 0x23, 0x6D, 0x9A, 0xF1, 0x06, 0x48, 0xBF, 0x9E, 0x69, 0x27, 0xD0, 0xBB, 0x4C, 0x02, 0xF5, 0x40, 0xB7, 0xF9, 0x0E, 0x65, 0x92, 0xDC, 0x2B, 0x0A, 0xFD, 0xB3, 0x44, 0x2F, 0xD8, 0x96, 0x61, 0x55, 0xA2, 0xEC, 0x1B, 0x70, 0x87, 0xC9, 0x3E, 0x1F, 0xE8, 0xA6, 0x51, 0x3A, 0xCD, 0x83, 0x74, 0xC1, 0x36, 0x78, 0x8F, 0xE4, 0x13, 0x5D, 0xAA, 0x8B, 0x7C, 0x32, 0xC5, 0xAE, 0x59, 0x17, 0xE0, 0x2A, 0xDD, 0x93, 0x64, 0x0F, 0xF8, 0xB6, 0x41, 0x60, 0x97, 0xD9, 0x2E, 0x45, 0xB2, 0xFC, 0x0B, 0xBE, 0x49, 0x07, 0xF0, 0x9B, 0x6C, 0x22, 0xD5, 0xF4, 0x03, 0x4D, 0xBA, 0xD1, 0x26, 0x68, 0x9F
	};
	private static byte HndCrc8(byte[] data)
	{
		byte crc = 0xFF;
		for (int i = 0; i < data.Length; i++)
		{
			crc = crc8Table[(crc ^ data[i]) & 0xFF];
		}
		return crc;
	}

	static string BCDToString(byte[] bytes, int startIndex, int length)
	{
		StringBuilder sb = new StringBuilder();

		// for (int i = startIndex; i < startIndex + length; i++)
		// {
		// 	// 将高4位和低4位分别转换为十进制数字
		// 	byte highDigit = (byte)((bytes[i] >> 4) & 0x0F);
		// 	byte lowDigit = (byte)(bytes[i] & 0x0F);

		// 	// 将两个数字连接成字符串
		// 	sb.Append(highDigit);
		// 	sb.Append(lowDigit);
		// }

		for (int i = startIndex; i < startIndex + length; i++)
		{
			sb.Append(bytes[i].ToString("X2"));
		}
		return sb.ToString();
	}
	static byte[] StringToBCD(string strTemp)
	{
		if (Convert.ToBoolean(strTemp.Length & 1))//若字符串的长度为奇数，则变为偶数 
		{
			strTemp = "0" + strTemp;//数位为奇数时前面补0  
		}
		Byte[] aryTemp = new Byte[strTemp.Length / 2];
		for (int i = 0; i < (strTemp.Length / 2); i++)
		{
			aryTemp[i] = (Byte)(((strTemp[i * 2] - '0') << 4) | (strTemp[i * 2 + 1] - '0'));//两个字节的数组成一个字节的BCD码0-9
																							// aryTemp[i] = (Byte)(((strTemp[i * 2])<< 4) | (strTemp[i * 2 + 1]));
		}
		return aryTemp;//高位在前
	}
	/// <summary>
	/// 消息ID，每个一消息ID都是惟一的，同步发送时用
	/// </summary>
	public int Id
	{
		get { return _id; }
		set { _id = value; }
	}
	public uint TimeToken
	{
		get { return this._remoteTime; }
	}
	public DateTime RemoteTime
	{
		get { return new DateTime((this._remoteTime + date1970Second + (int)TimeZoneInfo.Local.BaseUtcOffset.TotalSeconds) * 1000 * 10000, DateTimeKind.Local); }
	}
	public string Sn
	{
		get { return _sn; }
	}
	public int DataLen
	{
		get { return _dataLen; }
	}
	public byte[] PicData
	{
		get { return _picData; }
	}
}