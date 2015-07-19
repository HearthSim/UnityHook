using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Hooks
{
	[RuntimeHook]
	class UseWebAuth
	{
		public UseWebAuth()
		{
			HookRegistry.Register(OnCall);
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if (typeName != "BattleNetCSharp" || methodName != "AuroraStateHandler_WaitForLogon")
			{
				return null;
			}

			var bnet = (BattleNetCSharp)thisObj;
			var challenge = typeof(ChallengeAPI).GetField("m_nextExternalChallenge", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bnet.Challenge) as ExternalChallenge;
			if (challenge != null)
			{
				var url = "";
				Log.Bob.Print("UseWebAuth: CheckWebAuth returned {0}, {1}", bnet.CheckWebAuth(out url), url);
			}
			return null;
		}
	}
}
