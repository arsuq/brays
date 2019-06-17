using System.Collections.Generic;
using System.Net;

namespace brays.tests
{
	struct TestEndpoints
	{
		public TestEndpoints(IDictionary<string, List<string>> args, int lport = -1, int rport = -1)
		{
			if (lport < 0) lport = LPORT;
			if (rport < 0) rport = RPORT;

			Listen = new IPEndPoint(IPAddress.Loopback, lport);

			if (args.TryGetValue("remote", out List<string> v) && v != null &&
				v.Count > 0 && IPAddress.TryParse(v[0], out IPAddress ip))
				Target = new IPEndPoint(ip, rport);
			else
				Target = new IPEndPoint(IPAddress.Loopback, rport);
		}

		public readonly IPEndPoint Listen;
		public readonly IPEndPoint Target;

		const int LPORT = 3210;
		const int RPORT = 3211;
	}
}
