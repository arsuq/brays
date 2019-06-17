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
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;

		public async Task Run(IDictionary<string, List<string>> args)
		{
#if !DEBUG
			return;
#endif

			await Task.Yield();

			var te = new TestEndpoints(args);
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

			var s = te.Listen;
			var t = te.Target;
			var a = new XPU(new XCfg(
				 new BeamerCfg()
				 {
					 Log = new BeamerLogCfg("a")
#if DEBUG
					 ,dropFrames = true,
					 deopFramePercent = 20
#endif
				 },
				 new XLogCfg("a", true),
				 new HeapHighway(ushort.MaxValue)));

			var b = new XPU(new XCfg(
				 new BeamerCfg()
				 {
					 Log = new BeamerLogCfg("b")
#if DEBUG
					 ,dropFrames = true,
					 deopFramePercent = 20
#endif
				 },
				 new XLogCfg("b", true),
				 new HeapHighway(ushort.MaxValue)));

			const string F = "verify_hash";

			try
			{
				b.RegisterAPI<(byte[] data, byte[] hash)>(F, verify_hash);

				await a.Start(s, t);
				await b.Start(t, s);

				using (var ix = await a.Request<bool, (byte[], byte[])>(F, (arg, md5)))
					if (!ix.IsOK || !ix.Arg)
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

		void verify_hash(Exchange<(byte[] data, byte[] hash)> ix)
		{
			var md5 = MD5.Create().ComputeHash(ix.Arg.data);
			var eql = Assert.SameValues(md5, ix.Arg.hash);

			ix.Instance.XPU.Reply(ix.Instance, eql).Wait();
		}
	}
}
