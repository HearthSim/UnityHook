using Hooks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

#if false

namespace Hooks
{
	[RuntimeHook]
	class ExampleHook
	{
		private bool _reentrant;

		public ExampleHook()
		{
			HookRegistry.Register(OnCall);
		}

		private void InitDynamicTypes() { }

		public static string[] GetExpectedMethods()
		{
			return new string[] { };
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			return null;
		}
	}
}

#endif
