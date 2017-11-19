using Hooks.PacketDumper;
using System;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hooks
{
	[RuntimeHook]
	class SocketHook
	{
		private Type _asyncOPModel;
		private FieldInfo _asyncModelBuffer;
		private FieldInfo _asyncModelOffset;
		private FieldInfo _asyncModelRequestedBytes;

		private MethodInfo[] _sendProxy;
		private MethodInfo[] _readProxy;

		private Map<int, Semaphore> _reentrantStructs;

		public SocketHook()
		{
			HookRegistry.Register(OnCall);

			_reentrantStructs = new Map<int, Semaphore>();

			InitDynamicTypes();
		}

		#region SETUP

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

		#endregion

		#region PROXY

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

		// Returns TRUE if we're alowed to enter the hook, false otherwise.
		// This method uses a semaphore to limit reentrancy, there is no semaphoreslim alternative
		// AFAIK.
		private bool ReentrantEnter(object thisObj)
		{
			int key = thisObj.GetHashCode();
			Semaphore barrier;
			_reentrantStructs.TryGetValue(key, out barrier);
			if (barrier == null)
			{
				barrier = new Semaphore(1, 1);
				_reentrantStructs[key] = barrier;
			}

			// Tests for the signal and returns immediately.
			// This decreases the semaphore count (if not already at 0).
			return barrier.WaitOne(0);
		}

		private void ReentrantLeave(object thisObj)
		{
			int key = thisObj.GetHashCode();
			Semaphore barrier = _reentrantStructs[key];
			// Increases semaphore count (if not already at maximum).
			barrier.Release();
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args, IntPtr[] refArgs, int[] refIdxMatch)
		{
			if (typeName != "System.Net.Sockets.Socket" ||
				(methodName != "EndSend" && methodName != "EndReceive"))
			{
				return null;
			}

			// Socket is a low-level construct so we must guard ourselves robustly against race conditions.
			if (!ReentrantEnter(thisObj))
			{
				return null;
			}

			/* Actual hook code */

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

			// Fetch the asyncModel early to prevent it being cleaned up
			// directly after operation end.
			var asyncResult = args[0] as IAsyncResult;
			if (asyncResult == null)
			{
				HookRegistry.Panic("SocketHook - asyncResult == null");
			}

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

			// These variables have a different meaning depending on the operation; read or write.
			byte[] buffer = GetAsyncBuffer(asyncResult);
			// Offset in buffer where relevant data starts.
			int offset = GetAsyncOffset(asyncResult);
			int requestedBytes = GetAsyncRequestedBytes(asyncResult);
			// Amount of bytes actually processed by the operation.
			int processedBytes = OPResult;

			if (buffer != null)
			{
				if (offset + processedBytes > buffer.Length)
				{
					offset -= processedBytes;
				}

				// HookRegistry.Log("SocketHook - {0} - writing", thisSocket.GetHashCode());
				dumpServer.PartialData(thisSocket, !isOutgoing, buffer, offset, processedBytes, isWrapping: false, singleDecode: false);
			}
			else
			{
				HookRegistry.Log("SocketHook - {0} - buffer == null", thisSocket.GetHashCode());
			}

			if (feedbackErrorCode == true)
			{
				var errorCode = (SocketError)args[1];
				IntPtr errorCodePtr = refArgs[0];
				HookRegistry.Debug("SocketHook - {0} - Writing `{1}` to refPtr", thisSocket.GetHashCode(), errorCode.ToString());
				Marshal.StructureToPtr(errorCode, errorCodePtr, true);
			}

			ReentrantLeave(thisObj);
			return OPResult;
		}
	}
}

