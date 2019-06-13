using System;
using System.Runtime.CompilerServices;

namespace brays
{
	public class XLogCfg
	{
		public XLogCfg(
			string filePath, bool enabled, XFlags flags = (XFlags)((1 << 17) - 1),
			string ext = "bx", int rotSizeKb = 500)
		{
			LogFilePath = filePath;
			IsEnabled = enabled;
			Ext = ext;
			RotationLogFileKB = rotSizeKb;
			Flags = flags;
		}

		/// <summary>
		/// Will create a new log file. 
		/// </summary>
		public bool RotateLogAtStart = true;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsOn(XFlags f) => (f & Flags) == f && IsEnabled;

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
		/// The trace-enabled flags.
		/// </summary>
		public XFlags Flags;
	}
}
