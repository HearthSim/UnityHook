using Hooks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hooker
{
    class Restore
    {
        public const string ERR_RESTORE_FILE = "A problem occurred while restoring file `{0}`!";

        // Collection of all options
        private RestoreSubOptions _options { get; }

        public Restore(RestoreSubOptions options)
        {
            _options = options;
        }

        private void CheckOptions()
        {
            // Game path is already checked at Program
        }

        public void TryRestore()
        {
            // Check the options
            CheckOptions();

            // Fetch list of filenames which contain .original
            var pattern = string.Format("*{0}*", AssemblyStore.AssemblyBackupAffix);
            var affixLength = AssemblyStore.AssemblyBackupAffix.Length;
            var originals = Directory.GetFiles(_options.GamePath, pattern);
            // Cut off extension and backup affix
            var fileNamesNoExt = originals.Select(str =>
            {
                // Returns only the name of the file
                var noExt = Path.GetFileNameWithoutExtension(str);
                // Prepend the original path
                noExt = Path.Combine(Path.GetDirectoryName(str), noExt);
                var noExtLength = noExt.Length;
                var noAffix = noExt.Substring(0, noExtLength - affixLength);
                return noAffix;
            }).ToArray();

            // Loop all filnames to restore originals
            for (int i = 0; i < originals.Length; ++i)
            {
                var reconstructed = fileNamesNoExt[i] + ".dll";
                var backup = originals[i];

                try
                {
                    File.Copy(backup, reconstructed, true);
                }
                catch (Exception e)
                {
                    // This is actually really bad.. but we'll continue to restore originals
                    Program.Log.Exception(ERR_RESTORE_FILE, e, backup);
                }
            }
        }
    }
}
