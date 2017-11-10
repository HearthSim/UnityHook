using Hooks.PacketDumper;
using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

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

		private MethodInfo[] _sendProxy;
		private MethodInfo[] _readProxy;

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
					HookRegistry.Panic("SocketHook - asyncOPModel == null!");
				}

				if (_asyncModelBuffer == null)
				{
					HookRegistry.Panic("SocketHook - asyncModelBuffer == null!");
				}

				if (_asyncModelOffset == null)
				{
					HookRegistry.Panic("SocketHook - asyncModelOffset == null!");
				}

				if (_asyncModelRequestedBytes == null)
				{
					HookRegistry.Panic("SocketHook - asyncModelRequestedBytes == null!");
				}

				// Hardcode proxy methods, because it's tough to match on methods when you have implementing
				// types as arguments.
				Type[] singleArg = new Type[] { typeof(IAsyncResult) };
				Type[] doubleArg = new Type[] { typeof(IAsyncResult), typeof(SocketError).MakeByRefType() };

				_sendProxy = new MethodInfo[2];
				_sendProxy[0] = typeof(Socket).GetMethod("EndSend", BindingFlags.Public | BindingFlags.Instance, null, singleArg, null);
				if (_sendProxy[0] == null) HookRegistry.Panic("SocketHook - EP1");
				_sendProxy[1] = typeof(Socket).GetMethod("EndSend", BindingFlags.Public | BindingFlags.Instance, null, doubleArg, null);
				if (_sendProxy[1] == null) HookRegistry.Panic("SocketHook - EP2");

				_readProxy = new MethodInfo[2];
				_readProxy[0] = typeof(Socket).GetMethod("EndReceive", BindingFlags.Public | BindingFlags.Instance, null, singleArg, null);
				if (_readProxy[0] == null) HookRegistry.Panic("SocketHook - EP3");
				_readProxy[1] = typeof(Socket).GetMethod("EndReceive", BindingFlags.Public | BindingFlags.Instance, null, doubleArg, null);
				if (_readProxy[1] == null) HookRegistry.Panic("SocketHook - EP4");
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

		//private Type[] GetParamTypeArray(object[] args)
		//{
		//	Type[] result = new Type[args.Length];
		//	for (int i = 0; i < args.Length; ++i)
		//	{
		//		result[i] = args[i]?.GetType();
		//	}

		//	return result;
		//}

		private object ProxyEndWrite(object socket, ref object[] args)
		{
			MethodInfo writeMethod = _sendProxy[args.Length - 1];
			if (writeMethod == null)
			{
				HookRegistry.Panic("SocketHook - writeMethod == null");
			}
			return writeMethod.Invoke(socket, args);
		}

		private object ProxyEndRead(object socket, ref object[] args)
		{
			MethodInfo readMethod = _readProxy[args.Length - 1];
			if (readMethod == null)
			{
				HookRegistry.Panic("SocketHook - readMethod == null");
			}
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

		object OnCall(string typeName, string methodName, object thisObj, object[] args, IntPtr[] refArgs, int[] refIdxMatch)
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

			bool isOutgoing = methodName.EndsWith("Send");
			int OPResult = 0;
			var dumpServer = DumpServer.Get();
			// True if we need to feedback the error code returned by the proxy method.
			bool feedbackErrorCode = false;

			var thisSocket = thisObj as Socket;
			if (thisSocket == null)
			{
				HookRegistry.Panic("SocketHook - `thisObj` is NOT a Socket object");
			}

			// We expect 1 referenceArgument, which makes the total passed argument equal to 2.
			// The argument in question is `out SocketError`
			if (refArgs != null && refArgs.Length == 1)
			{
				if (args.Length != 2 || refIdxMatch == null || refIdxMatch[0] != 1)
				{
					string message = String.Format("SocketHook - {0} - Got 1 reference argument, but total arguments don't match: {1} <=> 2",
						thisSocket.GetHashCode(), args.Length);
					HookRegistry.Panic(message);
				}
				else
				{
					HookRegistry.Debug("SocketHook - {0} - Valid referenced argument!", thisSocket.GetHashCode());
					feedbackErrorCode = true;
				}
			}

			dumpServer.PreparePartialBuffers(thisSocket, false);

			if (isOutgoing)
			{
				int sentBytes = (int)ProxyEndWrite(thisObj, ref args);

				// buffer holds the transmitted contents.
				// requestedBytes holds the amount of bytes requested when starting the operation.
				//	=> This amount gets decreased, towards 0, each time bytes are sent.
				//	=> The actual amount of bytes sent are found inside sentBytes.
				// offset is the starting offset, within buffer, of data to be written when starting
				// the operation.
				//	=> This amount gets increased, towards orignal value of size, each time bytes are sent.
				//	=> The actual offset would then be (offset-sentBytes)!

				OPResult = sentBytes;

				//processedBytes = sentBytes;
				//// Update offset parameter.
				//offset = offset - sentBytes;
			}
			else
			{
				int readBytes = (int)ProxyEndRead(thisObj, ref args);
				OPResult = readBytes;
				//processedBytes = readBytes;
			}

			var asyncResult = args[0] as IAsyncResult;
			if(asyncResult == null)
			{
				HookRegistry.Panic("SocketHook - asyncResult == null");
			}
			// These variables have a different meaning depending on the operation; read or write.
			byte[] buffer = GetAsyncBuffer(asyncResult);
			// Offset in buffer where relevant data starts.
			int offset = GetAsyncOffset(asyncResult);
			int requestedBytes = GetAsyncRequestedBytes(asyncResult);
			// Amount of bytes actually processed by the operation.
			int processedBytes = OPResult;

			if(offset + processedBytes > buffer.Length)
			{
				offset -= processedBytes;
			}

			if (buffer != null)
			{
				// HookRegistry.Log("Offset: {0}, buffsize: {1}, element: {2}", offset, buffer.Length, processedBytes);
				dumpServer.PartialData(thisSocket, !isOutgoing, buffer, offset, processedBytes, false);
			}
			else
			{
				HookRegistry.Debug("SocketHook - {0} - buffer == null", thisSocket.GetHashCode());
			}

			if (feedbackErrorCode == true)
			{
				var errorCode = (SocketError)args[1];
				IntPtr errorCodePtr = refArgs[0];
				HookRegistry.Debug("SocketHook - {0} - Writing `{1}` to refPtr", thisSocket.GetHashCode(), errorCode.ToString());
				Marshal.StructureToPtr(errorCode, errorCodePtr, true);
			}

			_reentrant = false;
			return OPResult;
		}
	}
}

