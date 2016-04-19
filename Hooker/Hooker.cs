using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using System.IO;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace Hooker
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 2)
			{
				Console.WriteLine("Usage: Hooker.exe [GameName_Data directory] [hooks file]");
				return;
			}
			var dataPath = args[0];
			foreach (var s in new[] { "Assembly-CSharp-firstpass", "Assembly-CSharp" }) {
				var inStream = File.Open(s + ".dll", FileMode.Open, FileAccess.Read);
				var scriptAssembly = AssemblyDefinition.ReadAssembly(inStream);
				var hooker = new Hooker(scriptAssembly.MainModule);
				using (var fs = new FileStream(args[1], FileMode.Open, FileAccess.Read))
				using (var sr = new StreamReader(fs))
					while (!sr.EndOfStream) {
						var line = sr.ReadLine();
						var dotI = line.IndexOf('.');
						if (dotI > 0) {
							hooker.AddHookBySuffix(line.Substring(0, dotI), line.Substring(dotI + 1));
						}
					}

				scriptAssembly.Write(s + ".out.dll");
			}

			foreach (var assemblyName in new []{"Assembly-CSharp", "Assembly-CSharp-firstpass", "HookRegistry", "Newtonsoft.Json"})
			{
				var srcName = assemblyName + ".dll";
				if (File.Exists(assemblyName + ".out.dll"))
				{
					srcName = assemblyName + ".out.dll";
				}
				File.Copy(srcName, Path.Combine(dataPath, @"Managed", assemblyName + ".dll"), true);
			}
		}
	}

	class Hooker
	{
		public ModuleDefinition Module { get; private set; }
		TypeReference hookRegistryType;
		TypeReference rmhType;
		MethodReference onCallMethod;

		public Hooker(ModuleDefinition module)
		{
			Module = module;

			hookRegistryType = Module.Import(typeof(Hooks.HookRegistry));
			rmhType = Module.Import(typeof(System.RuntimeMethodHandle));
			onCallMethod = Module.Import(
				typeof(Hooks.HookRegistry).GetMethods()
				.First(mi => mi.Name.Contains("OnCall")));
			Module.AssemblyReferences.Add(new AssemblyNameReference("HookRegistry", new Version(1, 0, 0, 0)));
		}

		public void AddHookBySuffix(string typeName, string methodName)
		{
			var matchingTypes = Module.Types.Where(t =>
			{
				var idx = t.Name.IndexOf(typeName);
				return idx < 0 ? false : idx == 0 || t.Name[idx - 1] == '.';
			});
			var found = false;
			foreach (var type in matchingTypes)
			{
				foreach (var method in type.Methods.Where(m => m.Name == methodName))
				{
					AddHook(method);
					found = true;
				}
			}
			if (!found)
			{
				Console.WriteLine("Cannot find any method matching {0}.{1}", typeName, methodName);
			}
		}

		public void AddHook(MethodDefinition method)
		{
			if (!method.HasBody)
			{
				Console.WriteLine("Cannot hook method `{0}`", method);
				return;
			}
			if (method.HasGenericParameters)
			{
				// TODO: check if this hook procedure works with generics as-is
				throw new InvalidOperationException("Generic parameters not supported");
			}
			Console.WriteLine("Hooking method `{0}`", method);
			// object[] interceptedArgs;
			// object hookResult;
			var interceptedArgs = new VariableDefinition("interceptedArgs", method.Module.TypeSystem.Object.MakeArrayType());
			var hookResult = new VariableDefinition("hookResult", method.Module.TypeSystem.Object);
			method.Body.Variables.Add(interceptedArgs);
			method.Body.Variables.Add(hookResult);
			var numArgs = method.Parameters.Count;
			var hook = new List<Instruction>();
			// interceptedArgs = new object[numArgs];
			hook.Add(Instruction.Create(OpCodes.Ldc_I4, numArgs));
			hook.Add(Instruction.Create(OpCodes.Newarr, Module.TypeSystem.Object));
			hook.Add(Instruction.Create(OpCodes.Stloc, interceptedArgs));

			// rmh = methodof(this).MethodHandle;
			hook.Add(Instruction.Create(OpCodes.Ldtoken, method));

			// thisObj = static ? null : this;
			if (!method.IsStatic)
			{
				hook.Add(Instruction.Create(OpCodes.Ldarg_0));
			}
			else
			{
				hook.Add(Instruction.Create(OpCodes.Ldnull));
			}

			var i = 0;
			foreach (var param in method.Parameters)
			{
				// interceptedArgs[i] = (object)arg;
				hook.Add(Instruction.Create(OpCodes.Ldloc, interceptedArgs));
				hook.Add(Instruction.Create(OpCodes.Ldc_I4, i));
				hook.Add(Instruction.Create(OpCodes.Ldarg, param));
				if (param.ParameterType.IsByReference)
				{
					// if the arg is a reference type, it must be copied and boxed
					var refType = (ByReferenceType)param.ParameterType;
					hook.Add(Instruction.Create(OpCodes.Ldobj, refType.ElementType));
					hook.Add(Instruction.Create(OpCodes.Box, refType.ElementType));
				}
				else if (param.ParameterType.IsValueType)
				{
					// if the arg descends from ValueType, it must be boxed to be
					// converted to an object:
					hook.Add(Instruction.Create(OpCodes.Box, param.ParameterType));
				}
				hook.Add(Instruction.Create(OpCodes.Stelem_Ref));
				i++;
			}
			// hookResult = HookRegistry.OnCall(rmh, thisObj, interceptedArgs);
			hook.Add(Instruction.Create(OpCodes.Ldloc, interceptedArgs));
			hook.Add(Instruction.Create(OpCodes.Call, onCallMethod));
			hook.Add(Instruction.Create(OpCodes.Stloc, hookResult));
			// if (hookResult != null) {
			//     return (ReturnType)hookResult;
			// }
			hook.Add(Instruction.Create(OpCodes.Ldloc, hookResult));
			hook.Add(Instruction.Create(OpCodes.Ldnull));
			hook.Add(Instruction.Create(OpCodes.Ceq));
			hook.Add(Instruction.Create(OpCodes.Brtrue_S, method.Body.Instructions.First()));
			if (!method.ReturnType.FullName.EndsWith("Void"))
			{
				hook.Add(Instruction.Create(OpCodes.Ldloc, hookResult));
				hook.Add(Instruction.Create(OpCodes.Castclass, method.ReturnType));
				hook.Add(Instruction.Create(OpCodes.Unbox_Any, method.ReturnType));
			}
			hook.Add(Instruction.Create(OpCodes.Ret));

			hook.Reverse();
			foreach (var inst in hook)
			{
				method.Body.Instructions.Insert(0, inst);
			}
			method.Body.OptimizeMacros();
		}
	}
}
