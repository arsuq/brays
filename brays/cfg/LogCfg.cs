namespace brays
{
	public class LogCfg
	{
		public LogCfg(string filePath, bool enabled = false, bool traceAll = true, string ext = "brays", int rotSizeKb = 500)
		{
			LogFilePath = filePath;
			Enabled = enabled;
			Ext = ext;
			RotationLogFileKB = rotSizeKb;

			TraceReqAck = traceAll;
			TraceReqTiles = traceAll;
			TraceBeam = traceAll;
			TraceStatus = traceAll;
			TraceSignal = traceAll;
			TraceProcBlock = traceAll;
			TraceProcError = traceAll;
			TraceProcStatus = traceAll;
			TraceProcSignal = traceAll;

		}

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

		public bool TraceReqAck;
		public bool TraceReqTiles;
		public bool TraceBeam;
		public bool TraceStatus;
		public bool TraceSignal;
		public bool TraceProcBlock;
		public bool TraceProcError;
		public bool TraceProcStatus;
		public bool TraceProcSignal;
	}
}
