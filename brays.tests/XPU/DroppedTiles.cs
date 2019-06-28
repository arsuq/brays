using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class DroppedTiles : ITestSurface
	{
		public string Info => "Tests request-reply of bigger than a tile args with dropping frames Beamer.";
		public string Tags => "xpu, drops";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Start(IDictionary<string, List<string>> args)
		{
#if !DEBUG
			return;
#endif

			await Task.Yield();

			var ta = new TestArgs(args);
			var arg = new byte[100_000_000];

			void fill()
			{
				var bs = new Span<byte>(arg);
				var si = MemoryMarshal.Cast<byte, int>(bs);

				for (int i = 0; i < si.Length; i++)
					si[i] = i;
			}

			fill();

			var md5 = MD5.Create().ComputeHash(arg);

			var s = ta.AE;
			var t = ta.BE;
			var a = new XPU(new XCfg(
				 new BeamerCfg()
				 {
					 Log = new BeamerLogCfg("a", ta.Log)
#if DEBUG
					 ,dropFrames = true,
					 deopFramePercent = 20
#endif
				 },
				 new XLogCfg("a", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg()
				 {
					 Log = new BeamerLogCfg("b", ta.Log)
#if DEBUG
					 ,dropFrames = true,
					 deopFramePercent = 20
#endif
				 },
				 new XLogCfg("b", ta.Log),
				 new HeapHighway(ushort.MaxValue)));

			const string F = "verify_hash";

			try
			{
				b.RegisterAPI(F, verify_hash);

				 a.Start(s, t);
				 b.Start(t, s);

				using (var ix = await a.Request(F, (arg, md5)))
					if (!ix.IsOK || !ix.Make<bool>())
					{
						Passed = false;
						FailureMessage = "Exchange failure.";
						return;
					}

				Passed = true;
				IsComplete = true;
			}
			catch (Exception ex)
			{
				Passed = false;
				FailureMessage = ex.ToString();
			}
			finally
			{
				a.Dispose();
				b.Dispose();
			}
		}

		void verify_hash(Exchange ix)
		{
			var tpl = ix.Make<(byte[] data, byte[] hash)>();

			var md5 = MD5.Create().ComputeHash(tpl.data);
			var eql = Assert.SameValues(md5, tpl.hash);

			ix.XPU.Reply(ix, eql);
		}
	}
}
