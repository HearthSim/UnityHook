// File which holds information about the HearthStone game.
// Specifically; Install directory structure, location of lib files and
// possibly more.

using System;
using System.IO;

namespace GameKnowledgeBase
{
	class HSKB : GameKB
	{

		// This path element will be added to the `gamedir` option.
		// In case the necessary libraries are not located in the root directory of the game folder.
		// The Unity libraries are located at "Hearthstone_Data\Managed", from the root of HS install folder on Windows
		// And in Contents/Resources/Data/Managed on macOS.
		public const string WIN_REL_LIBRARY_PATH = "Hearthstone_Data\\Managed";
		public const string MAC_REL_LIBRARY_PATH = "Contents/Resources/Data/Managed";

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

		// File names of all assemblies, with dll extension.
		// These names match the defined LIB_TYPE enum.
		private static string[] _assemblyFileNames = new string[]
		{
			"", // Empty/Default entry.
			"Assembly-CSharp.dll",
			"Assembly-CSharp-firstpass.dll",
			"UnityEngine.dll",
			"PlayMaker.dll",
		};

		private static HSKB _thisObject;

		// Array of LIB_TYPE instances which act as key for _assemblyFileNames.
		private LIB_TYPE[] _assemblyKeys;
		public LIB_TYPE[] AssemblyKeys
		{
			get
			{
				if (_assemblyKeys == null)
				{
					_assemblyKeys = GetAllLibraryTypes();
				}
				return _assemblyKeys;
			}
		}

		private HSKB(string libPath) : base(libPath, _assemblyFileNames)
		{
		}

		private HSKB(string installPath, bool constructPath) : base(ConstructLibPath(installPath),
																	_assemblyFileNames)
		{
		}

		// Generates the folder path which contains the game libraries.
		private static string ConstructLibPath(string installPath)
		{
			// Append relative directory to library files
			int p = (int)Environment.OSVersion.Platform;
			if ((p == 4) || (p == 6) || (p == 128))
			{
				// Running macOS
				return Path.Combine(installPath, MAC_REL_LIBRARY_PATH);
			}
			else
			{
				// Running Windows
				return Path.Combine(installPath, WIN_REL_LIBRARY_PATH);
			}
		}

		private LIB_TYPE[] GetAllLibraryTypes()
		{
			// Construct an array from the LIB_TYPE enum.
			// The actual underlying type is int, because we enforced that.
			var generalValues = Enum.GetValues(typeof(LIB_TYPE));
			// But we want LIB_TYPE typed values, so we cast all values
			return (LIB_TYPE[])generalValues;
		}

		// If there is no singleton object, it's constructed with the provided install path.
		// The fame folder structure is appended internally.
		public static HSKB Construct(string installPath)
		{
			if (_thisObject == null)
			{
				_thisObject = new HSKB(installPath, true);
			}

			return _thisObject;
		}

		// If there is no singleton object, it's constructed with the provided library path.
		// The libraryPath points to the folder which contains all game libraries.
		public static HSKB Get(string libPath = null)
		{
			if (_thisObject == null)
			{
				_thisObject = new HSKB(libPath);
			}

			return _thisObject;
		}
	}
}
