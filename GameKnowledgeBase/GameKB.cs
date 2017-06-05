using System;
using System.IO;

namespace GameKnowledgeBase
{
	public class GameKB
	{
		// Information about the game to hook.
		IKnowledge _gameKnowledge;

		// Path to the install directory of the game.
		private string _installpath;
		public string InstallPath
		{
			get
			{
				return _installpath;
			}
		}

		public string LibraryPath
		{
			get
			{
				return Path.Combine(_installpath, _gameKnowledge.LibraryRelativePath);
			}
		}

		// Array of important library file names.
		private string[] _libraryFilePaths;
		public string[] LibraryFilePaths
		{
			get
			{
				return _libraryFilePaths;
			}
		}

		// Construct a new knowledge base with a provided path to the installation folder.
		public GameKB(string installPath, IKnowledge gameKnowledge)
		{
			if (!Directory.Exists(installPath))
			{
				throw new ArgumentException("LibraryPath parameter must point to a valid path!");
			}

			_installpath = Path.GetFullPath(installPath);
			_gameKnowledge = gameKnowledge ?? throw new ArgumentNullException("GameKnowledge parameter cannot be null!");

			ConstructLibraryPaths();
		}

		public static GameKB CreateFromLibraryPath(IKnowledge gameKnowledge, string libPath)
		{
			string libPathPart = gameKnowledge.LibraryRelativePath;
			if (!libPath.EndsWith(libPathPart))
			{
				throw new InvalidOperationException("The provided library path does not match the provided game knowledge base!");
			}
			// The install path is the relative library path removed from the full lib path.
			string installPath = libPath.Remove(libPath.Length - libPathPart.Length);
			return new GameKB(installPath, gameKnowledge);
		}

		// Construct the full path of each library defined by the game knowledgebase.
		private void ConstructLibraryPaths()
		{
			string[] fileNames = _gameKnowledge.LibraryFileNames;
			int libraryCount = fileNames.Length;

			_libraryFilePaths = new string[libraryCount];
			if (fileNames[0].Length != 0)
			{
				throw new InvalidOperationException("The first assembly filename entry MUST be empty!");
			}

			for (int i = 0; i < libraryCount; ++i)
			{
				_libraryFilePaths[i] = Path.Combine(LibraryPath, fileNames[i]);
			}
		}
	}
}
