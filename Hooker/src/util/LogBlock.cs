using System;

namespace Hooker.util
{
    // Object with the purpose to separate logging info into blocks by functionality.
    // The Logger will automatically close this block when a Logblock object is disposed!
    /*
     * Usage:
     *      using(Log.OpenBlock("blockname")) {
     *      // Your code here
     *      }
     */
    class LogBlock : IDisposable
    {
        public string BlockName { get; private set; }

        public LogBlock(string blockName)
        {
            BlockName = blockName;
        }

        public void Dispose()
        {
            Program.Log.CloseBlock(this);
        }
    }
}
