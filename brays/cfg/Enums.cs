
namespace brays
{
	enum Lead : byte
	{
		Probe,
		Signal,
		Error,
		Block,
		Status,
	}

	enum SignalKind : int
	{
		NACK,
		ACK,
		UNK,
		ERR
	}

	enum FrameOptions : byte
	{
		None,
		ReqAckBlockTransfer
	}

	public enum ErrorCode : int
	{
		NoTranferAck,
	}
}
