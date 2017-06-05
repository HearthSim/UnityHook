using System.Collections.Generic;
using System.IO;

namespace Hooker.Hook
{
	static class HooksFileParser
	{
		// The string used to split TypeName from FunctionName; see ReadHooksFile(..)
		public const string METHOD_SPLIT = "::";

		// This structure represents one line of text in our hooks file.
		// It basically boils down to what function we target in which class.
		public struct HOOK_ENTRY
		{
			public string TypeName;
			public string MethodName;

			public string FullMethodName
			{
				get
				{
					return TypeName + METHOD_SPLIT + MethodName;
				}
			}
		}

		public static List<HOOK_ENTRY> ReadHooksFile(string hooksFilePath)
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
				var breakIdx = lineTrimmed.IndexOf(METHOD_SPLIT);
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
						MethodName = lineTrimmed.Substring(breakIdx + METHOD_SPLIT.Length),
					});
				}

			}
			using (Program.Log.OpenBlock("Parsing hooks file"))
			{
				Program.Log.Info("File location: `{0}`", hooksFilePath);
				Program.Log.Info("Parsed {0} entries.", hookEntries.Count);
			}
			return hookEntries;
		}
	}
}
