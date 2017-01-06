// This hook causes Hearthstone to communicate in plaintext, without using TLS/SSL.
// NOTE: This is designed for use with 3rd party servers that don't support TLS connections
// and will cause the Hearthstone client to fail to connect to an official server, therefore
// it is disabled by default.
// To enable this hook, add "BattleNetCSharp.Init" to example_hooks

using bgs;

namespace Hooks
{
	[RuntimeHook]
	class SSLDisable
	{
		private bool reentrant = false;

		public SSLDisable()
		{
			HookRegistry.Register(OnCall);
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if (typeName != "bgs.BattleNetCSharp" || methodName != "Init")
			{
				return null;
			}

			if (reentrant)
				return null;

			reentrant = true;

			var bnet = (BattleNetCSharp)thisObj;

			// disable SSL
			var sslParams = (SslParameters)args[4];
			sslParams.useSsl = false;

			// perform the real call
			bool result = bnet.Init((bool)args[0], (string)args[1], (string)args[2], (int)args[3], sslParams);

			return result;
		}
	}
}
