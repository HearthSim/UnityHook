using Mono.Cecil;
using System;
using System.IO;
using System.Linq;

namespace Hooker.util
{
	public static class AssemblyHelper
	{
		// The namespace to introduce new types into.
		public const string TokenNamespace = "Hooker";
		// A mark indicating that the targetted assembly has been patched already.
		public const string TokenIsPatched = "HK_AssemblyPatched";

		// String to append after assembly filename and before the extension. This is used for HOOKED assemblies.
		public const string AssemblyOutAffix = ".out";
		// String to append after assembly filename and before the extension. This is used for ORIGINAL assemblies.
		public const string AssemblyBackupAffix = ".original";

		public const string ASSEMBLY_EXTENSION = "dll";

		// Load an assembly from the given path.
		// A reference to the loaded assembly will be stored at the passed parameter assembly.
		public static AssemblyDefinition LoadAssembly(string filePath, string resolvePath = null)
		{
			// Construct a resolver that finds other assemblies linked by the one we try to load.
			// Unless specified, the directory containing the assembly file (to load) will be added as resolvepath.
			string fileDir = resolvePath ?? Path.GetDirectoryName(filePath);
			var resolver = new DefaultAssemblyResolver();
			resolver.AddSearchDirectory(fileDir);
			// The resolver gets passed in a set of parameters
			var loadParams = new ReaderParameters();
			loadParams.AssemblyResolver = resolver;

			return AssemblyDefinition.ReadAssembly(filePath, loadParams);
		}

		// Add the TokenIsPatched name as class Type to the main module of the assembly.
		public static void AddPatchMark(this AssemblyDefinition assembly)
		{
			TypeDefinition tokenType = new TypeDefinition(TokenNamespace, TokenIsPatched,
														  TypeAttributes.Class);
			// IMPORTANT - Always set the base type to object!
			// Also, use the Cecil Typesystem to not introduce a dependancy to our .net runtime framework.
			tokenType.BaseType = assembly.MainModule.TypeSystem.Object;
			// Insert the type.
			assembly.MainModule.Types.Add(tokenType);
			// When the assembly gets saved, this class will be present in decompilation!
		}

		// Returns true if the patchmark is detected.
		public static bool HasPatchMark(this AssemblyDefinition assembly)
		{
			string fullTokenName = TokenIsPatched;
			if (TokenNamespace.Length != 0)
			{
				fullTokenName = string.Format("{0}.{1}", TokenNamespace, TokenIsPatched);
			}
			// Look for the token in the given assembly (the Linq system is useful here)
			return assembly.MainModule.Types.FirstOrDefault(t => t.FullName.Equals(
																fullTokenName)) != null;
		}

		// Constructs an outpath string for the given assembly filename.
		// An optional directory can be provided where the assembly file will be stored. The resulting path
		// will be next to the original file if no path is given.
		public static string GetPathOut(string fullPath, string directory = null)
		{
			if (directory != null && !Directory.Exists(directory))
			{
				throw new ArgumentException("Argument `directory` does not exist!");
			}

			// Construct a new filename for the manipulated assembly
			string file = Path.GetFileNameWithoutExtension(fullPath);
			// We know for sure that the extension is .dll
			string newFileName = file + AssemblyOutAffix + "." + ASSEMBLY_EXTENSION;
			string dir = (directory != null) ? directory : Path.GetDirectoryName(fullPath);
			// Construct a new full path for the manipulated assembly
			string newFullPath = Path.Combine(dir, newFileName);

			return newFullPath;
		}

		// Constructs an backup path string for the given assembly filename.
		// An optional directory can be provided where the assembly file will be stored. The resulting path
		// will be next to the original file if no path is given.
		public static string GetPathBackup(string fullPath, string directory = null)
		{
			if (directory != null && !Directory.Exists(directory))
			{
				throw new ArgumentException("Argument `directory` does not exist!");
			}

			// Construct a new filename for the manipulated assembly
			string file = Path.GetFileNameWithoutExtension(fullPath);
			// We know for sure that the extension is .dll
			string newFileName = file + AssemblyBackupAffix + "." + ASSEMBLY_EXTENSION;
			string dir = (directory != null) ? directory : Path.GetDirectoryName(fullPath);
			// Construct a new full path for the manipulated assembly
			string newFullPath = Path.Combine(dir, newFileName);

			return newFullPath;
		}

		public static void Save(this AssemblyDefinition lib, string outPath)
		{
			if (outPath == null || outPath.Length == 0)
			{
				throw new ArgumentException("Parameter `outPath` is invalid!");
			}

			lib.AddPatchMark();

			// Store the assembly
			lib.Write(outPath);
		}
	}
}
