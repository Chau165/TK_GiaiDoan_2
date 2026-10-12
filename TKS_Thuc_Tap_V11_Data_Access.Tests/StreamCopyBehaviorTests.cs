using System.Security.Cryptography;
using TKS_Thuc_Tap_V11_Web_Common.Common;
using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

public sealed class StreamCopyBehaviorTests
{
    [Fact]
    public async Task Partial_reads_copy_exact_bytes()
    {
        var v_arrExpected = Enumerable.Range(0, 10_123).Select(v_i => (byte)(v_i % 251)).ToArray();
        await using var v_objSource = new PartialReadStream(v_arrExpected, 4_096);
        await using var v_objDestination = new MemoryStream();

        await CStream_Copy_Utility.Copy_Async(v_objSource, v_objDestination);

        Assert.Equal(v_arrExpected.Length, v_objDestination.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(v_arrExpected)), Convert.ToHexString(SHA256.HashData(v_objDestination.ToArray())));
    }

    private sealed class PartialReadStream : Stream
    {
        private readonly MemoryStream m_objInner;
        private readonly int m_iMax_Read;

        public PartialReadStream(byte[] p_arrData, int p_iMax_Read)
        {
            m_objInner = new MemoryStream(p_arrData, writable: false);
            m_iMax_Read = p_iMax_Read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => m_objInner.Length;
        public override long Position { get => m_objInner.Position; set => m_objInner.Position = value; }

        public override int Read(byte[] p_arrBuffer, int p_iOffset, int p_iCount) =>
            m_objInner.Read(p_arrBuffer, p_iOffset, Math.Min(p_iCount, m_iMax_Read));

        public override ValueTask<int> ReadAsync(Memory<byte> p_arrBuffer, CancellationToken p_objCancellation_Token = default) =>
            m_objInner.ReadAsync(p_arrBuffer[..Math.Min(p_arrBuffer.Length, m_iMax_Read)], p_objCancellation_Token);

        public override Task<int> ReadAsync(byte[] p_arrBuffer, int p_iOffset, int p_iCount, CancellationToken p_objCancellation_Token) =>
            m_objInner.ReadAsync(p_arrBuffer, p_iOffset, Math.Min(p_iCount, m_iMax_Read), p_objCancellation_Token);

        public override long Seek(long p_lOffset, SeekOrigin p_objOrigin) => m_objInner.Seek(p_lOffset, p_objOrigin);
        public override void SetLength(long p_lValue) => throw new NotSupportedException();
        public override void Write(byte[] p_arrBuffer, int p_iOffset, int p_iCount) => throw new NotSupportedException();
        public override void Flush() { }

        protected override void Dispose(bool p_bDisposing)
        {
            if (p_bDisposing)
                m_objInner.Dispose();

            base.Dispose(p_bDisposing);
        }
    }
}
