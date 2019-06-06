using System;
using System.Runtime.CompilerServices;

namespace brays
{
	public class XLogCfg
	{
		public XLogCfg(
			string filePath, bool enabled, XTraceOps trace = (XTraceOps)((1 << 5) - 1),
			string ext = "bx", int rotSizeKb = 500)
		{
			LogFilePath = filePath;
			IsEnabled = enabled;
			Ext = ext;
			RotationLogFileKB = rotSizeKb;
			Flags = trace;
		}

		/// <summary>
		/// Will create a new log file. 
		/// </summary>
		public bool RotateLogAtStart = true;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOn(XTraceOps op) => (op & Flags) == op && IsEnabled;

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
		public XTraceOps Flags;
	}
}
