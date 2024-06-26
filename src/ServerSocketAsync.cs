﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Serilog;
using Serilog.Core;

public class ServerSocketAsync : IDisposable
{
	private TcpListener _tcpListener;
	private Dictionary<int, AcceptSocket> _clients = new Dictionary<int, AcceptSocket>();
	private object _clients_lock = new object();
	private int _id = 1;
	private int _port;
	private bool _running;
	public event AcceptedEventHandler Accepted;
	public event ClosedEventHandler Closed;
	public event ReceiveEventHandler Receive;
	public event ErrorEventHandler Error;

	internal WorkQueue _receiveWQ;
	internal WorkQueue _receiveSyncWQ;
	//private WorkQueue _writeWQ;

	private IAsyncResult _beginAcceptTcpClient;
	public static Logger _serverLog;
	static ServerSocketAsync()
	{
		var outputTemplate = "{Timestamp:HH:mm:ss.ffffff} [{Level:u3}] {Message:lj}{NewLine}";
		_serverLog = new LoggerConfiguration()
		.WriteTo.Console(outputTemplate: outputTemplate)
		.WriteTo.File("serverlog/.log", outputTemplate: outputTemplate, rollingInterval: RollingInterval.Day)
		.CreateLogger();
	}

	public ServerSocketAsync(int port)
	{
		this._port = port;
	}

	public void Start()
	{
		if (this._running == false)
		{
			this._running = true;
			try
			{
				this._tcpListener = new TcpListener(IPAddress.Any, this._port);
				this._tcpListener.Start();
				this._receiveWQ = new WorkQueue();
				this._receiveSyncWQ = new WorkQueue();
				//this._writeWQ = new WorkQueue();
			}
			catch (Exception ex)
			{
				this._running = false;
				this.OnError(ex);
				_serverLog.Error(ex, "start error");
				return;
			}
			this._beginAcceptTcpClient = this._tcpListener.BeginAcceptTcpClient(HandleTcpClientAccepted, null);
		}
	}

	private void HandleTcpClientAccepted(IAsyncResult ar)
	{
		if (this._running)
		{
			try
			{
				TcpClient tcpClient = this._tcpListener.EndAcceptTcpClient(ar);

				try
				{
					AcceptSocket acceptSocket = new AcceptSocket(this, tcpClient, this._id);

					// // 禁用Nagle算法
					// acceptSocket.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
					this.OnAccepted(acceptSocket);
				}
				catch (Exception ex)
				{
					this.OnError(ex);
					_serverLog.Error(ex, "accept Socket error");
				}

				this._beginAcceptTcpClient = this._tcpListener.BeginAcceptTcpClient(HandleTcpClientAccepted, null);
			}
			catch (Exception ex)
			{
				this.OnError(ex);
				_serverLog.Error(ex, "accept Tcp Client error");
			}
		}
	}
	public void Stop()
	{
		if (this._tcpListener != null)
		{
			this._tcpListener.Stop();
		}
		if (this._running == true)
		{
			this._beginAcceptTcpClient.AsyncWaitHandle.Close();

			this._running = false;

			int[] keys = new int[this._clients.Count];
			try
			{
				this._clients.Keys.CopyTo(keys, 0);
			}
			catch
			{
				lock (this._clients_lock)
				{
					keys = new int[this._clients.Count];
					this._clients.Keys.CopyTo(keys, 0);
				}
			}
			foreach (int key in keys)
			{
				AcceptSocket client = null;
				if (this._clients.TryGetValue(key, out client))
				{
					client.Close();
				}
			}
			if (this._receiveWQ != null)
			{
				this._receiveWQ.Dispose();
			}
			if (this._receiveSyncWQ != null)
			{
				this._receiveSyncWQ.Dispose();
			}
			// if (this._writeWQ != null)
			// {
			// 	this._writeWQ.Dispose();
			// }
			this._clients.Clear();
		}
	}

	internal void AccessDenied(AcceptSocket client)
	{
		// client.Write(SocketMessager.SYS_ACCESS_DENIED, delegate (object sender2, ReceiveEventArgs e2)
		// {
		// }, TimeSpan.FromSeconds(1));
		client.Close();
	}

	public void Write(SocketMessager messager)
	{
		// int[] keys = new int[this._clients.Count];
		// try
		// {
		// 	this._clients.Keys.CopyTo(keys, 0);
		// }
		// catch
		// {
		// 	lock (this._clients_lock)
		// 	{
		// 		keys = new int[this._clients.Count];
		// 		this._clients.Keys.CopyTo(keys, 0);
		// 	}
		// }
		// foreach (int key in keys)
		// {
		// 	AcceptSocket client = null;
		// 	if (this._clients.TryGetValue(key, out client))
		// 	{
		// 		this._writeWQ.Enqueue(delegate ()
		// 		{
		// 			client.Write(messager);
		// 		});
		// 	}
		// }
	}

	public AcceptSocket GetAcceptSocket(int id)
	{
		AcceptSocket socket = null;
		this._clients.TryGetValue(id, out socket);
		return socket;
	}

	internal void CloseClient(AcceptSocket client)
	{
		this._clients.Remove(client.Id);
	}

	protected virtual void OnAccepted(AcceptedEventArgs e)
	{
		// SocketMessager helloMessager = new SocketMessager(SocketMessager.SYS_HELLO_WELCOME.Action);
		// e.AcceptSocket.Write(helloMessager, delegate (object sender2, ReceiveEventArgs e2)
		// {
		// 	if (e2.Messager.Id == helloMessager.Id &&
		// 		string.Compare(e2.Messager.Action, helloMessager.Action) == 0)
		// 	{
		// 		e.AcceptSocket._accepted = true;
		// 	}
		// }, TimeSpan.FromSeconds(2));
		e.AcceptSocket._accepted = true;
		if (e.AcceptSocket._accepted)
		{
			if (this.Accepted != null)
			{
				try
				{
					this.Accepted(this, e);
				}
				catch (Exception ex)
				{
					this.OnError(ex);
					_serverLog.Error(ex, "OnAccepted error");
				}
			}
		}
		else
		{
			e.AcceptSocket.AccessDenied();
		}
	}
	private void OnAccepted(AcceptSocket client)
	{
		lock (_clients_lock)
		{
			_clients.Add(this._id++, client);
		}
		AcceptedEventArgs e = new AcceptedEventArgs(this._clients.Count, client);
		this.OnAccepted(e);
	}

	protected virtual void OnClosed(ClosedEventArgs e)
	{
		if (this.Closed != null)
		{
			this.Closed(this, e);
		}
	}
	internal void OnClosed(AcceptSocket client)
	{
		ClosedEventArgs e = new ClosedEventArgs(this._clients.Count, client.Id);
		this.OnClosed(e);
	}

	protected virtual void OnReceive(ReceiveEventArgs e)
	{
		if (this.Receive != null)
		{
			this.Receive(this, e);
		}
	}
	internal void OnReceive2(ReceiveEventArgs e)
	{
		this.OnReceive(e);
	}

	protected virtual void OnError(ErrorEventArgs e)
	{
		if (this.Error != null)
		{
			this.Error(this, e);
		}
	}
	protected void OnError(Exception ex)
	{
		ErrorEventArgs e = new ErrorEventArgs(-1, ex, null);
		this.OnError(e);
		_serverLog.Error(ex, "OnError error");
	}
	internal void OnError2(ErrorEventArgs e)
	{
		this.OnError(e);
		_serverLog.Error(e.Exception, "OnError2 error");
	}

	#region IDisposable 成员

	public void Dispose()
	{
		this.Stop();
	}

	#endregion

	public class AcceptSocket : BaseSocket, IDisposable
	{
		private ServerSocketAsync _server;
		private TcpClient _tcpClient;
		public TcpClient TcpClient { get { return _tcpClient; } }
		private bool _running;
		private int _id;
		private ulong _receives;
		private int _errors;
		private object _errors_lock = new object();
		private object _write_lock = new object();
		// private Dictionary<int, SyncReceive> _receiveHandlers = new Dictionary<int, SyncReceive>();
		// private object _receiveHandlers_lock = new object();
		private DateTime _lastActive;
		internal bool _accepted;
		internal IAsyncResult _beginRead;

		public AcceptSocket(ServerSocketAsync server, TcpClient tcpClient, int id)
		{
			this._running = true;
			this._id = id;
			this._server = server;
			this._tcpClient = tcpClient;
			this._lastActive = DateTime.Now;
			MyHandleDataReceived();
		}

		private void HandleDataReceived()
		{
			if (this._running)
			{
				try
				{
					NetworkStream ns = this._tcpClient.GetStream();
					ns.ReadTimeout = 1000 * 20;

					DataReadInfo dr = new DataReadInfo(DataReadInfoType.Head, this, ns, BaseSocket.HeadLength, BaseSocket.HeadLength);
					dr.BeginRead();

				}
				catch (Exception ex)
				{
					this._running = false;
					this.OnError(ex);
					_serverLog.Error(ex, "HandleDataReceived GetStream error");
				}
			}
		}

		private void MyHandleDataReceived()
		{
			if (this._running)
			{
				try
				{
					NetworkStream ns = this._tcpClient.GetStream();
					ns.ReadTimeout = 1000 * 20;

					MyDataReadInfo dr = new MyDataReadInfo(DataReadInfoType.UnKnown, this, ns, BaseSocket.BuffLength, BaseSocket.BuffLength);
					dr.BeginRead();
				}
				catch (Exception ex)
				{
					this._running = false;
					_serverLog.Error(ex, "MyHandleDataReceived GetStream error");
				}
			}
		}

		private void OnDataAvailable(DataReadInfo dr)
		{
			// SocketMessager messager = SocketMessager.Parse(dr.ResponseStream.ToArray());
			// if (string.Compare(messager.Action, SocketMessager.SYS_QUIT.Action) == 0)
			// {
			// 	dr.AcceptSocket.Close();
			// }
			// else if (string.Compare(messager.Action, SocketMessager.SYS_TEST_LINK.Action) != 0)
			// {
			// 	ReceiveEventArgs e = new ReceiveEventArgs(this._receives++, messager, this);
			// 	SyncReceive receive = null;

			// 	if (this._receiveHandlers.TryGetValue(messager.Id, out receive))
			// 	{
			// 		this._server._receiveSyncWQ.Enqueue(delegate ()
			// 		{
			// 			try
			// 			{
			// 				receive.ReceiveHandler(this, e);
			// 			}
			// 			catch (Exception ex)
			// 			{
			// 				this.OnError(ex);
			// 			}
			// 			finally
			// 			{
			// 				receive.Wait.Set();
			// 			}
			// 		});
			// 	}
			// 	else
			// 	{
			// 		this._server._receiveWQ.Enqueue(delegate ()
			// 		{
			// 			this.OnReceive(e);
			// 		});
			// 	}
			// }
			this._lastActive = DateTime.Now;
			HandleDataReceived();
		}

		private void OnDataAvailable(MyDataReadInfo dr)
		{
			SocketMessager messager = SocketMessager.Parse(dr.Buffer);

			// SyncReceive receive = null;

			// if (this._receiveHandlers.TryGetValue(messager.Id, out receive))
			// {
			// 	this._server._receiveSyncWQ.Enqueue(delegate ()
			// 	{
			// 		try
			// 		{
			// 			receive.ReceiveHandler(this, e);
			// 		}
			// 		catch (Exception ex)
			// 		{
			// 			this.OnError(ex);
			// 		}
			// 		finally
			// 		{
			// 			receive.Wait.Set();
			// 		}
			// 	});
			// }
			// else
			// {
			// 	this._server._receiveWQ.Enqueue(delegate ()
			// 	{
			// 		this.OnReceive(e);
			// 	});
			// }
			// this._server._receiveWQ.Enqueue(delegate ()
			// {
			// 	this.OnReceive(e);
			// });
			if (messager != null)
			{
				ReceiveEventArgs e = new ReceiveEventArgs(this._receives++, messager, this);
				this.OnReceive(e);
			}
			this._lastActive = DateTime.Now;
			//MyHandleDataReceived();
		}

		class DataReadInfo
		{
			public DataReadInfoType Type { get; set; }
			public AcceptSocket AcceptSocket { get; }
			public NetworkStream NetworkStream { get; }
			public byte[] Buffer { get; }
			public int Size { get; }
			public int Over { get; set; }
			public int OverZoreTimes { get; set; }
			public MemoryStream ResponseStream { get; set; }
			public DataReadInfo(DataReadInfoType type, AcceptSocket client, NetworkStream ns, int bufferSize, int size)
			{
				this.Type = type;
				this.AcceptSocket = client;
				this.NetworkStream = ns;
				this.Buffer = new byte[bufferSize];
				this.Size = size;
				this.Over = size;
				this.ResponseStream = new MemoryStream();
			}

			public void BeginRead()
			{
				this.AcceptSocket._beginRead = this.NetworkStream.BeginRead(this.Buffer, 0, this.Over < this.Buffer.Length ? this.Over : this.Buffer.Length, HandleDataRead, this);
			}
		}

		class MyDataReadInfo
		{
			public DataReadInfoType Type { get; set; }
			public AcceptSocket AcceptSocket { get; }
			public NetworkStream NetworkStream { get; }
			public MemoryStream TempStream { get; }//头部12字节和可能断包字节
			public byte[] Buffer { get; set; }
			public int Over { get; set; }
			// public int OverZoreTimes { get; set; }
			// public MemoryStream ResponseStream { get; set; }
			public MyDataReadInfo(DataReadInfoType type, AcceptSocket client, NetworkStream ns, int bufferSize, int size)
			{
				this.Type = type;
				this.AcceptSocket = client;
				this.NetworkStream = ns;
				this.Buffer = new byte[bufferSize];
				this.Over = size;
				// this.ResponseStream = new MemoryStream();
				this.TempStream = new MemoryStream();
			}

			public void BeginRead()
			{
				this.AcceptSocket._beginRead = this.NetworkStream.BeginRead(this.Buffer, 0, this.Over < this.Buffer.Length ? this.Over : this.Buffer.Length, MyHandleDataRead, this);
			}
		}

		enum DataReadInfoType
		{
			///未知
			UnKnown,
			///读帧头
			Head,
			///读帧体
			Body
		}

		static void HandleDataRead(IAsyncResult ar)
		{
			DataReadInfo dr = ar.AsyncState as DataReadInfo;
			if (dr.AcceptSocket._running)
			{
				int overs = 0;
				try
				{
					overs = dr.NetworkStream.EndRead(ar);
				}
				catch (Exception ex)
				{
					dr.AcceptSocket.OnError(ex);
					_serverLog.Error(ex, "HandleDataRead error");
					return;
				}
				if (overs > 0)
				{
					dr.ResponseStream.Write(dr.Buffer, 0, overs);
					dr.OverZoreTimes = 0;
				}
				else if (++dr.OverZoreTimes > 10)
					return;

				dr.Over -= overs;
				if (dr.Over > 0)
				{
					dr.BeginRead();
				}
				else if (dr.Type == DataReadInfoType.Head)
				{
					var bodySizeBuffer = dr.ResponseStream.ToArray();
					if (int.TryParse(Encoding.UTF8.GetString(bodySizeBuffer, 0, bodySizeBuffer.Length), NumberStyles.HexNumber, null, out overs))
					{
						DataReadInfo drBody = new DataReadInfo(DataReadInfoType.Body, dr.AcceptSocket, dr.NetworkStream, 1024, overs - BaseSocket.HeadLength);
						drBody.BeginRead();
					}
				}
				else
				{
					dr.AcceptSocket.OnDataAvailable(dr);
				}
			}
		}
		static void MyHandleDataRead(IAsyncResult ar)
		{
			MyDataReadInfo dr = ar.AsyncState as MyDataReadInfo;
			if (dr.AcceptSocket._running)
			{
				int overs = 0;
				try
				{
					overs = dr.NetworkStream.EndRead(ar);
				}
				catch (Exception ex)
				{
					dr.AcceptSocket.OnError(ex);
					_serverLog.Error(ex, "MyHandleDataRead error");
					return;
				}
				if (overs == 0)
				{
					dr.AcceptSocket.Close();
					return;
				}

				//BaseSocket.WriteLog(BitConverter.ToString(dr.Buffer));
				dr.Over -= overs;
				if (dr.Over > 0)
				{
					//_serverLog.Information("已读取" + overs + "没有读取完" + dr.Over + "继续读取 " + dr.Buffer.Length);
					dr.TempStream.Write(dr.Buffer, 0, overs);//缓存收到的断包数据
					try
					{
						dr.BeginRead();
					}
					catch (Exception ex)
					{
						dr.AcceptSocket.OnError(ex);
						_serverLog.Error(ex, "Over BeginRead error");
						return;
					}
				}
				else if (dr.Type == DataReadInfoType.UnKnown)//未知类型的需要查找起始位
				{
					//可能有缓存的情况
					dr.TempStream.Write(dr.Buffer, 0, overs);
					dr.Buffer = dr.TempStream.ToArray();
					//_serverLog.Information("UnKnown-" + overs + "-" + dr.Buffer.Length + ":" + BitConverter.ToString(dr.Buffer));
					var StartBytes = new byte[] { 0xD5, 0xF0, 0x01, 0x00 };
					//找到起始标志位位置

					var startIndex = findBytes(dr.Buffer, StartBytes, 0);
					if (startIndex > 0)
						_serverLog.Information("start frame position -" + startIndex);
					if (startIndex > -1)
					{
						var dataLen = 0;
						if (dr.Buffer.Length > 10 + startIndex)
						{
							dataLen = BitConverter.ToUInt16(dr.Buffer, 10 + startIndex);
							overs = startIndex + 16 + dataLen - dr.Buffer.Length;
						}
						else
						{
							overs = BaseSocket.BuffLength;
						}
						if (overs > 0)
						{
							//_serverLog.Information("UnKnown-继续读取" + overs + "字节");
							//有未读完的数据
							MyDataReadInfo drBody = new MyDataReadInfo(overs == BaseSocket.BuffLength ? DataReadInfoType.UnKnown : DataReadInfoType.Body, dr.AcceptSocket, dr.NetworkStream, overs, overs);
							var availableBytes = dr.Buffer.Skip(startIndex).ToArray();
							drBody.TempStream.Write(availableBytes, 0, availableBytes.Length);//缓存有效数据位
							try
							{
								drBody.BeginRead();
							}
							catch (Exception ex)
							{
								dr.AcceptSocket.OnError(ex);
								_serverLog.Error(ex, "Body BeginRead error");
								return;
							}
						}
						else if (overs < 0)//1466里面有多帧数据
						{
							//先取前面一包数据去处理
							var temBuff = dr.Buffer.Skip(16 + dataLen).ToArray();
							dr.Buffer = dr.Buffer.Take(16 + dataLen).ToArray();
							dr.AcceptSocket.OnDataAvailable(dr);

							var secStartIndex = findBytes(temBuff, StartBytes, 0);
							if (secStartIndex > -1 && temBuff.Length > 10 + secStartIndex)
							{
								var dataLen2 = BitConverter.ToUInt16(temBuff, 10 + secStartIndex);
								overs = 16 + dataLen2 - temBuff.Length;
							}
							else
							{
								overs = BaseSocket.BuffLength;
							}

							//_serverLog.Information("UnKnown-继续读取" + overs + "字节");
							//有未读完的数据
							MyDataReadInfo drBody = new MyDataReadInfo(overs == BaseSocket.BuffLength ? DataReadInfoType.UnKnown : DataReadInfoType.Body, dr.AcceptSocket, dr.NetworkStream, Math.Abs(overs), Math.Abs(overs));
							drBody.TempStream.Write(temBuff, 0, temBuff.Length);//缓存有效数据位
							try
							{
								drBody.BeginRead();
							}
							catch (Exception ex)
							{
								dr.AcceptSocket.OnError(ex);
								_serverLog.Error(ex, "Body BeginRead error");
								return;
							}
						}
						else
						{
							//正好是一整包数据
							dr.AcceptSocket.OnDataAvailable(dr);
							dr.AcceptSocket.MyHandleDataReceived();
						}
					}
					else
					{
						dr.AcceptSocket.MyHandleDataReceived();//获取起始位异常
						return;
					}
				}
				// else if (dr.Type == DataReadInfoType.Head)
				// {
				// 	//头部12字节部分字节可能有缓存的情况
				// 	dr.TempStream.Write(dr.Buffer, 0, overs);
				// 	dr.Buffer = dr.TempStream.ToArray();
				// 	_serverLog.Information("Head-" + overs + ":" + BitConverter.ToString(dr.Buffer));
				// 	//判断有无获取数据错乱的情况
				// 	if (!(dr.Buffer[0] == BaseSocket.StartBytes[0] && dr.Buffer[1] == BaseSocket.StartBytes[1] && dr.Buffer[2] == BaseSocket.StartBytes[2] && dr.Buffer[3] == BaseSocket.StartBytes[3]))
				// 	{
				// 		_serverLog.Information("起始标志错误,重新获取");
				// 		dr.AcceptSocket.MyHandleDataReceived();
				// 		return;
				// 	}
				// 	else
				// 	{
				// 		overs = BitConverter.ToUInt16(dr.Buffer, 10) + 4;
				// 		MyDataReadInfo drBody = new MyDataReadInfo(DataReadInfoType.Body, dr.AcceptSocket, dr.NetworkStream, overs, overs);
				// 		drBody.TempStream.Write(dr.Buffer, 0, dr.Buffer.Length);//缓存头部12字节
				// 		try
				// 		{
				// 			drBody.BeginRead();
				// 		}
				// 		catch (Exception ex)
				// 		{
				// 			dr.AcceptSocket.OnError(ex);
				// 			_serverLog.Error(ex, "Body BeginRead error");
				// 			return;
				// 		}
				// 	}
				// }
				else
				{
					//头部12字节和可能有断包缓存的情况
					dr.TempStream.Write(dr.Buffer, 0, overs);
					dr.Buffer = dr.TempStream.ToArray();
					//_serverLog.Information("Body-" + overs + "-" + dr.Buffer.Length + ":" + BitConverter.ToString(dr.Buffer));
					dr.AcceptSocket.OnDataAvailable(dr);
					dr.AcceptSocket.MyHandleDataReceived();
				}
			}
		}

		public void Close()
		{
			if (this._running == true)
			{
				this._beginRead.AsyncWaitHandle.Close();

				this._running = false;
				if (this._tcpClient != null)
				{
					this._tcpClient.Dispose();
					this._tcpClient = null;
				}
				this.OnClosed();
				this._server.CloseClient(this);
				// int[] keys = new int[this._receiveHandlers.Count];
				// try
				// {
				// 	this._receiveHandlers.Keys.CopyTo(keys, 0);
				// }
				// catch
				// {
				// 	lock (this._receiveHandlers_lock)
				// 	{
				// 		keys = new int[this._receiveHandlers.Count];
				// 		this._receiveHandlers.Keys.CopyTo(keys, 0);
				// 	}
				// }
				// foreach (int key in keys)
				// {
				// 	SyncReceive receiveHandler = null;
				// 	if (this._receiveHandlers.TryGetValue(key, out receiveHandler))
				// 	{
				// 		receiveHandler.Wait.Set();
				// 	}
				// }
				// lock (this._receiveHandlers_lock)
				// {
				// 	this._receiveHandlers.Clear();
				// }
			}
		}

		public void Write(SocketMessager messager)
		{
			this.Write(messager, null, TimeSpan.Zero);
		}
		public void Write(SocketMessager messager, ReceiveEventHandler receiveHandler)
		{
			this.Write(messager, receiveHandler, TimeSpan.FromSeconds(20));
		}
		public void Write(SocketMessager messager, ReceiveEventHandler receiveHandler, TimeSpan timeout)
		{
			SyncReceive syncReceive = null;
			try
			{
				if (receiveHandler != null)
				{
					syncReceive = new SyncReceive(receiveHandler);
					// lock (this._receiveHandlers_lock)
					// {
					// 	if (!this._receiveHandlers.ContainsKey(messager.Id))
					// 	{
					// 		this._receiveHandlers.Add(messager.Id, syncReceive);
					// 	}
					// 	else
					// 	{
					// 		this._receiveHandlers[messager.Id] = syncReceive;
					// 	}
					// }
				}
				if (this._running)
				{
					lock (_write_lock)
					{
						NetworkStream ns = this._tcpClient.GetStream();
						base.WriteAsync(ns, messager);
					}
					this._lastActive = DateTime.Now;

					if (syncReceive != null)
					{
						syncReceive.Wait.Reset();
						syncReceive.Wait.WaitOne(timeout);
						syncReceive.Wait.Set();
						// lock (this._receiveHandlers_lock)
						// {
						// 	this._receiveHandlers.Remove(messager.Id);
						// }
					}
				}
			}
			catch (Exception ex)
			{
				this._running = false;
				this.OnError(ex);
				_serverLog.Error(ex, "Write error");
				if (syncReceive != null)
				{
					syncReceive.Wait.Set();
					// lock (this._receiveHandlers_lock)
					// {
					// 	this._receiveHandlers.Remove(messager.Id);
					// }
				}
			}
		}

		/// <summary>
		/// 拒绝访问，并关闭连接
		/// </summary>
		public void AccessDenied()
		{
			this._server.AccessDenied(this);
		}

		protected virtual void OnClosed()
		{
			try
			{
				this._server.OnClosed(this);
			}
			catch (Exception ex)
			{
				this.OnError(ex);
				_serverLog.Error(ex, "OnClosed error");
			}
		}

		protected virtual void OnReceive(ReceiveEventArgs e)
		{
			try
			{
				this._server.OnReceive2(e);
			}
			catch (Exception ex)
			{
				this.OnError(ex);
				_serverLog.Error(ex, "OnReceive2 error");
			}
		}

		protected virtual void OnError(Exception ex)
		{
			int errors = 0;
			lock (this._errors_lock)
			{
				errors = ++this._errors;
			}
			ErrorEventArgs e = new ErrorEventArgs(errors, ex, this);
			this._server.OnError2(e);
		}

		public int Id
		{
			get { return _id; }
		}

		class SyncReceive : IDisposable
		{
			private ReceiveEventHandler _receiveHandler;
			private ManualResetEvent _wait;

			public SyncReceive(ReceiveEventHandler onReceive)
			{
				this._receiveHandler = onReceive;
				this._wait = new ManualResetEvent(false);
			}

			public ManualResetEvent Wait
			{
				get { return _wait; }
			}
			public ReceiveEventHandler ReceiveHandler
			{
				get { return _receiveHandler; }
			}

			#region IDisposable 成员

			public void Dispose()
			{
				this._wait.Set();
			}

			#endregion
		}

		#region IDisposable 成员

		void IDisposable.Dispose()
		{
			this.Close();
		}

		#endregion
	}

	public delegate void ClosedEventHandler(object sender, ClosedEventArgs e);
	public delegate void AcceptedEventHandler(object sender, AcceptedEventArgs e);
	public delegate void ErrorEventHandler(object sender, ErrorEventArgs e);
	public delegate void ReceiveEventHandler(object sender, ReceiveEventArgs e);

	public class ClosedEventArgs : EventArgs
	{

		private int _accepts;
		private int _acceptSocketId;

		public ClosedEventArgs(int accepts, int acceptSocketId)
		{
			this._accepts = accepts;
			this._acceptSocketId = acceptSocketId;
		}

		public int Accepts
		{
			get { return _accepts; }
		}
		public int AcceptSocketId
		{
			get { return _acceptSocketId; }
		}
	}

	public class AcceptedEventArgs : EventArgs
	{

		private int _accepts;
		private AcceptSocket _acceptSocket;

		public AcceptedEventArgs(int accepts, AcceptSocket acceptSocket)
		{
			this._accepts = accepts;
			this._acceptSocket = acceptSocket;
		}

		public int Accepts
		{
			get { return _accepts; }
		}
		public AcceptSocket AcceptSocket
		{
			get { return _acceptSocket; }
		}
	}

	public class ErrorEventArgs : EventArgs
	{

		private int _errors;
		private Exception _exception;
		private AcceptSocket _acceptSocket;

		public ErrorEventArgs(int errors, Exception exception, AcceptSocket acceptSocket)
		{
			this._errors = errors;
			this._exception = exception;
			this._acceptSocket = acceptSocket;
		}

		public int Errors
		{
			get { return _errors; }
		}
		public Exception Exception
		{
			get { return _exception; }
		}
		public AcceptSocket AcceptSocket
		{
			get { return _acceptSocket; }
		}
	}

	public class ReceiveEventArgs : EventArgs
	{
		private ulong _receives;
		private SocketMessager _messager;
		private AcceptSocket _acceptSocket;

		public ReceiveEventArgs(ulong receives, SocketMessager messager, AcceptSocket acceptSocket)
		{
			this._receives = receives;
			this._messager = messager;
			this._acceptSocket = acceptSocket;
		}

		public ulong Receives
		{
			get { return _receives; }
		}
		public SocketMessager Messager
		{
			get { return _messager; }
		}
		public AcceptSocket AcceptSocket
		{
			get { return _acceptSocket; }
		}
	}
}
