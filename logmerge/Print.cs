using System;
using System.Threading;

namespace logmerge
{
	public static class Print
	{
		static Print()
		{
			Foreground = Console.ForegroundColor;
		}

		public static void Line() { if (!IgnoreAll) Console.WriteLine(); }

		public static void AsHelp(this string text, params object[] formatArgs) =>
			trace(text, 0, false, Help, formatArgs);

		public static void AsSystemTrace(this string text, params object[] formatArgs) =>
			trace(text, 0, false, SystemTrace, formatArgs);

		public static void AsInfo(this string text, ConsoleColor c, params object[] formatArgs) =>
			Trace(text, 2, false, c, formatArgs);

		public static void AsInfo(this string text, int leftMargin, bool split, ConsoleColor c, params object[] formatArgs) => Trace(text, leftMargin, split, c, formatArgs);

		public static void AsInfo(this string text, params object[] formatArgs) =>
			Trace(text, 2, false, Foreground, formatArgs);

		public static void AsSuccess(this string text, params object[] formatArgs) =>
			Trace(text, 2, false, Success, formatArgs);

		public static void AsInnerInfo(this string text, params object[] formatArgs) =>
			Trace(text, 4, false, Foreground, formatArgs);

		public static void AsError(this string text, params object[] formatArgs) =>
			Trace(text, 2, false, Error, formatArgs);

		public static void AsWarn(this string text, params object[] formatArgs) =>
			Trace(text, 2, false, Warn, formatArgs);

		public static void Trace(this string text, ConsoleColor c, params object[] formatArgs) =>
			Trace(text, 2, false, c, formatArgs);

		/// <summary>
		/// Traces the text if both IgnoreInfo and IgnoreAll are false.
		/// </summary>
		/// <param name="text">The text string.</param>
		/// <param name="leftMargin">Number of chars to pad each line.</param>
		/// <param name="split">Splits the text into multiple lines and adds the margin.</param>
		/// <param name="c">The trace foreground color.</param>
		/// <param name="formatArgs">The standard string format arguments.</param>
		public static void Trace(this string text, int leftMargin, bool split, ConsoleColor c, params object[] formatArgs)
		{
			if (IgnoreInfo) return;

			trace(text, leftMargin, split, c, formatArgs);
		}

		internal static void trace(this string text, int leftMargin, bool split, ConsoleColor c, params object[] formatArgs)
		{
			if (IgnoreAll) return;

			var L = split ? text.Split(SPLIT_LINE, SplitOptions) : null;
			var pass = false;

			if (SerializeTraces) pass = Monitor.TryEnter(gate, LockAwaitMS);
			else pass = true;

			if (pass)
			{
				var cc = Console.ForegroundColor;
				Console.ForegroundColor = c;

				Console.SetCursorPosition(leftMargin, Console.CursorTop);
				if (L != null)
					foreach (var line in L)
					{
						Console.SetCursorPosition(leftMargin, Console.CursorTop);
						Console.WriteLine(line, formatArgs);
					}
				else Console.WriteLine(text, formatArgs);

				Console.ForegroundColor = cc;

				if (SerializeTraces) Monitor.Exit(gate);
			}
			else if (ThrowOnLockTimeout)
				throw new TimeoutException($"Failed to acquire the trace lock in {LockAwaitMS}ms.");
		}

		/// <summary>
		/// The text splitter string.
		/// The default is Environment.NewLine.
		/// </summary>
		public static string[] SPLIT_LINE = new string[] { Environment.NewLine };

		/// <summary>
		/// By default is set to StringSplitOptions.None.
		/// </summary>
		public static StringSplitOptions SplitOptions = StringSplitOptions.None;

		/// <summary>
		/// Never traces if true.
		/// </summary>
		public static bool IgnoreAll = false;

		/// <summary>
		/// Ignores all but the SystemTrace and the test header and status
		/// </summary>
		public static bool IgnoreInfo = false;

		/// <summary>
		/// Will bail tracing after waiting the specified timeout in milliseconds.
		/// If ThrowOnLockTimeout is true will throw a TimeoutException.
		/// The default value is -1 (infinite).
		/// </summary>
		public static int LockAwaitMS = -1;

		/// <summary>
		/// By default is false.
		/// </summary>
		public static bool ThrowOnLockTimeout = false;

		/// <summary>
		/// If there is no threading involved set to false.
		/// Starts as true.
		/// </summary>
		public static bool SerializeTraces = true;

		public static void SetForegroudColor(ConsoleColor fc)
		{
			Foreground = fc;
			Console.ForegroundColor = Foreground;
		}

		public static ConsoleColor Help = ConsoleColor.DarkYellow;
		public static ConsoleColor Success = ConsoleColor.DarkGreen;
		public static ConsoleColor Error = ConsoleColor.DarkRed;
		public static ConsoleColor Warn = ConsoleColor.Yellow;

		public static ConsoleColor SystemTrace = ConsoleColor.Cyan;
		public static ConsoleColor Foreground = ConsoleColor.White;

		public static object gate = new object();
	}
}