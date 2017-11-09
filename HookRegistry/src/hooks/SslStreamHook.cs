using Hooks.PacketDumper;
using Mono.Security.Protocol.Tls;
using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;

namespace Hooks
{
	[RuntimeHook]
	class SslStreamHook
	{
		private bool _reentrant;

		private Type _asyncOPModel;
		private MethodInfo _asyncModelBuffer;
		private MethodInfo _asyncModelOffset;
		private MethodInfo _asyncModelCount;

		private FieldInfo _innerStream;
		private MethodInfo _networkSocket;

		public SslStreamHook()
		{
			HookRegistry.Register(OnCall);
			_reentrant = false;
			_asyncOPModel = null;

			InitDynamicTypes();
		}

		private void InitDynamicTypes()
		{
			if (HookRegistry.IsWithinUnity())
			{
				_innerStream = typeof(AuthenticatedStream).GetField("innerStream", BindingFlags.Instance | BindingFlags.NonPublic);
				if (_innerStream == null)
				{
					HookRegistry.Panic("innerStream == null");
				}

				_networkSocket = typeof(NetworkStream).GetProperty("Socket", BindingFlags.Instance | BindingFlags.NonPublic)?.GetGetMethod(true);
				if (_networkSocket == null)
				{
					HookRegistry.Panic("networkSocket == null");
				}

				_asyncOPModel = typeof(SslStreamBase).GetNestedType("InternalAsyncResult", BindingFlags.NonPublic);
				_asyncModelBuffer = _asyncOPModel.GetProperty("Buffer")?.GetGetMethod();
				_asyncModelOffset = _asyncOPModel.GetProperty("Offset")?.GetGetMethod();
				_asyncModelCount = _asyncOPModel.GetProperty("Count")?.GetGetMethod();
				if (_asyncOPModel == null)
				{
					HookRegistry.Panic("asyncOPModel == null!");
				}

				if (_asyncModelBuffer == null)
				{
					HookRegistry.Panic("asyncModelBuffer == null!");
				}

				if (_asyncModelOffset == null)
				{
					HookRegistry.Panic("asyncModelOffset == null!");
				}

				if (_asyncModelCount == null)
				{
					HookRegistry.Panic("asyncModelCount == null!");
				}
			}
		}

		public static string[] GetExpectedMethods()
		{
			return new string[] {
				"System.Net.Security.SslStream::EndWrite",
				"System.Net.Security.SslStream::EndRead",
				// Synchronous methods call the asynchronous methods within the method body.
				// It's not necessary to hook the read and write methods themselves.
				// "System.Net.Security.SslStream::Read",
				// "System.Net.Security.SslStream::Write",
				};
		}

		#region PROXY

		private object ProxyEndWrite(object stream, object[] args)
		{
			MethodInfo writeMethod = typeof(SslStream).GetMethod("EndWrite");
			return writeMethod.Invoke(stream, args);
		}

		private object ProxyEndRead(object stream, object[] args)
		{
			MethodInfo readMethod = typeof(SslStream).GetMethod("EndRead");
			return readMethod.Invoke(stream, args);
		}

		private Socket GetUnderlyingSocket(object stream)
		{
			var baseStream = (Stream)_innerStream.GetValue(stream);
			var netStream = baseStream as NetworkStream;
			if (netStream == null)
			{
				HookRegistry.Panic("Underlying stream is NOT a network stream!");
			}

			var underlyingSocket = (Socket)_networkSocket.Invoke(netStream, new object[0] { });
			if (underlyingSocket == null)
			{
				HookRegistry.Panic("Couldn't find underlying socket!");
			}

			return underlyingSocket;
		}

		#endregion

		#region DYNAMIC

		private byte[] GetAsyncBuffer(IAsyncResult model)
		{
			return (byte[])_asyncModelBuffer.Invoke(model, new object[0] { });
		}

		private int GetAsyncOffset(IAsyncResult model)
		{
			return (int)_asyncModelOffset.Invoke(model, new object[0] { });
		}

		private int GetAsyncCount(IAsyncResult model)
		{
			return (int)_asyncModelCount.Invoke(model, new object[0] { });
		}

		#endregion

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if (typeName != "System.Net.Security.SslStream" ||
				(methodName != "EndWrite" && methodName != "EndRead"))
			{
				return null;
			}

			if (_reentrant == true)
			{
				return null;
			}

			/* Actual hook code */

			_reentrant = true;

			bool isWriting = methodName.EndsWith("Write");
			object OPresult = null;
			var dumpServer = DumpServer.Get();

			var asyncResult = args[0] as IAsyncResult;
			// These variables have a different meaning depending on the operation; read or write.
			byte[] buffer = GetAsyncBuffer(asyncResult);
			// Offset in buffer where relevant data starts.
			int offset = GetAsyncOffset(asyncResult);
			// Amount of bytes encoding the relevant data (starting from offset).
			int count = GetAsyncCount(asyncResult);

			Socket underlyingSocket = GetUnderlyingSocket(thisObj);
			dumpServer.PreparePartialBuffers(underlyingSocket, true);

			if (isWriting)
			{
				OPresult = ProxyEndWrite(thisObj, args);
				// Buffer holds written data,
				// offset holds offset within buffer where writing started,
				// count holds amount of written bytes.
			}
			else
			{
				int readBytes = (int)ProxyEndRead(thisObj, args);
				OPresult = readBytes;
				// Buffer holds read data,
				// offset holds offset within buffer where reading started,
				// count holds size of buffer.

				count = readBytes; // Reassigned
			}

			// We can assume the async operation succeeded.			
			if (buffer != null)
			{
				dumpServer.PartialData(underlyingSocket, !isWriting, buffer, offset, count, true);
			}
			else
			{
				HookRegistry.Panic("buffer == null!");
			}


			// Short circuit original method; this prevents executing the method twice.
			_reentrant = false;
			return OPresult;
		}
	}
}
