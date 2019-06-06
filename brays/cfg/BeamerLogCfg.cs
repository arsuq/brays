using System;
using System.Runtime.CompilerServices;

namespace brays
{
	public class BeamerLogCfg
	{
		public BeamerLogCfg(
			string filePath, bool enabled, TraceOps trace = (TraceOps)((1 << 14) - 1),
			string ext = "brays", int rotSizeKb = 500)
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
		public bool IsOn(TraceOps op) => (op & Flags) == op && IsEnabled;

		/// <summary>
		/// If not null will be invoked on each trace.
		/// </summary>
		public Action<TraceOps, string, string> OnTrace;

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
		public TraceOps Flags;
	}
}
