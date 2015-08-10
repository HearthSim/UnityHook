using System;
using System.Reflection;

namespace Hooks
{
	[RuntimeHookAttribute]
	public class BackendSwitcher
	{
		public BackendSwitcher()
		{
			HookRegistry.Register (OnCall);
		}

		object OnCall(string typeName, string methodName, object thisObj, params object[] args) {
			if (typeName != "BattleNet" || methodName != ".cctor") {
				return null;
			}
			string backend = Vars.Key("Aurora.Backend").GetStr("BattleNetDll");

			IBattleNet impl;
			if (backend == "BattleNetDll") {
				impl = new BattleNetDll ();
			} else if (backend == "BattleNetCSharp") {
				impl = new BattleNetCSharp();
			} else {
				throw new NotImplementedException("Invalid Battle.net Backend (Aurora.Backend)");
			}
			typeof(BattleNet).GetField("s_impl", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, impl);
			Log.BattleNet.Print("Forced BattleNet backend to {0}", backend);
			return 1;
		}
	}
}