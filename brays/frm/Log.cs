using System.IO;
using System.Text;

namespace System
{
	/// <summary>
	/// Simple file logger
	/// </summary>
	public class Log : IDisposable
	{
		/// <summary>
		/// Creates an instance of the logger. It's bound to the filename provided and
		/// will rotate it after the maxSizeKb is reached. The new file name will be 
		/// filename plus the creation time of the new file.
		/// </summary>
		/// <remarks>
		/// If no full path is provided uses the AppDomain.CurrentDomain.BaseDirectory.
		/// When there are multiple versions of the same file will select the latest by name.
		/// </remarks>
		/// <param name="filename">The log file name or full path without the extension.</param>
		/// <param name="ext">The file extension, if any.</param>
		/// <param name="maxSizeKb">The max file size before creating a new file.</param>
		public Log(string filename, string ext, int maxSizeKb)
		{
			Filename = filename;
			Extension = string.IsNullOrEmpty(ext) ? "" : "." + ext;
			MaxLogSizeKb = maxSizeKb;
			rotatePosition = MaxLogSizeKb * 1000;

			var lastLog = findLastLog();

			if (lastLog == null) lastLog = $"{Filename}{Extension}";

			logStream = new FileStream(lastLog, FileMode.Append);
			logFile = new FileInfo(lastLog);
		}

		/// <summary>
		/// The method serializes the writes with a lock.
		/// </summary>
		/// <param name="title">On the same line as the time.</param>
		/// <param name="text">Text under the title.</param>
		/// <param name="flush">By default is true.</param>
		public void Write(string title = "", string text = "", bool flush = true)
		{
			try
			{
				// Compute the time before the lock
				var dt = UseUTC ? DateTime.UtcNow : DateTime.Now;
				var time = string.Format("{0:0000}{1:00}{2:00}-{3:00}{4:00}{5:00}{6:000}",
					dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);

				var nl = string.IsNullOrEmpty(text) ? string.Empty : Environment.NewLine;
				var line = $"{Environment.NewLine}[{time}] {title}{nl}{text}";

				lock (rotationLock)
				{
					logStream.Write(Encoding.UTF8.GetBytes(line));
					if (flush) logStream.Flush();

					rotate();
				}
			}
			catch { }

		}

		public void Flush() => logStream?.Flush();

		/// <summary>
		/// Flushes before disposal.
		/// </summary>
		public void Dispose()
		{
			if (logStream != null)
			{
				logStream.Flush();
				logStream.Dispose();
			}
		}

		void rotate()
		{
			if ((logStream.Position) > rotatePosition)
			{
				var utc = DateTime.UtcNow;
				var time = string.Format("{0:0000}{1:00}{2:00}-{3:00}{4:00}{5:00}{6:000}",
					utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, utc.Millisecond);

				var dir = Path.GetDirectoryName(Filename);

				if (string.IsNullOrEmpty(dir))
					dir = AppDomain.CurrentDomain.BaseDirectory;

				var newLog = Path.Combine(dir, $"{Filename}-{time}{Extension}");

				logStream.Dispose();

				logStream = new FileStream(newLog, FileMode.Append);
				logFile = new FileInfo(newLog);
			}
		}

		string findLastLog()
		{
			var dir = Path.GetDirectoryName(Filename);

			if (string.IsNullOrEmpty(dir))
				dir = AppDomain.CurrentDomain.BaseDirectory;

			var F = Directory.GetFiles(dir, $"{Filename}*");

			if (F != null && F.Length > 0)
			{
				Array.Sort(F);
				return F[F.Length - 1];
			}
			else return null;
		}

		public FileInfo CurrentFile => logFile;

		public bool UseUTC;
		public readonly string Filename;
		public readonly string Extension;
		public int MaxLogSizeKb;
		readonly int rotatePosition;

		FileStream logStream;
		FileInfo logFile;

		object rotationLock = new object();
	}
}
