using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
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
	}
}
