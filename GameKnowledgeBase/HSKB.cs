// File which holds information about the HearthStone game.
// Specifically; Install directory structure, location of lib files and
// possibly more.

using System;

namespace GameKnowledgeBase
{
	class HSKB : IKnowledge
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
			//UNITY_ENGINE,
			//LIB_PLAYMAKER,
		}

		// File names of all assemblies, with dll extension.
		// These names match the defined LIB_TYPE enum.
		private static string[] _assemblyFileNames = new string[]
		{
			"", // Empty/Default entry.
			"Assembly-CSharp.dll",
			"Assembly-CSharp-firstpass.dll",
			"System.dll",
			//"UnityEngine.dll",
			//"PlayMaker.dll",
		};

		public string[] LibraryFileNames
		{
			get
			{
				return _assemblyFileNames;
			}
		}

		public string LibraryRelativePath
		{
			get
			{
				int p = (int)Environment.OSVersion.Platform;
				if ((p == 4) || (p == 6) || (p == 128))
				{
					// Running macOS
					return MAC_REL_LIBRARY_PATH;
				}
				else
				{
					// Running Windows
					return WIN_REL_LIBRARY_PATH;
				}
			}
		}

		// Array of LIB_TYPE instances which act as key for _assemblyFileNames.
		private LIB_TYPE[] _libKeys;
		public LIB_TYPE[] LibKeys
		{
			get
			{
				if (_libKeys == null)
				{
					_libKeys = GetAllLibraryTypes();
				}
				return _libKeys;
			}
		}

		private LIB_TYPE[] GetAllLibraryTypes()
		{
			// Construct an array from the LIB_TYPE enum.
			// The actual underlying type is int, because we enforced that.
			Array generalValues = Enum.GetValues(typeof(LIB_TYPE));
			// But we want LIB_TYPE typed values, so we cast all values
			return (LIB_TYPE[])generalValues;
		}

		public HSKB() { }
	}
}
