using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace NetSdrClientAppTests;

public class TcpClientWrapperTests
{
    private static TcpListener StartTestListener(out int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return listener;
        }

        [Test]
        public async Task Connect_Send_Receive_And_Disconnect_Works()
        {
            var listener = StartTestListener(out int port);
            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                var acceptTask = listener.AcceptTcpClientAsync();

                wrapper.Connect();
                Assert.That(wrapper.Connected, Is.True, "Wrapper should report Connected after Connect().");

                using var serverClient = await acceptTask;
                using var serverStream = serverClient.GetStream();

                var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                wrapper.MessageReceived += (_, data) => tcs.TrySetResult(data);

                await Task.Delay(50); // дати StartListeningAsync стартанути

                var payload = Encoding.UTF8.GetBytes("Hello");
                await serverStream.WriteAsync(payload, 0, payload.Length);

                var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
                Assert.That(completed, Is.SameAs(tcs.Task), "Timed out waiting for MessageReceived.");
                CollectionAssert.AreEqual(payload, tcs.Task.Result);

                // SendMessageAsync(byte[])
                var pingBytes = new byte[] { 0x01, 0x02, 0x03 };
                await wrapper.SendMessageAsync(pingBytes);

                var buffer = new byte[pingBytes.Length];
                var bytesRead = await serverStream.ReadAsync(buffer, 0, buffer.Length);
                Assert.That(bytesRead, Is.EqualTo(pingBytes.Length));
                CollectionAssert.AreEqual(pingBytes, buffer);

                // SendMessageAsync(string)
                await wrapper.SendMessageAsync("Text message");
                var buffer2 = new byte[1024];
                _ = await serverStream.ReadAsync(buffer2, 0, buffer2.Length);

                wrapper.Disconnect();
                Assert.That(wrapper.Connected, Is.False, "Wrapper should report not connected after Disconnect().");
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Connect_WhenAlreadyConnected_DoesNotReconnect()
        {
            var listener = StartTestListener(out int port);
            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                var acceptTask = listener.AcceptTcpClientAsync();

                wrapper.Connect();
                using var serverClient = acceptTask.Result;
                Assert.That(wrapper.Connected, Is.True);

                Assert.DoesNotThrow(() => wrapper.Connect());
                Assert.That(wrapper.Connected, Is.True);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Connect_WithInvalidPort_DoesNotThrowAndStaysDisconnected()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);

            var portField = typeof(TcpClientWrapper)
                .GetField("_port", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(portField, Is.Not.Null, "_port field should exist.");

            portField!.SetValue(wrapper, -1);

            Assert.DoesNotThrow(() => wrapper.Connect());
            Assert.That(wrapper.Connected, Is.False, "Wrapper should not be connected after failed Connect().");
        }

        [Test]
        public void Disconnect_WhenNotConnected_DoesNotThrow()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);

            Assert.DoesNotThrow(() => wrapper.Disconnect());
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void SendMessageAsync_WhenNotConnected_ThrowsInvalidOperationException()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);
            var data = new byte[] { 0x01, 0x02 };

            Assert.That(
                async () => await wrapper.SendMessageAsync(data),
                Throws.Exception.With.Message.Contains("Not connected to a server.")
            );
        }

        [Test]
        public async Task StartListeningAsync_WhenNotConnected_ThrowsInvalidOperationException()
        {
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);

            var method = typeof(TcpClientWrapper)
                .GetMethod("StartListeningAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(method, Is.Not.Null, "StartListeningAsync method should exist.");

            var task = (Task)method!.Invoke(wrapper, null)!;

            Assert.That(
                async () => await task,
                Throws.Exception.With.Message.Contains("Not connected to a server.")
            );
        }

        [Test]
        public async Task StartListeningAsync_GenericException_Path_IsCovered()
        {
            var listener = StartTestListener(out int port);
            TcpClient? serverClient = null;
            TcpClient? remoteClient = null;

            try
            {
                var acceptTask = listener.AcceptTcpClientAsync();

                remoteClient = new TcpClient();
                await remoteClient.ConnectAsync(IPAddress.Loopback, port);

                serverClient = await acceptTask;

                var wrapper = new TcpClientWrapper("127.0.0.1", port);

                var tcpClientField = typeof(TcpClientWrapper)
                    .GetField("_tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var streamField = typeof(TcpClientWrapper)
                    .GetField("_stream", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.That(tcpClientField, Is.Not.Null, "_tcpClient field should exist.");
                Assert.That(streamField, Is.Not.Null, "_stream field should exist.");

                tcpClientField!.SetValue(wrapper, serverClient);
                streamField!.SetValue(wrapper, serverClient.GetStream());
                // _cts залишаємо null → у while(!_cts.Token...) буде NullReferenceException,
                // який попадe у catch(Exception ex).

                var method = typeof(TcpClientWrapper)
                    .GetMethod("StartListeningAsync", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(method, Is.Not.Null, "StartListeningAsync method should exist.");

                var task = (Task)method!.Invoke(wrapper, null)!;

                await task; // виняток хендлиться всередині

                Assert.Pass("Generic exception path in StartListeningAsync was executed.");
            }
            finally
            {
                serverClient?.Close();
                remoteClient?.Close();
                listener.Stop();
            }
        }
}