using Hooks.PacketDumper;

namespace Hooks
{
	[RuntimeHook]
	class SSLStream
	{
		public SSLStream()
		{
		}

		public static string[] GetExpectedMethods()
		{
			return new string[] {"System.Net.Security.SslStream::BeginWrite"};
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if(typeName != "System.Net.Security.SslStream" || methodName != "BeginWrite") {
				return null;
			}

			// Just start the dumpserver as a test.
			var dumper = DumpServer.Get();
			HookRegistry.Get().Log("SslStream beginWrite HIT");

			return null;
		}
	}
}
