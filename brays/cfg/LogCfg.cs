using System;

namespace brays
{
	public class LogCfg
	{
		public LogCfg(
			string filePath, bool enabled, TraceOps trace = (TraceOps)((1 << 14) - 1),
			string ext = "brays", int rotSizeKb = 500)
		{
			LogFilePath = filePath;
			Enabled = enabled;
			Ext = ext;
			RotationLogFileKB = rotSizeKb;
			Flags = trace;
		}

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
		public bool Enabled;

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
