using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Hooker.util
{
	class Logger
	{
		/*
		 * V1.6
		 *		Addition of constructor with Options object to configure logger.
		 * 
		 * V1.5.2
		 *		Changed exception printing layout.
		 * 
         * V1.5.1
         *      Added checks to prevent formatting without provided fill arguments.
         * 
         * V1.5
         *      Added function for writing out the timing of an event.
         * 
         * V1.4.1
         *      Added constructor to enable debugmode.
         * 
         * V1.4
         *      Updated logblocks with IDisposable logblock class.
         *      
         * V1.3
         *      Addition of logblocks.
         * 
         */


		// 2017-01-10 13:50:17Z [INFO]  Message
		private const string LOGFORMAT = "{0} [{1}]\t {2}";
		// 2017-01-10 13:50:17Z [INFO]  Message\nStacktrace
		private const string EXCEPT_FORMAT = "{0} [{1}]\t {2}: {3}\n{4}\n";
		// 2017-01-10 13:50:17.999Z [EVENT]  Event description
		private const string EVENT_TIMING_FORMAT = "{0} [{1}]\t {2}";

		// Syntax of log blocks
		private const string BLOCKFORMAT = "------------ BLOCK {0}: {1} ------------";
		private const string BLOCK_OPEN = "OPEN";
		private const string BLOCK_CLOSE = "CLOSE";

		private const string INNER_EXCEPTION = "INNER EXCEPTION";
		private const string NO_STACKTRACE = "MISSING STACKTRACE";
		private const string UNKN_EXCEPTION = "[Unknown exception]";

		private const string ERROR = "ERROR";
		private const string EXCEPTION = "EXCEPTION";
		private const string INFO = "INFO";
		private const string WARN = "WARN";
		private const string DEBUG = "DEBUG";
		private const string EVENT = "EVENT";

		// TRUE if debug messages should be written
		private bool DebugMode;

		// The stream to write messages to
		private TextWriter OutStream;

		// Keeps track of opened blocks in logfile.
		private Stack<LogBlock> LogBlocks;

		// Initialises a default Logger instance that writes to console.
		// No debug messages!
		public Logger()
		{
			DebugMode = false;
			OutStream = Console.Out;
			LogBlocks = new Stack<LogBlock>();
		}

		public Logger(bool debug) : this()
		{
			DebugMode = debug;
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
		public Logger(GeneralOptions options): this()
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
				Exception("Could not create a logfile on the given path!", e);
			}
		}

		public void Debug(string message, params object[] fills)
		{
			if (fills.Length != 0)
			{
				message = string.Format(message, fills);
			}
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, DEBUG, message);
			OutStream.WriteLine(msg);
		}

		public void Info(string message, params object[] fills)
		{
			if (fills.Length != 0)
			{
				message = string.Format(message, fills);
			}
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, INFO, message);
			OutStream.WriteLine(msg);
		}

		public void Warn(string message, params object[] fills)
		{
			if (fills.Length != 0)
			{
				message = string.Format(message, fills);
			}
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, WARN, message);
			OutStream.WriteLine(msg);
		}

		// Intended to ONLY PRINT exception details. If throwing is necessary, use DBGException.
		public void Exception(string message, Exception e = null, params object[] fills)
		{
			if (fills.Length != 0)
			{
				message = string.Format(message, fills);
			}

			/* Build stacktrace */
			var stacktraceText = (e?.StackTrace == null) ? NO_STACKTRACE : e.StackTrace;
			stacktraceText = stacktraceText.Trim();
			stacktraceText = (stacktraceText.Length == 0) ? NO_STACKTRACE : stacktraceText;

			/* Build Exception name */
			var exceptionName = (e != null) ? e.GetType().Name : UNKN_EXCEPTION;
			var exceptionMessage = (e != null) ? e.Message : "";

			/* Build text to display exception information */
			var exceptionText = string.Format("--->\n{0}\n<---\n> {1}", stacktraceText, message);
			exceptionText = (DebugMode == true) ? exceptionText : "";

			var msg = string.Format(EXCEPT_FORMAT, DateTime.UtcNow, EXCEPTION, exceptionName, exceptionMessage, exceptionText);
			OutStream.WriteLine(msg);

			// if there is an inner exception, print it now.
			// Call stack is sorted from parent to child call.
			if (e?.InnerException != null) Exception(INNER_EXCEPTION, e.InnerException);
		}

		// This method can be used to trigger exception when in debug mode.
		// The passed exception is logged and automatically thrown.
		// Exception throwing is a NO-OP when compiled in release!
		public void DBGException(string message, Exception e = null, params object[] fills)
		{
			if (fills.Length != 0)
			{
				message = string.Format(message, fills);
			}

			// Print to log.
			Exception(message, e);
			// and throw..
			ThrowException(message, e);
		}

		[Conditional("DEBUG")]
		private void ThrowException(string message, Exception e = null)
		{
			if (e != null)
			{
				throw e;
			}
			else
			{
				throw new InvalidOperationException(message);
			}
		}

		public LogBlock OpenBlock(string blockName)
		{
			var message = string.Format(BLOCKFORMAT, BLOCK_OPEN, blockName);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, "", message);
			OutStream.WriteLine(msg);

			// Store the blockName on the stack for easy retrieval when block closes.
			var block = new LogBlock(blockName);
			LogBlocks.Push(block);
			return block;
		}

		public void CloseBlock(LogBlock block)
		{
			// Test stack and passed block
			if (LogBlocks.Count == 0) return;
			if (!LogBlocks.Peek().Equals(block))
			{
				// Invalid close block call!
				return;
			}

			// Perform actual operations..
			var stackBlock = LogBlocks.Pop();
			var blockName = stackBlock.BlockName;
			var message = string.Format(BLOCKFORMAT, BLOCK_CLOSE, blockName);
			var msg = string.Format(LOGFORMAT, DateTime.UtcNow, "", message);
			OutStream.WriteLine(msg);
			// Write an additionall empty line
			OutStream.WriteLine();
		}

		public void Event(string description)
		{
			var msg = string.Format(
				EVENT_TIMING_FORMAT,
				// Write current time with millisecond precision.
				DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
				EVENT,
				description
				);
			OutStream.WriteLine(msg);
		}
	}
}
