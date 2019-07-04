using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using TestSurface;

namespace brays.tests
{
	public class ConSocketSurface : ITestSurface
	{
		public string Info => "";
		public string Tags => "";
		public string FailureMessage { get; set; }
		public bool? Passed { get; set; }
		public bool IsComplete { get; set; }
		public bool IndependentLaunchOnly => true;

		public async Task Start(IDictionary<string, List<string>> args)
		{
			bind();
			// tcp();
		}

		void bind()
		{
			var a = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			var b = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			var c = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			var ae = new IPEndPoint(IPAddress.Loopback, 55000);
			var be = new IPEndPoint(IPAddress.Loopback, 55001);
			var ce = new IPEndPoint(IPAddress.Loopback, 55002);

			a.Bind(ae);
			b.Bind(be);
			c.Bind(ce);

			a.Connect(be); // If not connected to 'b', 'a' will receive from everywhere.
			b.Connect(ae);
			c.Connect(ae);

			// 'a' doesn't connect to 'c' so no dgrams should be received from it

			var ab = new byte[1000];
			var bb = new byte[1000];
			var cb = new byte[1000];

			new Task(() =>
			{
				while (true)
				{
					var read = a.Receive(ab, SocketFlags.None);
					$"a: received {read} bytes".AsInfo();
				}
			}).Start();

			var send = new byte[] { 1, 2, 3 };

			b.Send(send, SocketFlags.None);

			$"b sent".AsInfo();
			Console.ReadLine();

			c.Send(send, SocketFlags.None);

			$"c sent".AsInfo();
			Console.ReadLine();
		}

		void tcp()
		{
			// This is supposed to fail

			var a = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			var b = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			var ae = new IPEndPoint(IPAddress.Loopback, 55000);
			var be = new IPEndPoint(IPAddress.Loopback, 55001);

			var ta = Task.Run(() =>
			{
				try
				{
					a.Bind(ae);
					a.Connect(be);

					var buff = new byte[100];

					for (int i = 0; i < 10; i++)
					{
						var read = a.Receive(buff);

						for (int j = 0; j < read; j++)
							if (buff[j] != buff[0])
							{
								Passed = false;
								FailureMessage = "Received wrong data, as expected";
								break;
							}

						$"a-read:{read}".AsInnerInfo();
					}
				}
				catch (Exception ex)
				{
					Passed = false;
					FailureMessage = ex.ToString();
				}
			});

			var tb = Task.Run(() =>
			{
				try
				{
					b.Bind(be);
					b.Connect(ae);

					var rdm = new Random();

					Parallel.For(0, 10, (i) =>
					{
						var size = rdm.Next(10, 80);
						var buff = new byte[size];

						for (int j = 0; j < size; j++)
							buff[j] = (byte)i;

						var span = buff.AsSpan().Slice(0, size);
						b.Send(span);
						$"b-send:{i}".AsInnerInfo();
					});
				}
				catch (Exception ex)
				{
					Passed = false;
					FailureMessage = ex.ToString();
				}
			});

			Task.WaitAll(ta, tb);

		}
	}
}
