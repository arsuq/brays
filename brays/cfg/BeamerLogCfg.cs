/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Runtime.CompilerServices;

namespace brays
{
	public class BeamerLogCfg
	{
		public BeamerLogCfg(
			string filePath, bool enabled = true, LogFlags trace = (LogFlags)((1 << 17) - 1),
			string ext = "brays", int rotSizeKb = 5000)
		{
			LogFilePath = filePath;
			IsEnabled = enabled;
			Ext = ext;
			RotationLogFileKB = rotSizeKb;
			Flags = trace;
		}

		/// <summary>
		/// The beamer init will create a new log file. 
		/// </summary>
		public bool RotateLogAtStart = true;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOn(LogFlags op) => (op & Flags) == op && IsEnabled;

		/// <summary>
		/// If not null will be invoked on each trace.
		/// </summary>
		public Action<LogFlags, string, string> OnTrace;

		/// <summary>
		/// The trace file path.
		/// </summary>
		public string LogFilePath;

		/// <summary>
		/// The log file extension.
		/// </summary>
		public string Ext;

		/// <summary>
		/// Enables the tracer.
		/// </summary>
		public bool IsEnabled;

		/// <summary>
		/// The max log file size in KB.
		/// </summary>
		public int RotationLogFileKB;

		/// <summary>
		/// The trace-enabled ops.
		/// </summary>
		public LogFlags Flags;

		/// <summary>
		/// If true, exceptions from the consumer's callback will be logged.
		/// The default is false.
		/// </summary>
		public bool LogUnhandledCallbackExceptions = false;
	}
}
