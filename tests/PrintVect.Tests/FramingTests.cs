using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    [TestClass]
    public class FramingTests
    {
        [TestMethod]
        public void EncodeHeader_StartsWithMagicAndLittleEndianLength()
        {
            byte[] frame = Framing.EncodeHeader(RequestHeader.ForList());

            Assert.AreEqual("PVCT", Encoding.ASCII.GetString(frame, 0, 4));
            int length = frame[4] | (frame[5] << 8) | (frame[6] << 16) | (frame[7] << 24);
            Assert.AreEqual(frame.Length - 8, length);
            string json = Encoding.UTF8.GetString(frame, 8, length);
            StringAssert.Contains(json, "\"type\":\"list\"");
            StringAssert.Contains(json, "\"v\":1");
            Assert.IsFalse(json.Contains("printerId"), "absent fields are left out: " + json);
        }

        [TestMethod]
        public async Task Header_RoundTripsThroughAStream()
        {
            RequestHeader original = RequestHeader.ForJob("Counter 1 Laser", "letter.xps", "xps", 12345, "Letter to HQ", "1234");
            original.Client = "COUNTER3";
            original.User = "spm";

            var stream = new MemoryStream();
            await Framing.WriteHeaderAsync(stream, original, CancellationToken.None);
            stream.Position = 0;
            RequestHeader decoded = await Framing.ReadHeaderAsync(stream, CancellationToken.None);

            Assert.AreEqual("job", decoded.Type);
            Assert.AreEqual(1, decoded.Version);
            Assert.AreEqual("Counter 1 Laser", decoded.PrinterId);
            Assert.AreEqual(original.JobId, decoded.JobId);
            Assert.AreEqual("letter.xps", decoded.FileName);
            Assert.AreEqual("xps", decoded.Format);
            Assert.AreEqual(12345, decoded.Size);
            Assert.AreEqual("COUNTER3", decoded.Client);
            Assert.AreEqual("spm", decoded.User);
            Assert.AreEqual("Letter to HQ", decoded.Doc);
            Assert.AreEqual(PinHash.Compute("1234"), decoded.Pin);
            Assert.AreEqual(stream.Length, stream.Position, "nothing may be left unread");
        }

        [TestMethod]
        public async Task ReadHeader_RejectsWrongMagic()
        {
            var stream = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n"));

            try
            {
                await Framing.ReadHeaderAsync(stream, CancellationToken.None);
                Assert.Fail("expected a ProtocolException");
            }
            catch (ProtocolException ex)
            {
                StringAssert.Contains(ex.Message, "PVCT");
            }
        }

        [TestMethod]
        public async Task ReadHeader_RejectsOversizedLength()
        {
            var frame = new byte[8];
            Buffer.BlockCopy(ProtocolConstants.Magic, 0, frame, 0, 4);
            Framing.WriteInt32LittleEndian(frame, 4, ProtocolConstants.MaxHeaderBytes + 1);

            try
            {
                await Framing.ReadHeaderAsync(new MemoryStream(frame), CancellationToken.None);
                Assert.Fail("expected a ProtocolException");
            }
            catch (ProtocolException ex)
            {
                StringAssert.Contains(ex.Message, "length");
            }
        }

        [TestMethod]
        public async Task ReadHeader_RejectsJsonWithoutType()
        {
            byte[] json = Encoding.UTF8.GetBytes("{\"v\":1}");
            var frame = new byte[8 + json.Length];
            Buffer.BlockCopy(ProtocolConstants.Magic, 0, frame, 0, 4);
            Framing.WriteInt32LittleEndian(frame, 4, json.Length);
            Buffer.BlockCopy(json, 0, frame, 8, json.Length);

            try
            {
                await Framing.ReadHeaderAsync(new MemoryStream(frame), CancellationToken.None);
                Assert.Fail("expected a ProtocolException");
            }
            catch (ProtocolException ex)
            {
                StringAssert.Contains(ex.Message, "type");
            }
        }

        [TestMethod]
        public async Task ReadHeader_TruncatedStream_IsEndOfStream()
        {
            byte[] frame = Framing.EncodeHeader(RequestHeader.ForList());
            var truncated = new MemoryStream(frame, 0, frame.Length - 3);

            await Assert.ThrowsExceptionAsync<EndOfStreamException>(() => Framing.ReadHeaderAsync(truncated, CancellationToken.None));
        }

        [TestMethod]
        public async Task Reply_RoundTripsAsOneLine()
        {
            var stream = new MemoryStream();
            await Framing.WriteReplyAsync(stream, new JobReply { Ok = true, JobId = "j1", State = JobStates.Printed, Message = "Printed on X" }, CancellationToken.None);
            stream.Position = 0;

            string line = await Framing.ReadReplyLineAsync(stream, CancellationToken.None);
            JobReply reply = Framing.ParseReply<JobReply>(line);

            Assert.IsFalse(line.Contains("\n"));
            Assert.IsTrue(reply.Ok);
            Assert.AreEqual("j1", reply.JobId);
            Assert.AreEqual("printed", reply.State);
            Assert.AreEqual("Printed on X", reply.Message);
        }

        [TestMethod]
        public async Task ReadReplyLine_EmptyStream_IsEndOfStream()
        {
            await Assert.ThrowsExceptionAsync<EndOfStreamException>(() => Framing.ReadReplyLineAsync(new MemoryStream(), CancellationToken.None));
        }

        [TestMethod]
        public void ParseReply_GarbageIsProtocolError()
        {
            Assert.ThrowsException<ProtocolException>(() => Framing.ParseReply<JobReply>("<html>oops"));
        }

        [TestMethod]
        public void ListReply_ParsesAnErrorReplyToo()
        {
            ListReply reply = Framing.ParseReply<ListReply>("{\"ok\":false,\"state\":\"error\",\"message\":\"no\"}");

            Assert.IsFalse(reply.Ok);
            Assert.AreEqual("no", reply.Message);
            Assert.AreEqual(0, reply.Printers.Count);
        }

        [TestMethod]
        public async Task CopyExactly_CopiesExactlyAndFailsOnShortSource()
        {
            var data = new byte[200000];
            new Random(1).NextBytes(data);
            var destination = new MemoryStream();
            long reported = 0;
            var progress = new SynchronousProgress(v => reported = v);

            long copied = await Framing.CopyExactlyAsync(new MemoryStream(data), destination, 150000, progress, CancellationToken.None);

            Assert.AreEqual(150000, copied);
            Assert.AreEqual(150000, destination.Length);
            Assert.AreEqual(150000, reported);
            await Assert.ThrowsExceptionAsync<EndOfStreamException>(
                () => Framing.CopyExactlyAsync(new MemoryStream(data), new MemoryStream(), 200001, null, CancellationToken.None));
        }

        [TestMethod]
        public async Task Drain_StopsAtEndOfStream()
        {
            long drained = await Framing.DrainAsync(new MemoryStream(new byte[1000]), 5000, CancellationToken.None);

            Assert.AreEqual(1000, drained);
        }

        [TestMethod]
        public void JobFormats_FromFileName()
        {
            Assert.AreEqual("xps", JobFormats.FromFileName("Letter.XPS"));
            Assert.AreEqual("oxps", JobFormats.FromFileName("c:\\x\\page.oxps"));
            Assert.IsNull(JobFormats.FromFileName("page.pdf"));
            Assert.IsNull(JobFormats.FromFileName(null));
        }

        /// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; would need a sync context).</summary>
        private sealed class SynchronousProgress : IProgress<long>
        {
            private readonly Action<long> _handler;
            public SynchronousProgress(Action<long> handler) { _handler = handler; }
            public void Report(long value) { _handler(value); }
        }
    }

    [TestClass]
    public class PinHashTests
    {
        [TestMethod]
        public void Compute_IsLowercaseSha256Hex()
        {
            Assert.AreEqual("03ac674216f3e15c761ee1a5e255f067953623c8b388b4459e13f978d7c846f4", PinHash.Compute("1234"));
            Assert.AreEqual(64, PinHash.Compute("").Length);
        }

        [TestMethod]
        public void Matches_OpenHostAcceptsAnything()
        {
            Assert.IsTrue(PinHash.Matches("", null));
            Assert.IsTrue(PinHash.Matches(null, "whatever"));
        }

        [TestMethod]
        public void Matches_RequiresTheRightDigest()
        {
            Assert.IsTrue(PinHash.Matches("1234", PinHash.Compute("1234")));
            Assert.IsTrue(PinHash.Matches("1234", PinHash.Compute("1234").ToUpperInvariant()));
            Assert.IsFalse(PinHash.Matches("1234", PinHash.Compute("4321")));
            Assert.IsFalse(PinHash.Matches("1234", ""));
            Assert.IsFalse(PinHash.Matches("1234", null));
            Assert.IsFalse(PinHash.Matches("1234", "1234"), "the clear PIN is never accepted");
        }
    }
}
