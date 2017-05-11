using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace GameKnowledgeBase
{
	public class GameKB : IEnumerable<string>
	{
		// Path to the directory provided to this object on initialisation
		private string _libraryPath;
		public string LibraryPath
		{
			get
			{
				return _libraryPath;
			}
		}

		// Array of important library file names.
		private string[] _libraryFileNames;

		protected GameKB(string libraryPath, string[] libraryFileNames)
		{
			if (libraryFileNames.Length == 0)
			{
				throw new ArgumentException("LibraryFileNames must not be empty!");
			}

			if (libraryFileNames[0].Trim().Length != 0)
			{
				throw new ArgumentException("LibraryFileNames must have an empty first entry!");
			}

			if (!Directory.Exists(libraryPath))
			{
				throw new DirectoryNotFoundException("LibraryPath does not exist!");
			}

			_libraryPath = libraryPath;
			_libraryFileNames = libraryFileNames;
		}

		// Get the full path to the file of the requested library
		public string GetAssemblyPath(int fileNameIdx)
		{
			// Prevent IndexOutOfBounds by testing the value of lib.
			// 0 is counted as INVALID automatically.
			if (fileNameIdx < 1 || fileNameIdx >= _libraryFileNames.Length)
			{
				throw new ArgumentOutOfRangeException("Parameter lib must be within the valid range!");
			}

			// Construct the full path for the requested assembly. This requires prepending the path
			// recorded on initialisation.
			string fullPath = Path.Combine(_libraryPath, _libraryFileNames[fileNameIdx]);
			return fullPath;
		}

		public IEnumerator<string> GetEnumerator()
		{
			// Skip the invalid entry.
			for (int i = 1; i < _libraryFileNames.Length; ++i)
			{
				yield return GetAssemblyPath(i);
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
