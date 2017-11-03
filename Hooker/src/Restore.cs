using GameKnowledgeBase;
using Hooker.util;
using System;
using System.IO;

namespace Hooker
{
	class Restore
	{
		public const string FILE_RESTORED = "The original file `{0}` is restored.";
		public const string ERR_RESTORE_FILE = "A problem occurred while restoring file `{0}`!";

		// Collection of all options
		private RestoreSubOptions _options
		{
			get;
		}

		public Restore(RestoreSubOptions options)
		{
			_options = options;
		}

		private void CheckOptions()
		{
			// Game path is already checked at Program
		}

		public void TryRestore(GameKB gameKnowledge)
		{
			// Check the options
			CheckOptions();

			// Iterate all known libraries for the game.
			foreach (string gameLibFilePath in gameKnowledge.LibraryFilePaths)
			{
				string backupLibPath = AssemblyHelper.GetPathBackup(gameLibFilePath);
				if (File.Exists(backupLibPath))
				{
					// Restore the original file.
					try
					{
						File.Copy(backupLibPath, gameLibFilePath, true);
						Program.Log.Info(FILE_RESTORED, backupLibPath);
					}
					catch (Exception e)
					{
						// This is actually really bad.. but we'll continue to restore other originals.
						Program.Log.Exception(ERR_RESTORE_FILE, e, backupLibPath);
					}
				}
			}
		}
	}
}
