namespace GameKnowledgeBase
{
	public interface IKnowledge
    {
		// Path to the library files, relative to the install path of the game.
		string LibraryRelativePath
		{
			get;
		}

		// All filenames of library files including extension.
		// The filenames will be appended on the LibraryRelativePath.
		// The first entry in this array has to be empty, this corresponds to the invalid
		// value.
		string[] LibraryFileNames
		{
			get;
		}
    }
}
