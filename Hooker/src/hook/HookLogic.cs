﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Collections.Generic;
using Hooker.util;

namespace Hooker.Hook
{
	class HookLogic
	{
		public const string UNEXPECTED_METHOD =
			"The method `{0}` is not expected by your version of HookRegistry." +
			"Continuing will have no effect. Please update your hooksfile or retrieve another HookRegistry dll file that hooks into the given method!";
		public const string HOOKED_METHOD = "Method `{0}` hooked.\n";
		public const string HOOK_PROBLEM = "A problem occurred while hooking `{0}`: {1}\n" +
										   "Hooking will continue!";

		// The library to hook
		public ModuleDefinition Module
		{
			get;
			private set;
		}
		// Method that gets called when entering a hooked method
		private MethodReference onCallMethodRef;

		private HookLogic()
		{
		}

		public static HookLogic New(ModuleDefinition module, HookSubOptions options)
		{
			/*
			    These things have to be recreated for every module!
			    This is because a reference is specifically generated from a certain module and cannot be reused
			    by another module!
			*/

			// Fetch types and references
			TypeDefinition _hookRegistryType = options.HookRegistryTypeBlueprint;
			// Look for the HookRegistry.onCall(..) method
			MethodDefinition onCallMethod = _hookRegistryType.Methods.First(mi => mi.Name.Equals("OnCall"));
			MethodReference onCallMethodRef = module.Import(onCallMethod);

			var newObj = new HookLogic
			{
				Module = module,
				onCallMethodRef = onCallMethodRef,
			};

			return newObj;
		}

		public void AddHookBySuffix(string typeName, string methodName, string[] expectedMethods)
		{
			// Get all types from the Module that match the given typeName.
			// A type is selected if the predicate, inside the lambda definition, returns true.
			var matchingTypes = Module.Types.Where(t =>
			{
				var idx = t.Name.IndexOf(typeName);
				// This one is clever!
				//      - Return false if no match was found
				//      - Return true IF one of the next is true:
				//                      * The name of the Type starts with the typeName
				//                      * The name of the Type starts with the typeName, the type was found
				//                          underneath another namespace
				//
				// Side effect: This matches all types that begin with typeName
				// eg 'OnCall' will match the following:
				//      - ns.OnCall(..)
				//      - ns.deepns.OnCall(..)
				//      - ns.OnCall_static(..)
				return idx < 0 ? false : idx == 0 || t.Name[idx - 1] == '.';
			});

			// A Type (probably class) is found, now we test it/them for the requested method
			bool found = false;
			foreach (TypeDefinition type in matchingTypes)
			{
				// This time look for an EXACT match with methodName
				foreach (var method in type.Methods.Where(m => m.Name.Equals(methodName)))
				{
					try
					{
						var methodFullname = method.DeclaringType.FullName + HooksFileParser.METHOD_SPLIT + method.Name;
						// WARN because no hookregistry expects this method to be hooked
						if (expectedMethods.FirstOrDefault(m => m.Equals(methodFullname)) == null)
						{
							Program.Log.Warn(UNEXPECTED_METHOD, methodFullname);
						}
						// Hook our code into the body of this method
						AddHook(method);
						Program.Log.Info(HOOKED_METHOD, method.FullName);
						found = true;
					}
					catch (Exception e)   // Yes, catch all exceptions
					{
						// We are only interested in these specific exceptions
						if (e is InvalidProgramException || e is InvalidOperationException)
						{
							// Report the problem to user directly ignoring the exception
							Program.Log.Exception(HOOK_PROBLEM, e, method.FullName, e.Message);
							continue;
						}

						// Throw the same exception to parent, because we don't know it.
						// Especially because we know nothing about it we don't interfere!
						throw;
					}
				}
			}
			if (!found)
			{
				// Escalate issue, the parent can decide how to handle this
				// Console.WriteLine("Cannot find any method matching {0}.{1}", typeName, methodName);
				throw new MissingMethodException();
			}
		}

		public void AddHook(MethodDefinition method)
		{
			// Keep track of all generic parameters.
			var genericParams = new Collection<GenericParameter>();

			if (!method.HasBody)
			{
				// Escalate issue to parent
				throw new InvalidProgramException("The selected method does not have a body!");
			}

			/*
			 * Generic methods are a special case.
			 * When generic ARGUMENTS are used to construct runtimehandles (typeof variants) the information
			 * about the declaring type is lost -> original code with generic PARAMETERS.
			 * 
			 * Each hook method in the HookRegistry library must register the declaring class type of the generic
			 * method it hooks. If this is not done, the HookRegistry will panic() when the hooked method cannot 
			 * be resolved at runtime, leading to crashes (on purpose).
			 * See (HOOKREGISTRY)\\Hooks.OutgoingPackets.RegisterGenericDeclaringTypes() for an
			 * example.
			 */
			// Method itself might be generic!
			if (method.HasGenericParameters)
			{
				Program.Log.Warn("The method `{0}` has generic parameters!", method.Name);
				// throw new InvalidOperationException("Generic parameters on method not supported");
			}
			// Declaring class is generic!
			if (method.DeclaringType.HasGenericParameters)
			{
				Program.Log.Warn("The declaring class of method `{0}` has generic parameters!", method.Name);
				// throw new InvalidOperationException("Generic parameters on class not supported");
			}

			// Construct method from generic arguments
			

			// The following occurs in the body of the selected method              !important
			// The method body is a set of instructions executed by the runtime.
			// By manipulating the amount and order of instructions, we add a method call
			// to our own function as first operation (the actual hook).
			// After running our own method(s), the result will be returned to the targetted
			// method, that method will return our result and normal flow continues.

			// Without explaining every instruction, the following is what happens:
			//      1. Construct an array for intercepting the passed arguments to this method;
			//      2. Fill array with upcasted (to Object class) values (primitives are boxed)
			//      3. Call HookRegistry.OnCall( handle to original method, thisobject of method, arguments passed[])
			//      4. All hook classes are called
			//      5. If any of them returned anything (!= null), that result will be passed back as the return value
			//          of the hooked method.
			//          ! If null is returned from each hook class, the original function is executed

			// object[] interceptedArgs;
			// object hookResult;
			var interceptedArgs = new VariableDefinition("interceptedArgs",
														 method.Module.TypeSystem.Object.MakeArrayType());
			var hookResult = new VariableDefinition("hookResult", method.Module.TypeSystem.Object);

			method.Body.Variables.Add(interceptedArgs);
			method.Body.Variables.Add(hookResult);
			var numArgs = method.Parameters.Count;
			var hook = new List<Instruction>();
			// interceptedArgs = new object[numArgs];
			hook.Add(Instruction.Create(OpCodes.Ldc_I4, numArgs));
			hook.Add(Instruction.Create(OpCodes.Newarr, Module.TypeSystem.Object));
			hook.Add(Instruction.Create(OpCodes.Stloc, interceptedArgs));

			// rmh = methodof([this method]).MethodHandle;
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
					/// if the arg is a reference type, it must be copied and boxed
					var refType = (ByReferenceType)param.ParameterType;
					hook.Add(Instruction.Create(OpCodes.Ldobj, refType.ElementType));
					hook.Add(Instruction.Create(OpCodes.Box, refType.ElementType));
				}
				else if (param.ParameterType.IsValueType)
				{
					/// if the arg descends from ValueType, it must be boxed to be
					/// converted to an object:
					hook.Add(Instruction.Create(OpCodes.Box, param.ParameterType));
				}
				hook.Add(Instruction.Create(OpCodes.Stelem_Ref));
				i++;
			}
			// hookResult = HookRegistry.OnCall(rmh, thisObj, interceptedArgs);
			hook.Add(Instruction.Create(OpCodes.Ldloc, interceptedArgs));
			hook.Add(Instruction.Create(OpCodes.Call, onCallMethodRef));
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
