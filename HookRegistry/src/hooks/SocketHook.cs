using Hooks.PacketDumper;
using System;
using System.Net.Sockets;
using System.Reflection;

namespace Hooks
{
	[RuntimeHook]
	class SocketHook
	{
		private bool _reentrant;

		private Type _asyncOPModel;
		private FieldInfo _asyncModelBuffer;
		private FieldInfo _asyncModelOffset;
		private FieldInfo _asyncModelRequestedBytes;

		public SocketHook()
		{
			HookRegistry.Register(OnCall);

			_reentrant = false;

			InitDynamicTypes();
		}

		private void InitDynamicTypes()
		{
			if (HookRegistry.IsWithinUnity())
			{
				_asyncOPModel = typeof(Socket).GetNestedType("SocketAsyncResult", BindingFlags.NonPublic);

				_asyncModelBuffer = _asyncOPModel.GetField("Buffer", BindingFlags.Instance | BindingFlags.Public);
				_asyncModelOffset = _asyncOPModel.GetField("Offset", BindingFlags.Instance | BindingFlags.Public);
				_asyncModelRequestedBytes = _asyncOPModel.GetField("Size", BindingFlags.Instance | BindingFlags.Public);
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

				if (_asyncModelRequestedBytes == null)
				{
					HookRegistry.Panic("asyncModelRequestedBytes == null!");
				}
			}
		}

		public static string[] GetExpectedMethods()
		{
			return new string[] {
				"System.Net.Sockets.Socket::EndSend",
				"System.Net.Sockets.Socket::EndReceive",
				};
		}

		#region PROXY

		private Type[] GetParamTypeArray(object[] args)
		{
			Type[] result = new Type[args.Length];
			for (int i = 0; i < args.Length; ++i)
			{
				result[i] = args[i]?.GetType();
			}

			return result;
		}

		private object ProxyEndWrite(object socket, object[] args)
		{
			MethodInfo writeMethod = typeof(Socket).GetMethod("EndSend", GetParamTypeArray(args));
			return writeMethod.Invoke(socket, args);
		}

		private object ProxyEndRead(object socket, object[] args)
		{
			MethodInfo readMethod = typeof(Socket).GetMethod("EndReceive", GetParamTypeArray(args));
			return readMethod.Invoke(socket, args);
		}

		#endregion

		#region DYNAMIC

		private byte[] GetAsyncBuffer(IAsyncResult model)
		{
			return (byte[])_asyncModelBuffer.GetValue(model);
		}

		private int GetAsyncOffset(IAsyncResult model)
		{
			return (int)_asyncModelOffset.GetValue(model);
		}

		private int GetAsyncRequestedBytes(IAsyncResult model)
		{
			return (int)_asyncModelRequestedBytes.GetValue(model);
		}

		#endregion

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if (typeName != "System.Net.Sockets.Socket" ||
				(methodName != "EndSend" && methodName != "EndReceive"))
			{
				return null;
			}

			if (_reentrant == true)
			{
				return null;
			}

			/* Actual hook code */

			_reentrant = true;

			bool isWriting = methodName.EndsWith("Send");
			object OPResult = null;
			var dumpServer = DumpServer.Get();

			var asyncResult = args[0] as IAsyncResult;
			// These variables have a different meaning depending on the operation; read or write.
			byte[] buffer = GetAsyncBuffer(asyncResult);
			// Offset in buffer where relevant data starts.
			int offset = GetAsyncOffset(asyncResult);
			int requestedBytes = GetAsyncRequestedBytes(asyncResult);
			// Amount of bytes actually processed by the operation.
			int processedBytes = 0;

			var thisSocket = thisObj as Socket;
			if (thisSocket == null)
			{
				HookRegistry.Panic("`thisObj` is NOT a Socket object");
			}

			dumpServer.PreparePartialBuffers(thisSocket, false);

			if (isWriting)
			{
				int sentBytes = (int)ProxyEndWrite(thisObj, args);
				OPResult = sentBytes;
				processedBytes = sentBytes;
			}
			else
			{
				int readBytes = (int)ProxyEndRead(thisObj, args);
				OPResult = readBytes;
				processedBytes = readBytes;
			}

			if (buffer != null)
			{
				HookRegistry.Log("Offset: {0}, buffsize: {1}, element: {2}", offset, buffer.Length, processedBytes);
				dumpServer.PartialData(thisSocket, !isWriting, buffer, offset, processedBytes, false);
			}
			else
			{
				HookRegistry.Panic("buffer == null");
			}


			_reentrant = false;
			return OPResult;
		}
	}
}

