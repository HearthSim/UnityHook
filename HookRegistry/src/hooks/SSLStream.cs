using Hooks.PacketDumper;
using Mono.Security.Protocol.Tls;
using System;
using System.Net.Security;
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
				_asyncOPModel = typeof(SslStreamBase).GetNestedType("InternalAsyncResult", BindingFlags.NonPublic);
				_asyncModelBuffer = _asyncOPModel.GetProperty("Buffer")?.GetGetMethod();
				_asyncModelOffset = _asyncOPModel.GetProperty("Offset")?.GetGetMethod();
				_asyncModelCount = _asyncOPModel.GetProperty("Count")?.GetGetMethod();

				if (_asyncOPModel == null)
				{
					HookRegistry.Log("asyncOPModel == null!");
				}

				if (_asyncModelBuffer == null)
				{
					HookRegistry.Log("asyncModelBuffer == null!");
				}

				if (_asyncModelOffset == null)
				{
					HookRegistry.Log("asyncModelOffset == null!");
				}

				if (_asyncModelCount == null)
				{
					HookRegistry.Log("asyncModelCount == null!");
				}
			}
		}

		public static string[] GetExpectedMethods()
		{
			return new string[] {"System.Net.Security.SslStream::EndWrite",
				"System.Net.Security.SslStream::EndRead"};
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

			bool isWriting = methodName.Equals("EndWrite");
			object OPresult = 0;

			var asyncResult = args[0] as IAsyncResult;
			byte[] buffer = GetAsyncBuffer(asyncResult);
			int offset = GetAsyncOffset(asyncResult);
			int count = GetAsyncCount(asyncResult);

			if (isWriting)
			{
				OPresult = ProxyEndWrite(thisObj, args);
			}
			else
			{
				OPresult = ProxyEndRead(thisObj, args);
			}

			// We can assume the async operation succeeded.			
			if (buffer != null)
			{
				// Just start the dumpserver as a test.
				var dumper = DumpServer.Get();
				if (isWriting)
				{
					HookRegistry.Log(String.Format("SSLStream WRITE sending to dumper: ({0}, {1})", offset, count));
				}
				else
				{
					HookRegistry.Log(String.Format("SSLStream READ sending to dumper: ({0}, {1})", offset, count));
				}

				// dumper.SendPartialData(thisObj, !isWriting, buffer, offset, count);
			}
			else
			{
				HookRegistry.Log("Error trying to extract Buffer field from async operation!");
			}

			// Short circuit original method; this prevents executing the method twice.
			_reentrant = false;
			return OPresult;
		}
	}
}
