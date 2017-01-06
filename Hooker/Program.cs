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

                // Initialise the AssemblyStore with the given path.
                // All assemblies are directly loaded and parsed from their own location.
                // The store must be initialised before HookRegistry, because hookregistry also initialises
                // the store!
                var asStore = AssemblyStore.Get(dataPath);

                // Load the Hookregistery class. 
                // This operation will throw an exception if HookRegistry.dll is not found.
                HookRegistry.Get();

                // Loop all libraries looking for methods to hook       ! important - core
                // Library is a reference to the filename of the assembly file containing the actual
                // code we want to patch.
                foreach (AssemblyStore.LIB_TYPE library in AssemblyStore.GetAllLibraryTypes())
                {
                    // Skip invalid lib!
                    if (library == AssemblyStore.LIB_TYPE.INVALID) continue;

                    // Full path to current assembly
                    string libraryPath = library.GetPath();
                    // Full path to processed assembly
                    string libraryOutPath = library.GetPathOut();

                    // If the target file already exists, skip it..
                    // This prevents the error of not being able to overwrite the file
                    //if (File.Exists(libraryOutPath))
                    //{
                    //    Console.WriteLine("[INFO]\tThe out file for {0} already exists and parsing it will be skipped.\n" +
                    //        "Make sure to delete the file '{1}' before running this program!", libraryPath, libraryOutPath);
                    //    continue;
                    //}

                    // Load the assembly file
                    AssemblyDefinition assembly;
                    AssemblyStore.GetAssembly(library, out assembly);

                    //if (assembly.CheckPatchMark())
                    //{
                    //    Console.WriteLine("[INFO]\tThe file {0} is already patched and will be skipped!\n" +
                    //        "Restore the original library before running this program to patch it again.", libraryPath);
                    //    continue;
                    //}

                    // Construct a hooker wrapper around the main Module of the assembly.
                    // The wrapper facilitates hooking into method calls.
                    ModuleDefinition mainModule = assembly.MainModule;
                    Hooker wrapper = new Hooker(mainModule);
                    Console.WriteLine("[INFO]\tParsing {0}..", libraryPath);

                    // Keep track of hooked methods
                    bool isHooked = false;
                    // Loop each hook entry looking for registered types and methods
                    foreach (HOOK_ENTRY hookEntry in hookEntries)
                    {
                        try
                        {
                            wrapper.AddHookBySuffix(hookEntry.TypeName, hookEntry.MethodName);
                            isHooked = true;
                        }
                        catch (MissingMethodException)
                        {
                            //Console.WriteLine("[DEBUG]\tThere was no function found by the name '{0}.{1}', this entry will be ignored!",
                            //    hookEntry.TypeName, hookEntry.FunctionName);
                        }
                    }

                    try
                    {
                        // Only save if the file actually changed!
                        if (isHooked)
                        {
                            // Save the manipulated assembly
                            library.Save();
                        }

                        // Do NOT overwrite the original file with the new one !
                        // File.Copy(libraryOutPath, libraryPath, true);
                    }
                    catch (IOException)
                    {
                        // The file could be locked! Notify user.
                        // .. or certain libraries could not be resolved..
                        var msg = String.Format("An error occurred while trying to write to file '{0}'.\n" +
                            "The file is possibly locked, make sure no other program is using it!", libraryOutPath);

                        // If the outfile exists, remove it because it's most likely empty
                        if (File.Exists(libraryOutPath))
                        {
                            // This function normally results in a null operation if the file does not exist,
                            // but we checked if the file existed manually anyway!
                            File.Delete(libraryOutPath);
                        }

                        throw;
                    }
                } // End foreach LIB_TYPE

                // Do not copy needed files to target datadirectory      !
                //foreach (var assemblyName in new[] { "Assembly-CSharp", "Assembly-CSharp-firstpass", "HookRegistry", "Newtonsoft.Json" })
                //{
                //    var srcName = assemblyName + ".dll";
                //    if (File.Exists(assemblyName + ".out.dll"))
                //    {
                //        srcName = assemblyName + ".out.dll";
                //    }
                //    File.Copy(srcName, Path.Combine(dataPath, @"Managed", assemblyName + ".dll"), true);
                //}

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

