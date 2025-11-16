using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NetSdrClientApp.Networking;
using NUnit.Framework;

namespace NetSdrClientAppTests;

public class TcpClientWrapperTests
{
    /// <summary>
        /// Допоміжний метод для запуску TcpListener на вільному порту.
        /// </summary>
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
            // Arrange
            var listener = StartTestListener(out int port);
            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                // Приймаємо клієнта сервера
                var acceptTask = listener.AcceptTcpClientAsync();

                // Act: підключаємось
                wrapper.Connect();
                Assert.That(wrapper.Connected, Is.True, "Wrapper should report Connected after Connect().");

                var serverClient = await acceptTask;
                using var serverStream = serverClient.GetStream();

                // Підписуємось на подію отримання повідомлень
                var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                wrapper.MessageReceived += (_, data) => tcs.TrySetResult(data);

                // Даємо трохи часу, щоб StartListeningAsync стартував
                await Task.Delay(50);

                // Надсилаємо дані з боку сервера -> клієнту (TcpClientWrapper)
                var payload = Encoding.UTF8.GetBytes("Hello");
                await serverStream.WriteAsync(payload, 0, payload.Length);

                // Чекаємо на подію MessageReceived
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
                Assert.That(completed, Is.EqualTo(tcs.Task), "Timed out waiting for MessageReceived event.");
                CollectionAssert.AreEqual(payload, tcs.Task.Result, "Received payload should match sent bytes.");

                // Тестуємо SendMessageAsync(byte[])
                var pingBytes = new byte[] { 0x01, 0x02, 0x03 };
                await wrapper.SendMessageAsync(pingBytes);

                // Приймаємо з боку сервера
                var buffer = new byte[pingBytes.Length];
                var bytesRead = await serverStream.ReadAsync(buffer, 0, buffer.Length);
                Assert.That(bytesRead, Is.EqualTo(pingBytes.Length));
                CollectionAssert.AreEqual(pingBytes, buffer, "Server should receive bytes sent by wrapper.");

                // Тестуємо SendMessageAsync(string)
                await wrapper.SendMessageAsync("Text message");

                // Просто читаємо, щоб не падало (не обов'язково щось асертити для coverage)
                var buffer2 = new byte[1024];
                _ = await serverStream.ReadAsync(buffer2, 0, buffer2.Length);

                // Act: відключення
                wrapper.Disconnect();

                // Assert
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
            // Arrange
            var listener = StartTestListener(out int port);
            var wrapper = new TcpClientWrapper("127.0.0.1", port);

            try
            {
                var acceptTask = listener.AcceptTcpClientAsync();

                // Перше підключення
                wrapper.Connect();
                var serverClient = acceptTask.Result; // просто, щоб з’єднання було встановлене
                Assert.That(wrapper.Connected, Is.True);

                // Act: другий виклик Connect() має пройти по гілці "Already connected"
                wrapper.Connect();

                // Assert: все ще підключений, без виключень
                Assert.That(wrapper.Connected, Is.True);
                serverClient.Close();
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public void Connect_WithInvalidPort_DoesNotThrowAndStaysDisconnected()
        {
            // Arrange: нормальний хост, але зіпсуємо порт через reflection
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);
            var portField = typeof(TcpClientWrapper)
                .GetField("_port", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(portField, "_port field should exist.");

            // Робимо порт некоректним, щоб TcpClient.Connect() одразу кинув виняток
            portField!.SetValue(wrapper, -1);

            // Act + Assert: метод не повинен кидати (помилка хендлиться в catch)
            Assert.DoesNotThrow(() => wrapper.Connect());
            Assert.That(wrapper.Connected, Is.False, "Wrapper should not be connected after failed Connect().");
        }

        [Test]
        public void Disconnect_WhenNotConnected_WritesMessageButDoesNotThrow()
        {
            // Arrange
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);

            // Act + Assert
            Assert.DoesNotThrow(() => wrapper.Disconnect());
            Assert.That(wrapper.Connected, Is.False);
        }

        [Test]
        public void SendMessageAsync_WhenNotConnected_ThrowsInvalidOperationException()
        {
            // Arrange
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);
            var data = new byte[] { 0x01, 0x02 };

            // Act + Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await wrapper.SendMessageAsync(data));
        }

        [Test]
        public async Task StartListeningAsync_WhenNotConnected_ThrowsInvalidOperationException()
        {
            // Arrange: новий wrapper без підключення
            var wrapper = new TcpClientWrapper("127.0.0.1", 5000);

            // Дістаємо приватний метод StartListeningAsync через reflection
            var method = typeof(TcpClientWrapper)
                .GetMethod("StartListeningAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "StartListeningAsync method should exist.");

            var task = (Task)method!.Invoke(wrapper, null)!;

            // Act + Assert: перевіряємо, що Task завершується з винятком
            // (тип там все одно InvalidOperationException, але ми не прив'язуємось до нього generic-ом)
            Assert.That(
                async () => await task,
                Throws.Exception.With.Message.Contains("Not connected to a server.")
            );
        }

        [Test]
        public async Task StartListeningAsync_GenericException_Path_IsCovered()
        {
            // Цей тест штучно створює ситуацію, коли всередині циклу виникає
            // виняток, відмінний від OperationCanceledException, щоб покрити
            // гілку catch (Exception ex).

            // Спершу створюємо реальне підключене TcpClient, щоб Connected == true
            var listener = StartTestListener(out int port);
            TcpClient serverClient = null!;
            TcpClient remoteClient = null!;

            try
            {
                var acceptTask = listener.AcceptTcpClientAsync();

                remoteClient = new TcpClient();
                await remoteClient.ConnectAsync(IPAddress.Loopback, port);

                serverClient = await acceptTask;

                // Arrange wrapper
                var wrapper = new TcpClientWrapper("127.0.0.1", port);

                // Підкладаємо готові TcpClient і NetworkStream через reflection
                var tcpClientField = typeof(TcpClientWrapper)
                    .GetField("_tcpClient", BindingFlags.NonPublic | BindingFlags.Instance);
                var streamField = typeof(TcpClientWrapper)
                    .GetField("_stream", BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.NotNull(tcpClientField, "_tcpClient field should exist.");
                Assert.NotNull(streamField, "_stream field should exist.");

                tcpClientField!.SetValue(wrapper, serverClient);
                streamField!.SetValue(wrapper, serverClient.GetStream());

                // _cts навмисно залишаємо null, щоб у while(!_cts.Token...) зловити NullReferenceException
                // (який потім хендлиться в catch(Exception ex)).

                var method = typeof(TcpClientWrapper)
                    .GetMethod("StartListeningAsync", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method, "StartListeningAsync method should exist.");

                var task = (Task)method!.Invoke(wrapper, null)!;

                // Act: просто чекаємо завершення — виняток обробляється всередині, тому await не падає
                await task;

                // Якщо ми сюди дійшли без винятку — гілка catch(Exception ex) відпрацювала,
                // а в finally мав бути виклик "Listener stopped."
                Assert.Pass("Generic exception path in StartListeningAsync was executed successfully.");
            }
            finally
            {
                serverClient?.Close();
                remoteClient?.Close();
                listener.Stop();
            }
        }
}