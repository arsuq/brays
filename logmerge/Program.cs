/* This Source Code Form is subject to the terms of the Mozilla Public
   License, v. 2.0. If a copy of the MPL was not distributed with this
   file, You can obtain one at http://mozilla.org/MPL/2.0/. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace logmerge
{
	class Program
	{
		static void Main(string[] args)
		{
			if (args.Length < 2)
			{
				"Missing one or both file paths.".AsError();

				Print.Line();

				" > Pass two brays log file paths to merge the received traces from the second log. ".AsHelp();
				" > Pass two bx    log file paths to merge the replies from the second log with the first one.".AsHelp();
				$" The output will be saved as the source file with the {SAVEAS_SUFFIX} suffix.".AsHelp();
				" The result trace order helps tracking exchanges from the sender's point of view, ".AsHelp();
				" so one may need to apply the reverse merge as well. ".AsHelp();

				Print.Line();

				return;
			}

			var log1 = args[0];
			var log2 = args[1];

			var saveas = args.Length > 2 ? args[2] : null;

			var fi1 = new FileInfo(log1);
			var fi2 = new FileInfo(log2);

			if (!File.Exists(log1)) throw new Exception($"{log1} file not found.");
			if (!File.Exists(log2)) throw new Exception($"{log2} file not found.");
			if (fi1.Extension != fi2.Extension) throw new Exception("The files are not of the same type.");

			var isBX = fi1.Extension == BX_EXT;

			Print.Line();

			$"  Source: {log1} ".AsWarn();
			$" Replies: {log2} ".AsWarn();

			Print.Line();

			try
			{
				var aLines = new List<string>(File.ReadAllLines(log1));
				var bLines = new List<string>(File.ReadAllLines(log2));

				// Lists because the clones (brays) have the same frame IDs.
				var bIndex = new Dictionary<int, List<string>>();

				foreach (var l in bLines)
				{
					var line = parseLine(l);
					var id = 0;

					if (isBX)
					{
						// Take the replies only

						if (line.Direction == FrameDirection.Out &&
						   (int.TryParse(line.OriginalText.Substring(BX_REFID_POS, ID_LEN), out int refid)))
							id = refid;
						else continue;
					}
					else if (line.Direction == FrameDirection.In) id = line.FrameID;
					else continue;

					if (!bIndex.ContainsKey(id)) bIndex.Add(id, new List<string>());

					bIndex[id].Add(line.OriginalText);

				}

				var sb = new StringBuilder();

				if (isBX) sb.AppendLine($"# Merge of the {fi1.Name} log file and the replies from {fi2.Name}.");
				else
				{
					sb.AppendLine(
						$"# Merge of the received frames in {fi2.Name} " +
						$"(remote) interleaved with the {fi1.Name} (source) log.");
					sb.AppendLine($"# Only :i traces are taken from the remote log (marked with :r).");
					sb.AppendLine("# The duplicated retry-frame responses are removed from all but the last out frame.");
				}

				// When frames are dropped there will be multiple lines with the same ID.
				// To avoid repeating all of the replies after each retry, all of them are
				// added after the last out frame.

				var retriesMap = new Dictionary<int, int>();
				var paLines = new List<Line>();

				foreach (var l in aLines)
				{
					var pl = parseLine(l);
					paLines.Add(pl);

					if (pl.Direction != FrameDirection.Out) continue;
					if (!retriesMap.ContainsKey(pl.FrameID))
						retriesMap[pl.FrameID] = 0;

					retriesMap[pl.FrameID]++;
				}

				foreach (var line in paLines)
				{
					sb.AppendLine(line.OriginalText);
					// Get the remote INs, if any and print all received frames with that ID
					if (line.Direction == FrameDirection.Out && bIndex.ContainsKey(line.FrameID) && --retriesMap[line.FrameID] < 1)
						foreach (var r in bIndex[line.FrameID])
						{
							var C = r.ToCharArray();
							C[FRAME_DIRECTION_POS] = DIRECTION_REMOTE;
							sb.AppendLine(new string(C));
						}
				}

				if (!string.IsNullOrWhiteSpace(saveas))
					saveas = $"{saveas}{fi1.Extension}";
				else
					saveas = Path.Combine(fi1.DirectoryName, $"{fi1.Name}-{SAVEAS_SUFFIX}{fi1.Extension}");

				File.WriteAllText(saveas, sb.ToString());
				$"Merge saved as {saveas}".AsSuccess();
			}
			catch (Exception ex)
			{
				$"Parsing failed with error {ex.Message}".AsError();
			}
		}

		static Line parseLine(string line)
		{
			var l = new Line();

			l.Direction = FrameDirection.LocalTrace;
			l.OriginalText = line;

			if (line.Length > FRAME_ID_START_POS + ID_LEN &&
				int.TryParse(line.Substring(FRAME_ID_START_POS, ID_LEN), out l.FrameID))
			{

				var dc = line[FRAME_DIRECTION_POS];
				if (dc == DIRECTION_IN) l.Direction = FrameDirection.In;
				else if (dc == DIRECTION_OUT) l.Direction = FrameDirection.Out;
			}

			return l;
		}

		const int FRAME_ID_START_POS = 19;
		const int ID_LEN = 11;
		const int FRAME_DIRECTION_POS = 31;
		const int BX_REFID_POS = 35;
		const char DIRECTION_IN = 'i';
		const char DIRECTION_OUT = 'o';
		const char DIRECTION_REMOTE = 'r';
		const string SAVEAS_SUFFIX = "-wrem";
		const string BX_EXT = ".bx";

	}

	enum FrameDirection { LocalTrace, In, Out }

	struct Line
	{
		public int FrameID;
		public FrameDirection Direction;
		public string OriginalText;
	}
}
