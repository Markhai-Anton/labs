using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
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
    public void GetHashCode_IsConsistent_AndDependsOnEndpoint_AndEqualsIsConsistent()
    {
        var w1 = new UdpClientWrapper(12345);
        var w2 = new UdpClientWrapper(12345);
        var w3 = new UdpClientWrapper(12346);

        int h1 = w1.GetHashCode();
        int h2 = w2.GetHashCode();
        int h3 = w3.GetHashCode();

        // однакові параметри -> однаковий hash
        Assert.That(h1, Is.EqualTo(h2));
        // інший порт -> майже напевно інший hash
        Assert.That(h1, Is.Not.EqualTo(h3));

        // перевіряємо Equals
        Assert.That(w1.Equals(w2), Is.True);
        Assert.That(w1.Equals(w3), Is.False);
        Assert.That(w1.Equals(null), Is.False);
        Assert.That(w1.Equals((object)w1), Is.True); // гілка ReferenceEquals
    }

    [Test]
    public async Task StartListeningAsync_WhenExistingCts_CancelsAndDisposesPreviousToken()
    {
        int port = GetFreeUdpPort();
        var wrapper = new UdpClientWrapper(port);

        // через reflection підставляємо вже існуючий CTS, щоб _cts?.Cancel()/Dispose виконались
        var ctsField = typeof(UdpClientWrapper)
            .GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(ctsField, Is.Not.Null);

        var oldCts = new CancellationTokenSource();
        ctsField!.SetValue(wrapper, oldCts);

        var listenTask = wrapper.StartListeningAsync();

        // трохи чекаємо, потім зупиняємо
        await Task.Delay(50);
        wrapper.StopListening();
        await Task.WhenAny(listenTask, Task.Delay(500));

        Assert.That(listenTask.IsCompleted, Is.True,
            "Listening task should complete after StopListening when existing CTS was present.");
    }
}
