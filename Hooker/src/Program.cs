using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hooker
{
    class Program
    {
        // This path element will be added to the `gamedir` option.
        // In case the necessary libraries are not located in the root directory of the game folder.
        // The Unity libraries are located at "Hearthstone_Data\Managed", from the root of HS install folder
        public const string REL_LIBRARY_PATH = "Hearthstone_Data\\Managed";

        // Operation verbs
        public const string OPERATION_HOOK = "hook";
        public const string OPERATION_RESTORE = "restore";

        public const string EXCEPTION_MSG = "An exception occurred and assemblies might be in inconsistent state. " +
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
            // Append relative directory to library files
            gamePath = Path.Combine(gamePath, REL_LIBRARY_PATH);
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
            // Options for that operation
            object invokedOperationOptions = null;

            var opts = new Options();
            // Must check for null, because the parser won't..
            if (args == null || args.Length == 0)
            {
                Console.WriteLine(opts.GetUsage("help"));
                goto ERROR;
            }
            if (!CommandLine.Parser.Default.ParseArgumentsStrict(args, opts,
                (verb, subOptions) =>
                {
                    invokedOperation = verb;
                    invokedOperationOptions = subOptions;
                },
                () =>
                {
                    Log.Exception("Failed to parse arguments!");
                    // Failed to parse, exit the program
                    Environment.Exit(-2);
                }))
            {
                // The parser will have written usage information.
                // This might happen because options were misspelled or not given.
                goto ERROR;
            }

            try
            {
                // Process general options
                Prepare((GeneralOptions)invokedOperationOptions);
            } catch(Exception e)
            {
                Log.Exception(e.Message, e);
                goto ERROR;
            }

            try
            {
                switch (invokedOperation)
                {
                    case OPERATION_HOOK:
                        var hookHelper = new HookHelper((HookSubOptions)invokedOperationOptions);
                        hookHelper.TryHook();
                        Log.Info("Succesfully hooked the game libraries!");
                        break;
                    case OPERATION_RESTORE:
                        var restore = new Restore((RestoreSubOptions)invokedOperationOptions);
                        restore.TryRestore();
                        Log.Info("Succesfully restored the original game libraries!");
                        break;
                    default:
                        // Error happened
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

