using System;
using System.IO;
using System.Reflection;

namespace Hooks
{
	[RuntimeHook]
	class PlayMakerFSM
	{

		private bool reentrant;

		private Type TypeReflectionUtils;

		public PlayMakerFSM(bool initDynamicCalls)
		{
			HookRegistry.Register(OnCall);
			reentrant = false;

			if (initDynamicCalls)
			{
				PrepareDynamicCalls();
			}

		}

		private void PrepareDynamicCalls()
		{
			var loc = Path.Combine(HookRegistry.LibLocation, HookRegistry.LIB_PLAYMAKER_NAME);
			var libAssembly = Assembly.LoadFrom(loc);

			TypeReflectionUtils = libAssembly.GetType("HutongGames.PlayMaker.ReflectionUtils");
		}

		private object ProxyCall(object thisObj, object[] args)
		{
			object retValue = null;
			try
			{
				// Get the GetGlobalType static method.
				var method = TypeReflectionUtils.GetMethod("GetGlobalType",
														   BindingFlags.Static | BindingFlags.Public);
				// Invoke it.
				retValue = method.Invoke(null, args);

			}
			catch (TargetInvocationException e)
			{
				var reflException = e.InnerException;
				var msg = string.Format("Error in `HutongGames.PlayMaker.ReflectionUtils::GetGlobalType` for param {0}",
										args[0]);
				HookRegistry.Get().Log(msg);
				HookRegistry.Get().Log(e.ToString());

				if (reflException is ReflectionTypeLoadException)
				{
					HookRegistry.Get().Log("Dumping all types that were tried loading:");
					var strTypes = "";
					var typeArray = (reflException as ReflectionTypeLoadException).Types;
					foreach (var tp in typeArray)
					{
						if (tp == null)
						{
							strTypes += "NULL - ERROR\n";
						}
						else
						{
							strTypes += tp.FullName + "\n";
						}
					}
					HookRegistry.Get().Log(strTypes);


					HookRegistry.Get().Log("Dumping all loaded assemblies:");
					var strAssemblies = "";
					var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
					foreach (var assembly in allAssemblies)
					{
						strAssemblies += assembly.FullName + "\n";
					}
					HookRegistry.Get().Log(strAssemblies);
				}
			}

			return retValue;
		}

		object OnCall(string typeName, string methodName, object thisObj, object[] args)
		{
			if (typeName != "HutongGames.PlayMaker.ReflectionUtils" || methodName != "GetGlobalType")
			{
				return null;
			}

			if (reentrant)
			{
				return null;
			}

			reentrant = true;

			object retValue = ProxyCall(thisObj, args);

			reentrant = false;

			return retValue;
		}
	}
}
