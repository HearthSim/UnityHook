using Hooks.PacketDumper;
using System;
using System.Net.Security;
using System.Reflection;

namespace Hooks
{
	[RuntimeHook]
	class SslStreamHook
	{
		private bool _reentrant;

		public SslStreamHook()
		{
			_reentrant = false;
		}

		public static string[] GetExpectedMethods()
		{
			return new string[] {"System.Net.Security.SslStream::BeginWrite"};
		}

		private object ProxyBeginWrite(object stream, object[] args)
		{
			MethodInfo writeMethod = typeof(SslStream).GetMethod("BeginWrite");
			return writeMethod.Invoke(stream, args);
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if(typeName != "System.Net.Security.SslStream" || methodName != "BeginWrite") {
				return null;
			}

			if(_reentrant == true)
			{
				return null;
			}

			_reentrant = true;

			var asyncResult = (IAsyncResult)ProxyBeginWrite(thisObj, args);

			// Just start the dumpserver as a test.
			var dumper = DumpServer.Get();
			HookRegistry.Log("SslStream beginWrite HIT");

			_reentrant = false;

			// Short circuit original method; this prevents writing twice.
			return asyncResult;
		}
	}
}
