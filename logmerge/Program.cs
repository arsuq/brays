using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace logmerge
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 2)
			{
				var fc = Console.ForegroundColor;
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("Missing one or both file paths.");
				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine();
				Console.WriteLine(" Pass two brays log file paths to merge them as follows:");
				Console.WriteLine(" The first (source) will be interleaved with the received traces in the second (remote) log. ");
				Console.WriteLine(" The source file will not be reordered but augmented with the :i lines appended after the ");
				Console.WriteLine(" corresponding frameID :o source trace line. ");
				Console.WriteLine(" The result trace order helps tracking exchanges from sender's point of view, ");
				Console.WriteLine(" so one may need to apply the reverse merge as well. ");
				Console.WriteLine($" The output will be saved as the source file with the {SAVEAS_SUFFIX} suffix.");
				Console.WriteLine();
				Console.ForegroundColor = fc;

				return;
			}

			var log1 = args[0];
			var log2 = args[1];

			var fi1 = new FileInfo(log1);
			var fi2 = new FileInfo(log2);

			if (!File.Exists(log1)) throw new Exception($"{log1} file not found.");
			if (!File.Exists(log2)) throw new Exception($"{log2} file not found.");

			Console.WriteLine($"Will merge {log2} as replies to {log1} as main.");

			try
			{
				var aLines = new List<string>(File.ReadAllLines(log1));
				var bLines = new List<string>(File.ReadAllLines(log2));

				// Lists because the clones have the same frame IDs.
				var bIndex = new Dictionary<int, List<string>>();

				foreach (var l in bLines)
				{
					var line = parseLine(l);

					// Take only the INs
					if (line.Direction == FrameDirection.In)
					{
						if (!bIndex.ContainsKey(line.FrameID))
							bIndex.Add(line.FrameID, new List<string>());

						bIndex[line.FrameID].Add(line.OriginalText);
					}
				}

				var sb = new StringBuilder();

				sb.AppendLine($"Merge of the received frames in {fi2.Name} (remote) interleaved with the {fi1.Name} (source) log.");
				sb.AppendLine($"Only :i traces are taken from the remote log (marked with :r).");

				foreach (var l in aLines)
				{
					sb.AppendLine(l);

					var line = parseLine(l);

					// Get the remote INs, if any and print all received frames with that ID
					if (line.Direction == FrameDirection.Out && bIndex.ContainsKey(line.FrameID))
						foreach (var r in bIndex[line.FrameID])
						{
							var C = r.ToCharArray();
							C[FRAME_DIRECTION_POS] = DIRECTION_REMOTE;
							sb.AppendLine(new string(C));
						}
				}

				var saveAs = Path.Combine(fi1.DirectoryName, $"{fi1.Name}-{SAVEAS_SUFFIX}{fi1.Extension}");

				File.WriteAllText(saveAs, sb.ToString());
				Console.WriteLine($"Merge saved as {saveAs}");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Parsing failed with error {ex.Message}");
			}
		}

		static Line parseLine(string line)
		{
			var l = new Line();

			l.Direction = FrameDirection.LocalTrace;
			l.OriginalText = line;

			if (line.Length > FRAME_ID_START_POS + FRAME_ID_LEN &&
				int.TryParse(line.Substring(FRAME_ID_START_POS, FRAME_ID_LEN), out l.FrameID))
			{

				var dc = line[FRAME_DIRECTION_POS];
				if (dc == DIRECTION_IN) l.Direction = FrameDirection.In;
				else if (dc == DIRECTION_OUT) l.Direction = FrameDirection.Out;
			}

			return l;
		}

		const int FRAME_ID_START_POS = 21;
		const int FRAME_ID_LEN = 10;
		const int FRAME_DIRECTION_POS = 32;
		const char DIRECTION_IN = 'i';
		const char DIRECTION_OUT = 'o';
		const char DIRECTION_REMOTE = 'r';
		const string SAVEAS_SUFFIX = "-wrem";

	}

	enum FrameDirection { LocalTrace, In, Out }

	struct Line
	{
		public int FrameID;
		public FrameDirection Direction;
		public string OriginalText;
	}
}
