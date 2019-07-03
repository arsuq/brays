using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	class SuddenEndpointChange : ITestSurface
	{
		public string Info => $"Tests abrupt port change on the source while dropping {DROPPED_FRAMES_PERCENT} percent of the tiles";
		public string Tags => "xpu, ex, drops, cfg, endpoint";
		public string FailureMessage { get; private set; }
		public bool? Passed { get; private set; }
		public bool IsComplete { get; private set; }
		public bool IndependentLaunchOnly => false;
		const int DROPPED_FRAMES_PERCENT = 20;

		public async Task Start(IDictionary<string, List<string>> args)
		{
#if !DEBUG && !ASSERT
			return;
#endif

			Beamer rayA = null;
			Beamer rayB = null;

			var rst = new ManualResetEvent(false);
			var rdm = new Random();
			var data = new byte[150_000_000];
			byte[] hash = null;

			rdm.NextBytes(data);

			using (MD5 h = MD5.Create()) hash = h.ComputeHash(data);

			try
			{
				var targ = new TestArgs(args);
				var aep = targ.AE;
				var bep = targ.BE;

				async Task receive(MemoryFragment f)
				{
					await Task.Yield();

					using (var fs = f.CreateStream())
					{
						using (MD5 h = MD5.Create())
						{
							var rhash = h.ComputeHash(fs);
							if (!Assert.SameValues(hash, rhash))
							{
								Passed = false;
								FailureMessage = "Hash difference";
							}
							else "The MD5 hashes of the sent and received bits match.".AsSuccess();

							rst.Set();
						}
					}
				}

				if (targ.A)
					rayA = new Beamer((f) => { },
					new BeamerCfg()
					{
						UseTCP = targ.UseTCP,
						Log = new BeamerLogCfg("rayA-EndpointChange", targ.Log),
#if DEBUG || ASSERT
						dropFrames = true,
						deopFramePercent = DROPPED_FRAMES_PERCENT
#endif
					});

				if (targ.B)
					rayB = new Beamer(receive, new BeamerCfg()
					{
						UseTCP = targ.UseTCP,
						Log = new BeamerLogCfg("rayB-EndpointChange", targ.Log),
#if DEBUG || ASSERT
						dropFrames = true,
						deopFramePercent = DROPPED_FRAMES_PERCENT
#endif
					});

				if (targ.A)
				{
					var ta = new Task(async () =>
					{
						await rayA.LockOn(aep, bep, -1);

						Task.Delay(20).ContinueWith(async (t) =>
						{
							try
							{
								"Changing the source endpoint...".AsInfo();
								rayA.Source.Port = 9999;
								if (!await rayA.ConfigPush())
								{
									Passed = false;
									FailureMessage = "Faiiled to push the updated config";
									rst.Set();
								}
								else
								{
									if (await rayA.LockOn(rayA.Source, rayA.Target))
										$"The beamer locked on {rayA.Source.ToString()}".AsInfo();
									else
									{
										$"The beamer failed to lock on {rayA.Source.ToString()}".AsError();
										Passed = false;
										rst.Set();
									}
								}
							}
							catch (Exception ex)
							{
								Passed = false;
								FailureMessage = ex.ToString();
								rst.Set();
							}
						});

						if (!await rayA.Beam(new HeapSlot(data)))
						{
							FailureMessage = "Failed to beam the bits";
							Passed = false;
							rst.Set();
						}
						else $"{data.Length / 1000} KB beamed".AsInfo();
					});

					ta.Start();
				}

				if (targ.B)
				{
					if (! await rayB.LockOn(bep, aep)) $"Failed to lock on rayA".AsError();
				}

				if (!rst.WaitOne(new TimeSpan(0, 1, 0)))
				{
					Passed = false;
					FailureMessage = "Timeout.";
				}

				if (!Passed.HasValue)
				{
					Passed = true;
					IsComplete = true;
				}
			}
			catch (Exception ex)
			{
				FailureMessage = ex.Message;
				Passed = false;
			}
			finally
			{
				if (rayA != null) rayA.Dispose();
				if (rayB != null) rayB.Dispose();
			}
		}
	}
}
