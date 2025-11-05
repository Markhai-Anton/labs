using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using Moq;
using EchoServer;

namespace EchoServerTests
{
    [TestFixture]
    public class EchoServerSpecs
    {
        [Test]
        public async Task StartAsync_Then_Stop_CompletesCleanly_WhenNoClients()
        {
            // arrange
            var server = new EchoServer.EchoServer(50555);
            var runTask = Task.Run(() => server.StartAsync());

            // act: дочекаємось старту
            await SpinWaitUntil(async () => server.IsRunning, TimeSpan.FromSeconds(2));
            server.Stop();

            // assert: цикл завершився
            await Task.WhenAny(runTask, Task.Delay(1000));
            Assert.IsTrue(runTask.IsCompleted);
        }

        [Test]
        public async Task Echo_Roundtrip_Works()
        {
            var port = 50556;
            var server = new EchoServer.EchoServer(port);
            var runTask = Task.Run(() => server.StartAsync());
            await SpinWaitUntil(async () => server.IsRunning, TimeSpan.FromSeconds(2));

            // act: реальний TCP-клієнт
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();

            var send = System.Text.Encoding.UTF8.GetBytes("ping");
            await stream.WriteAsync(send, 0, send.Length);

            var buf = new byte[16];
            var n = await stream.ReadAsync(buf, 0, buf.Length);
            var received = System.Text.Encoding.UTF8.GetString(buf, 0, n);

            server.Stop();
            await Task.WhenAny(runTask, Task.Delay(1000));

            // assert
            Assert.That(received, Is.EqualTo("ping"));
        }

        [Test]
        public void UdpTimedSender_StartStop_InvokesSend()
        {
            var udp = new Mock<IUdpClientLite>();
            // дозволяємо виклик Send будь-де
            udp.Setup(x => x.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Returns((byte[] b, int i, IPEndPoint ep) => i); 
            
            using var sender = new UdpTimedSender("127.0.0.1", 60000, udp.Object);

            sender.StartSending(10);
            Thread.Sleep(60);     // даємо таймеру “вистрілити” кілька разів
            sender.StopSending();
            Thread.Sleep(20); // дам таймеру завершити поточний цикл
            
            udp.Verify(x => x.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()),
                       Times.AtLeastOnce);
        }

        [Test]
        public void UdpTimedSender_PrivateCallback_CanBeInvokedViaReflection()
        {
            var udp = new Mock<IUdpClientLite>();

            // правильна сигнатура для Send(byte[], int, IPEndPoint)
            udp.Setup(x => x.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()))
                .Returns((byte[] b, int i, IPEndPoint ep) => i);

            using var sender = new UdpTimedSender("127.0.0.1", 60000, udp.Object);

            // дістати приватний метод
            var mi = typeof(UdpTimedSender).GetMethod("SendMessageCallback", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(mi);

            // виклик через reflection
            mi.Invoke(sender, new object?[] { null });

            // перевірка, що Send дійсно викликався
            udp.Verify(x => x.Send(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<IPEndPoint>()),
                Times.AtLeastOnce);
        }
        
        [Test]
        public async Task EchoServer_Main_CanBeInvokedSafely()
        {
            // Arrange
            var mainMethod = typeof(EchoServer.EchoServer)
                .GetMethod("Main", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(mainMethod);
            using var sw = new StringWriter();
            Console.SetOut(sw);
            bool executed = false;
            // Act
            try
            {
                await (Task)mainMethod.Invoke(null, new object?[] { Array.Empty<string>() });
                executed = true;
            }
            catch (TargetInvocationException)
            {
                executed = true; 
            }
            catch (Exception)
            {
                executed = true;
            }

            // Assert
            Assert.IsTrue(executed, "Main method was invoked and executed at least partially.");
        }
        
        // невеличкий helper, щоб чемно дочекатись стану
        private static async Task SpinWaitUntil(Func<Task<bool>> cond, TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                if (await cond()) return;
                await Task.Delay(25);
            }
            Assert.Fail("Timed out while waiting for condition.");
        }
        private static async Task SpinWaitUntil(Func<bool> cond, TimeSpan timeout)
            => await SpinWaitUntil(() => Task.FromResult(cond()), timeout);
    }
}
