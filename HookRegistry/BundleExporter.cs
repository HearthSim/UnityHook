using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hooks
{
	[RuntimeHook]
	class BundleExporter
	{

		public BundleExporter()
		{
			HookRegistry.Register(OnCall);
		}


		private bool intercept = true;

		object OnCall(string typeName, string methodName, object thisObj, params object[] args)
		{
			if (typeName != "Network" || methodName != "GetBattlePayConfigResponse")
			{
				return null;
			}

			if (!intercept)
			{
				return null;
			}

			// perform the real call
			intercept = false;
			Network.BattlePayConfig battlePayConfigResponse = Network.GetBattlePayConfigResponse();

			// convert to json and save
			JsonSerializerSettings s = new JsonSerializerSettings();
			s.FloatParseHandling = FloatParseHandling.Decimal;
			File.WriteAllText("BattlePayConfigResponse.json", JsonConvert.SerializeObject(battlePayConfigResponse, s));

			// return real value
			return battlePayConfigResponse;
		}

	}
}
