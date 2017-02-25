using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hooks
{
    public class AssemblyStore
    {
        // Handle for selecting the correct AssemblyDefinition; see _assemblyFileNames
        // Force the underlying type to be integer.
        public enum LIB_TYPE : int
        {
            INVALID = 0,
            LIB_CSHARP,
            LIB_CSHARP_FIRSTPASS,
            UNITY_ENGINE,
            LIB_PLAYMAKER,
        }

        // File names of all assemblies, with dll extension, matching LIB_TYPE index
        private static string[] _assemblyFileNames = new string[] {
                "",
                "Assembly-CSharp.dll",
                "Assembly-CSharp-firstpass.dll",
                "UnityEngine.dll",
                "PlayMaker.dll",
            };

        // String to append after assembly filename and before the extension. This is used for HOOKED assemblies.
        public const string AssemblyOutAffix = ".out";
        // String to append after assembly filename and before the extension. This is used for ORIGINAL assemblies.
        public const string AssemblyBackupAffix = ".original";
        // A mark indicating that the targetted assembly has been patched already
        public const string TokenIsPatched = "HK_AssemblyPatched";
        // The namespace to introduce new types into
        public const string TokenNamespace = "Hooker";

        // Indicates if the libraries are loaded (on first retrieval) or not
        private bool _libsLoaded = false;

        // Path to the directory provided to this object on initialisation
        private static string _dataPath { get; set; }

        public static string DataPath
        {
            get
            {
                return _dataPath;
            }
        }

        // Container of Assembly blueprints
        private Dictionary<LIB_TYPE, AssemblyDefinition> _assemblies { get; set; }
        public Dictionary<LIB_TYPE, AssemblyDefinition> assemblies
        {
            get
            {
                // We target 3.5, which is too low for immutable collections.
                // So we resort to a shallow copy of our assemblies dict
                return new Dictionary<LIB_TYPE, AssemblyDefinition>(_assemblies);
            }
        }

        // Singleton for this object
        private static AssemblyStore _thisObject { get; set; }

        // Private constructor, external code cannot initialise this class
        private AssemblyStore()
        {
            _assemblies = new Dictionary<LIB_TYPE, AssemblyDefinition>();
        }

        // Load the ModuleDefinitions of all assemblies from assemblyFileNames
        private void LoadAssemblies()
        {
            // Construct a resolver that finds other assemblies linked by the 
            // ones we try to load.
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(_dataPath);
            // The resolver gets passed in a set of parameters
            var loadParams = new ReaderParameters();
            loadParams.AssemblyResolver = resolver;            

            // Retrieve an array of all values in our enum. The underlying type is int.
            // Because we know the underlying type, it's without danger to convert between
            // LIB_TYPE and int.
            foreach (LIB_TYPE enumVal in GetAllLibraryTypes())
            {
                // Skip 0 - INVALID
                if (enumVal == LIB_TYPE.INVALID) continue;

                // Construct the full path to the library.
                // The int representation of the enum is the index of the matching filename.
                var libFileName = _assemblyFileNames[(int)enumVal];
                var libFullPath = Path.Combine(_dataPath, libFileName);

                // Load the Assembly definition.
                // This 'loading' doesn't actually copy the library into our App domain, but a blueprint 
                // of the assembly file will be constructed in memory.
                var assemblyDefinition = AssemblyDefinition.ReadAssembly(libFullPath, loadParams);
                // and store the definition at the appropriate spot
                _assemblies.Add(enumVal, assemblyDefinition);
            }

            _libsLoaded = true;
        }

        // Access the one and only instance of this class
        public static AssemblyStore Get(String dataPath = null)
        {
            if (_thisObject == null)
            {
                if (dataPath == null)
                {
                    throw new ArgumentNullException("Parameter dataPath must be a valid string");
                }

                _thisObject = new AssemblyStore();
                // Save the dataPath for future reference
                _dataPath = dataPath;
            }

            return _thisObject;
        }

        // Pass a reference back to the requested assembly (library)
        public static void GetAssembly(LIB_TYPE lib, out AssemblyDefinition outAssembly)
        {
            if (_thisObject == null)
            {
                throw new InvalidOperationException("The class AssemblyStore has not been initialised!");
            }

            // Assemblies are loaded on first retrieval
            if (Get()._libsLoaded != true)
            {
                Get().LoadAssemblies();
            }

            // Try to fetch the correct definition from the map. The returned value (from the map) will be put
            // into this variable.
            AssemblyDefinition assemblyDefinition;
            Get()._assemblies.TryGetValue(lib, out assemblyDefinition);

            // If lib is invalid, null is returned. Escalate the issue.
            if (assemblyDefinition == null)
            {
                throw new ArgumentOutOfRangeException("Parameter lib must be a valid value of LIB_TYPE!");
            }

            // Pass the definition back in the same paradigm
            outAssembly = assemblyDefinition;
        }

        // Get the full path to the file of the requested library
        public static string GetAssemblyPath(LIB_TYPE lib)
        {
            if (_thisObject == null)
            {
                throw new InvalidOperationException("The class AssemblyStore has not been initialised!");
            }

            int fileNameIdx = (int)lib;
            // Prevent IndexOutOfBounds by testing the value of lib
            if (fileNameIdx < 1 || fileNameIdx >= _assemblyFileNames.Length)
            {
                throw new ArgumentOutOfRangeException("Parameter lib must be a valid value of LIB_TYPE!");
            }

            // Construct the full path for the requested assembly. This requires prepending the path 
            // recorded on initialisation.
            string fullPath = Path.Combine(_dataPath, _assemblyFileNames[fileNameIdx]);
            return fullPath;
        }

        public static LIB_TYPE[] GetAllLibraryTypes()
        {
            // Construct an array from the LIB_TYPE enum. The actual underlying type is int, because we forced
            // it to be an int; see AssemblyStore.LIB_TYPE
            var generalValues = Enum.GetValues(typeof(LIB_TYPE));
            // But we want LIB_TYPE typed values, so we cast all values
            return (LIB_TYPE[])generalValues;
        }
    }

    public static class AssemblyHelper
    {
        // Load an assembly from the given path.
        // A reference to the loaded assembly will be stored at the passed parameter assembly.
        public static void LoadAssembly(string filePath, out AssemblyDefinition assembly, string resolvePath = null)
        {
            // Construct a resolver that finds other assemblies linked by the 
            // ones we try to load.
            // Unless specified, the directory of the assembly file will be added as resolvepath
            var fileDir = (resolvePath != null) ? resolvePath : Path.GetDirectoryName(filePath);
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(fileDir);
            // The resolver gets passed in a set of parameters
            var loadParams = new ReaderParameters();
            loadParams.AssemblyResolver = resolver;

            assembly = AssemblyDefinition.ReadAssembly(filePath, loadParams);
        }

        // Add the TokenIsPatched name as class Type to the module
        public static void AddPatchMark(this AssemblyDefinition assembly)
        {
            // Generate new (class) type
            TypeDefinition tokenType = new TypeDefinition(AssemblyStore.TokenNamespace,
                AssemblyStore.TokenIsPatched, TypeAttributes.Class);
            // Make the type public
            // tokenType.IsPublic = true;
            // IMPORTANT - Always set the base type to object!
            tokenType.BaseType = assembly.MainModule.TypeSystem.Object;
            // Insert the type
            assembly.MainModule.Types.Add(tokenType);
            // When the assembly gets saved, this class will be present in decompilation!
        }

        // Returns true if the patchmark is detected
        public static bool HasPatchMark(this AssemblyDefinition assembly)
        {
            string fullTokenName = AssemblyStore.TokenIsPatched;
            if (AssemblyStore.TokenNamespace.Length != 0)
            {
                fullTokenName = String.Format("{0}.{1}", AssemblyStore.TokenNamespace, AssemblyStore.TokenIsPatched);
            }
            // Look for the token in the given assembly (the Linq system is useful here)
            return assembly.MainModule.Types.FirstOrDefault(t => t.FullName.Equals(fullTokenName)) != null;
        }

        // Fetch the original location of the given library
        public static string GetPath(this AssemblyStore.LIB_TYPE lib)
        {
            return AssemblyStore.GetAssemblyPath(lib);
        }

        // Constructs an outpath string for the given assembly
        // An optional directory can be provided where the assembly file will be stored. The resulting path
        // will be next to the original file if no path is given.
        public static string GetPathOut(this AssemblyStore.LIB_TYPE lib, string directory = null)
        {
            if (directory != null && !Directory.Exists(directory))
            {
                throw new ArgumentException("Argument path '{0}' does not exist!", directory);
            }

            string fullPath = AssemblyStore.GetAssemblyPath(lib);
            // Construct a new filename for the manipulated assembly
            string file = Path.GetFileNameWithoutExtension(fullPath);
            // We know for sure that the extension is .dll
            string newFileName = file + AssemblyStore.AssemblyOutAffix + ".dll";
            string dir = (directory != null) ? directory : Path.GetDirectoryName(fullPath);
            // Construct a new full path for the manipulated assembly
            string newFullPath = Path.Combine(dir, newFileName);

            return newFullPath;
        }

        // Constructs an backup path string for the given assembly
        // An optional directory can be provided where the assembly file will be stored. The resulting path
        // will be next to the original file if no path is given.
        public static string GetPathBackup(this AssemblyStore.LIB_TYPE lib, string directory = null)
        {
            if (directory != null && !Directory.Exists(directory))
            {
                throw new ArgumentException("Argument path '{0}' does not exist!", directory);
            }

            string fullPath = AssemblyStore.GetAssemblyPath(lib);
            // Construct a new filename for the manipulated assembly
            string file = Path.GetFileNameWithoutExtension(fullPath);
            // We know for sure that the extension is .dll
            string newFileName = file + AssemblyStore.AssemblyBackupAffix + ".dll";
            string dir = (directory != null) ? directory : Path.GetDirectoryName(fullPath);
            // Construct a new full path for the manipulated assembly
            string newFullPath = Path.Combine(dir, newFileName);

            return newFullPath;
        }

        public static void Save(this AssemblyStore.LIB_TYPE lib)
        {
            // Get the assembly
            AssemblyDefinition assDefinition;
            AssemblyStore.GetAssembly(lib, out assDefinition);
            // Put the patch mark on the assembly   ! important
            assDefinition.AddPatchMark();
            // Load the default out path
            string assOutPath = GetPathOut(lib);

            // Add our datapath to the resolve process
            (assDefinition.MainModule.AssemblyResolver as BaseAssemblyResolver).AddSearchDirectory(AssemblyStore.DataPath);

            // Store the assembly
            assDefinition.Write(assOutPath);
        }

        public static void Backup(this AssemblyStore.LIB_TYPE lib)
        {
            // Get the original path
            string path = GetPath(lib);
            // Get the backup path
            string backupPath = GetPathBackup(lib);
            // Copy the file to the backup location
            // but do NOT overwrite if it already exists!
            try
            {
                // This throws if the file already exists.
                File.Copy(path, backupPath, false);
            } catch (Exception)
            {
                // Do nothing
            }
        }
    }
}
