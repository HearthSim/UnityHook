using Hooks;
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
        // This structure represents one line of text in our hooks file.
        // It basically boils down to what function we target in which class.
        public struct HOOK_ENTRY
        {
            public string TypeName;
            public string MethodName;
        }

        // The string used to split TypeName from FunctionName; see ReadHooksFile(..)
        public const string HOOK_SPLIT = "::";

        static List<HOOK_ENTRY> ReadHooksFile(string hooksFilePath)
        {
            var hookEntries = new List<HOOK_ENTRY>();

            // Open and parse our hooks file.
            // File.ReadLines needs at least framework 4.0
            foreach (string line in File.ReadLines(hooksFilePath))
            {
                // Remove all unnecessary whitespace
                var lineTrimmed = line.Trim();
                // Skip empty or comment lines
                if (lineTrimmed.Length == 0 || lineTrimmed.IndexOf("//") == 0)
                {
                    continue;
                }
                // In our hooks file we use C++ style syntax to avoid parsing problems 
                // regarding full names of Types and Methods. (namespaces!)
                // Hook calls are now registered as FULL_TYPE_NAME::METHOD_NAME
                // There are no methods registered without type, so this always works!
                var breakIdx = lineTrimmed.IndexOf(HOOK_SPLIT);
                // This is not a super robuust test, but it filters out the gross of 
                // impossible values.
                if (breakIdx != -1)
                {
                    // Create and store a new entry object
                    hookEntries.Add(new HOOK_ENTRY
                    {
                        // From start to "::"
                        TypeName = lineTrimmed.Substring(0, breakIdx),
                        // After (exclusive) "::" to end
                        MethodName = lineTrimmed.Substring(breakIdx + HOOK_SPLIT.Length),
                    });
                }

            }

            return hookEntries;
        }

        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: Hooker.exe [GameName_Data directory] [hooks file]");
                goto ERROR;
            }
            // Path to library directory of Game
            var dataPath = args[0];
            // Path to hooks file
            var hookFilePath = args[1];

            // Check parameters
            // Do not throw exceptions! If we don't catch them at the same time the OS will do it for
            // us and that does not look pretty..
            if (Directory.Exists(dataPath) != true)
            {
                Console.WriteLine("[ERROR]\tThe data directory path '{0}' does not exist.", dataPath);
                goto ERROR;
            }
            if (File.Exists(hookFilePath) != true)
            {
                Console.WriteLine("[ERROR]\tThe hooks filepath '{0}' does not exist.", hookFilePath);
                goto ERROR;
            }

            try
            {
                // Read all hook functions into memory
                var hookEntries = ReadHooksFile(hookFilePath);
                Console.Out.WriteLine("[INFO]\tParsed {0} hook entries", hookEntries.Count);

                foreach (var s in new[] { "Assembly-CSharp-firstpass", "Assembly-CSharp" })
                {
                    var inStream = File.Open(s + ".dll", FileMode.Open, FileAccess.Read);
                    var scriptAssembly = AssemblyDefinition.ReadAssembly(inStream);
                    var hooker = new Hooker(scriptAssembly.MainModule);
                    // Simplified hooking
                    foreach (var hook in hookEntries)
                    {
                        hooker.AddHookBySuffix(hook.TypeName, hook.MethodName);
                    }

                    scriptAssembly.Write(s + ".out.dll");
                }

                foreach (var assemblyName in new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "HookRegistry", "Newtonsoft.Json" })
                {
                    var srcName = assemblyName + ".dll";
                    if (File.Exists(assemblyName + ".out.dll"))
                    {
                        srcName = assemblyName + ".out.dll";
                    }
                    File.Copy(srcName, Path.Combine(dataPath, @"Managed", assemblyName + ".dll"), true);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine("[EXCEPTION] {0}", e.Message);
                // To log
                // Console.Error.WriteLine("[EXCEPTION] {0}\n{1}", e.Message, e.StackTrace);
                goto ERROR;
            }

            FINISH:
            Console.WriteLine("FINISHED - Press a key to continue..");
            Console.ReadKey();
            return 0;

            ERROR:
            Console.WriteLine("FINISHED - Press a key to continue..");
            Console.ReadKey();
            return 1;

        }
    }
}

