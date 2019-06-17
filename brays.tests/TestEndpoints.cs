using System.Collections.Generic;
using System.Net;

namespace brays.tests
{
	enum Beamers
	{
		Both, A, B
	}

	struct TestEndpoints
	{
		public TestEndpoints(IDictionary<string, List<string>> args,
			Beamers b = Beamers.Both, int aPort = -1, int bPort = -1)
		{
			if (aPort < 0) aPort = APORT;
			if (bPort < 0) bPort = BPORT;

			AE = new IPEndPoint(IPAddress.Loopback, aPort);
			Beamers = b;
			IsLocalOnly = true;

			AE = new IPEndPoint(IPAddress.Loopback, aPort);
			BE = new IPEndPoint(IPAddress.Loopback, bPort);

			if (args.TryGetValue("-a", out List<string> v) && v != null &&
				v.Count > 0 && IPAddress.TryParse(v[0], out IPAddress ip))
			{
				AE = new IPEndPoint(ip, bPort);
				if (v.Count > 1) this.Beamers = Beamers.Parse<Beamers>(v[1]);
				IsLocalOnly = false;
			}

			if (args.TryGetValue("-b", out List<string> v2) && v2 != null &&
				v2.Count > 0 && IPAddress.TryParse(v2[0], out IPAddress ip2))
			{
				BE = new IPEndPoint(ip2, aPort);
				if (v2.Count > 1) this.Beamers = Beamers.Parse<Beamers>(v2[1]);
				IsLocalOnly = false;
			}
		}

		public bool A => this.Beamers == Beamers.A || this.Beamers == Beamers.Both;
		public bool B => this.Beamers == Beamers.B || this.Beamers == Beamers.Both;

		public readonly IPEndPoint AE;
		public readonly IPEndPoint BE;
		public readonly bool IsLocalOnly;
		public readonly Beamers Beamers;

		const int APORT = 3210;
		const int BPORT = 3211;
	}
}
