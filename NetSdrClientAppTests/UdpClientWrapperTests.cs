using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NetSdrClientApp.Networking; 
using NUnit.Framework;

namespace NetSdrClientAppTests;
[TestFixture]
public class UdpClientWrapperTests
{
    private static int GetFreeUdpPort()
        {
            using var udp = new UdpClient(0);
            return ((IPEndPoint)udp.Client.LocalEndPoint!).Port;
        }

        [Test]
        public async Task StartListeningAsync_ReceivesMessage_RaisesEvent_AndCanBeStopped()
        {
            // arrange
            int port = GetFreeUdpPort();
            var wrapper = new UdpClientWrapper(port);

            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            wrapper.MessageReceived += (_, data) => tcs.TrySetResult(data);

            var listenTask = wrapper.StartListeningAsync();

            // даємо трохи часу, щоб UdpClient всередині встиг створитися
            await Task.Delay(50);

            // act: надсилаємо UDP-пакет на цей порт
            using var sender = new UdpClient();
            var payload = Encoding.UTF8.GetBytes("hello");
            await sender.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Loopback, port));

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));

            // assert: подія викликалась і дані коректні
            Assert.That(completed, Is.SameAs(tcs.Task), "Timed out waiting for UDP message.");
            Assert.That(tcs.Task.Result, Is.EqualTo(payload));

            // зупиняємо слухання й переконуємось, що цикл завершився
            wrapper.StopListening();
            await Task.WhenAny(listenTask, Task.Delay(1000));
            Assert.That(listenTask.IsCompleted, Is.True, "Listening loop should complete after StopListening().");
        }

        [Test]
        public async Task StartListeningAsync_WhenPortAlreadyInUse_CompletesAndDoesNotThrow()
        {
            // займаємо порт окремим UdpClient, щоб створення всередині wrapper кинуло SocketException
            using var occupied = new UdpClient(0);
            int port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;

            var wrapper = new UdpClientWrapper(port);

            // act
            var task = wrapper.StartListeningAsync();

            // assert: метод не повинен падати назовні, а просто завершитися в catch(Exception)
            await Task.WhenAny(task, Task.Delay(1000));
            Assert.That(task.IsCompleted, Is.True, "StartListeningAsync should complete when port is already in use.");
        }

        [Test]
        public void StopListening_WithoutStart_DoesNotThrow()
        {
            int port = GetFreeUdpPort();
            var wrapper = new UdpClientWrapper(port);

            Assert.That(() => wrapper.StopListening(), Throws.Nothing);
        }

        [Test]
        public void GetHashCode_IsConsistent_AndDependsOnEndpoint()
        {
            var w1 = new UdpClientWrapper(12345);
            var w2 = new UdpClientWrapper(12345);
            var w3 = new UdpClientWrapper(12346);

            int h1 = w1.GetHashCode();
            int h2 = w2.GetHashCode();
            int h3 = w3.GetHashCode();

            // однакові параметри -> однаковий hash
            Assert.That(h1, Is.EqualTo(h2));

            // інший порт -> майже напевно інший hash (колізія MD5 тут практично нереальна)
            Assert.That(h1, Is.Not.EqualTo(h3));
        }
}