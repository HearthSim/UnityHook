using GameKnowledgeBase;
using System;
using System.IO;

namespace Hooker
{
	class Program
	{
		// Operation verbs
		public const string OPERATION_HOOK = "hook";
		public const string OPERATION_RESTORE = "restore";

		public const string EXCEPTION_MSG =
			"An exception occurred and assemblies might be in inconsistent state. " +
			"Please use the restore function to bring back the assemblies to original state!";

		// The logger to use for communicating messages
		public static Logger Log;

		// Prepare the general options
		public static void Prepare(GeneralOptions options)
		{
			// Initialise new logger
			Log = new Logger(options);

			// Check the game path
			var gamePath = Path.GetFullPath(options.GamePath);
			options.GamePath = gamePath;
			if (!Directory.Exists(gamePath))
			{
				throw new DirectoryNotFoundException("Exe option `gamedir` is invalid!");
			}
		}

		static int Main(string[] args)
		{
			// Operation
			string invokedOperation = "";
			// General options.
			GeneralOptions generalOptions = null;
			// Options specific to the action to perform.
			object invokedOperationOptions = null;

			var opts = new Options();
			// Must check for null, because the parser won't..
			if (args == null || args.Length == 0)
			{
				Console.WriteLine(opts.GetUsage("help"));
				goto ERROR;
			}

			CommandLine.Parser.Default.ParseArgumentsStrict(args, opts, (verb, subOptions) =>
			{
				// Action to store correct information for further instructing the processor.
				invokedOperation = verb;
				invokedOperationOptions = subOptions;
			}, () =>
			{
				// Failed attempt at parsing the provided arguments.
				Environment.Exit(-2);
			});

			try
			{
				// Process general options
				generalOptions = (GeneralOptions)invokedOperationOptions;
				Prepare(generalOptions);
			}
			catch (Exception e)
			{
				Log.Exception(e.Message, e);
				goto ERROR;
			}

			// Use knowledge about the game HearthStone. Game knowledge is defined in the shared code
			// project KnowledgeBase. See `GameKnowledgeBase.HSKB` for more information.
			// Change the following line if you want to hook another game.
			GameKB gameKnowledge = HSKB.Construct(generalOptions.GamePath);

			try
			{
				switch (invokedOperation)
				{
					case OPERATION_HOOK:
						var hookHelper = new HookHelper((HookSubOptions)invokedOperationOptions);
						hookHelper.TryHook(gameKnowledge);
						Log.Info("Succesfully hooked the game libraries!");
						break;
					case OPERATION_RESTORE:
						var restore = new Restore((RestoreSubOptions)invokedOperationOptions);
						restore.TryRestore(gameKnowledge);
						Log.Info("Succesfully restored the original game libraries!");
						break;
					default:
						throw new ArgumentException("Invalid verb processed");
				}
			}
			catch (Exception e)
			{
				Log.Exception(EXCEPTION_MSG, e);
				goto ERROR;
			}

			// All OK
			return 0;

			ERROR:
			return 1;
		}
	}
}

