using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Hooker
{
    class Logger
    {
        // 2017-01-10 13:50:17Z [INFO]  Message
        private const string LOGFORMAT = "{0:u} [{1}]\t {2}";
        // 2017-01-10 13:50:17Z [INFO]  Message\nStacktrace
        private const string EXCEPT_FORMAT = "{0:u} [{1}]\t {2}\n{3}";

        private const string ERROR = "ERROR";
        private const string EXCEPTION = "EXCEPTION";
        private const string INFO = "INFO";
        private const string WARN = "WARN";
        private const string DEBUG = "DEBUG";

        // TRUE if debug messages should be written
        private bool DebugMode { get; }
        // The stream to write messages to
        private TextWriter OutStream { get; set; }

        // Initialises a default Logger instance that writes to console.
        // No debug messages!
        public Logger()
        {
            DebugMode = false;
            OutStream = Console.Out;
        }

        ~Logger()
        {
            try
            {
                OutStream.Close();
            }
            catch (Exception)
            {
                // Do nothing
            }
        }

        // Initialise the logger according to the options provided
        public Logger(GeneralOptions options)
        {
            DebugMode = options.DebugMode;
            SetupLogStream(options.LogFile);
        }

        private void SetupLogStream(string logfile)
        {
            // No log file given; assign standard out
            if (logfile.Count() == 0)
            {
                OutStream = Console.Out;
                return;
            }

            // Do we even check if the file exists?
            try
            {
                logfile = Path.GetFullPath(logfile);
                OutStream = new StreamWriter(logfile, false);
            }
            catch (Exception e)
            {
                OutStream = Console.Error;
                Exception("Could not output a logfile to the given path!", e);
            }
        }

        public void Debug(string message, params object[] fills)
        {
            message = string.Format(message, fills);
            var msg = string.Format(LOGFORMAT, DateTime.UtcNow, DEBUG, message);
            OutStream.WriteLine(msg);
        }

        public void Info(string message, params object[] fills)
        {
            message = string.Format(message, fills);
            var msg = string.Format(LOGFORMAT, DateTime.UtcNow, INFO, message);
            OutStream.WriteLine(msg);
        }

        public void Warn(string message, params object[] fills)
        {
            message = string.Format(message, fills);
            var msg = string.Format(LOGFORMAT, DateTime.UtcNow, WARN, message);
            OutStream.WriteLine(msg);
        }

        public void Exception(string message, Exception e = null, params object[] fills)
        {
            message = string.Format(message, fills);
            // Show stacktrace only if debug mode was turned on
            var stacktraceText = string.Format("--->{0}<---\n{1}", e?.Message, e?.StackTrace);
            var stacktrace = (DebugMode == true) ? stacktraceText : "";

            var msg = string.Format(EXCEPT_FORMAT, DateTime.UtcNow, EXCEPTION, message, stacktrace);
            OutStream.WriteLine(msg);
        }
    }
}
