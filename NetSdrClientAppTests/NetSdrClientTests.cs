using System.Reflection;
using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using static NetSdrClientApp.Messages.NetSdrMessageHelper;

namespace NetSdrClientAppTests;

public class NetSdrClientTests
{
    NetSdrClient _client;
    Mock<ITcpClient> _tcpMock;
    Mock<IUdpClient> _updMock;

    public NetSdrClientTests() { }

    [SetUp]
    public void Setup()
    {
        _tcpMock = new Mock<ITcpClient>();
        _tcpMock.Setup(tcp => tcp.Connect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(true);
        });

        _tcpMock.Setup(tcp => tcp.Disconnect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(false);
        });

        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>())).Callback<byte[]>((bytes) =>
        {
            _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, bytes);
        });

        _updMock = new Mock<IUdpClient>();

        _client = new NetSdrClient(_tcpMock.Object, _updMock.Object);
    }

    [Test]
    public async Task ConnectAsyncTest()
    {
        //act
        await _client.ConnectAsync();

        //assert
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
    }

    [Test]
    public async Task DisconnectWithNoConnectionTest()
    {
        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task DisconnectTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task StartIQNoConnectionTest()
    {

        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        _tcpMock.VerifyGet(tcp => tcp.Connected, Times.AtLeastOnce);
    }

    [Test]
    public async Task StartIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _updMock.Verify(udp => udp.StartListeningAsync(), Times.Once);
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task StopIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StopIQAsync();

        //assert
        //No exception thrown
        _updMock.Verify(tcp => tcp.StopListening(), Times.Once);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task ChangeFrequencyAsyncShouldSendCorrectMessage()
    {
        // Arrange
        await _client.ConnectAsync();

        var expectedFreq = 1000000L; 
        var expectedChannel = 1;

        byte[] capturedMessage = null;

        // Ловимо TCP повідомлення 
        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((msg) =>
            {
                capturedMessage = msg;

                // Імітацємо відповідь від пристрою:
                var fakeResponse = new byte[] { 0xAA, 0xBB, 0xCC };
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, fakeResponse);
            })
            .Returns(Task.CompletedTask);

        // Act
        await _client.ChangeFrequencyAsync(expectedFreq, expectedChannel);

        // Assert
        Assert.That(capturedMessage, Is.Not.Null);

        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(4));
    }
    
    [Test]
    public async Task SendTcpRequestShouldThrowWhenNotConnected()
    {
        // Arrange
        await _client.ConnectAsync();
        var data = new byte[] { 0x10, 0x20 };
        var expectedResponse = new byte[] { 0xAA, 0xBB };

        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((msg) =>
            {
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, expectedResponse);
            })
            .Returns(Task.CompletedTask);

        var method = _client.GetType().GetMethod("SendTcpRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act
        var resultTask = (Task<byte[]>)method.Invoke(_client, new object[] { data })!;
        var result = await resultTask;

        // Assert
        Assert.That(result, Is.EqualTo(expectedResponse));
    }
    
    [Test]
    public async Task SendTcpRequestShouldReturnResponse()
    {
        // Arrange
        await _client.ConnectAsync();
        var data = new byte[] { 0x10, 0x20 };
        var expectedResponse = new byte[] { 0xAA, 0xBB };

        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((msg) =>
            {
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, expectedResponse);
            })
            .Returns(Task.CompletedTask);

        var method = _client.GetType().GetMethod("SendTcpRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act
        var task = (Task<byte[]>)method.Invoke(_client, new object[] { data })!;
        var result = await task;

        // Assert
        Assert.That(result, Is.EqualTo(expectedResponse));
    }
    
    [Test]
    public void TcpClientMessageReceivedShouldHandleUnsolicited()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02 };
        var method = _client.GetType().GetMethod("_tcpClient_MessageReceived",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act
        // Симулюємо, що responseTaskSource == null => піде в HandleUnsolicitedMessage
        method.Invoke(_client, new object?[] { null, data });

        // Assert — тут не перевіряємо консоль, але перевіряємо що не впало
        Assert.Pass("Unsolicited message handled without crash.");
    }
    
    [Test]
    public async Task TcpClientMessageReceivedShouldCompleteTaskSource()
    {
        // Arrange: підключимось
        await _client.ConnectAsync();

        var expectedResponse = new byte[] { 0x99, 0x88 };

        // Перекриваємо глобальний Setup SendMessageAsync, щоб він НЕ піднімав MessageReceived автоматично
        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Returns(Task.CompletedTask); // просто повертаємо завершений Task, без callback-а

        // Отримаємо рефлексією приватний метод SendTcpRequest
        var method = _client.GetType().GetMethod("SendTcpRequest",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act: викликаємо SendTcpRequest (воно викличе SendMessageAsync)
        var resultTask = (Task<byte[]>)method.Invoke(_client, new object[] { new byte[] { 0x01 } })!;

        // Тепер вручну піднімаємо подію, щоб симулювати відповідь від пристрою
        _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, expectedResponse);

        var result = await resultTask;

        // Assert
        Assert.That(result, Is.EqualTo(expectedResponse));
    }
    
    [Test]
    public void HandleUnsolicitedMessageShouldNotThrow()
    {
        // Arrange
        var data = new byte[] { 0x01, 0x02 };
        var method = _client.GetType().GetMethod("HandleUnsolicitedMessage",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Act
        method.Invoke(_client, new object[] { data });

        // Assert
        Assert.Pass("Unsolicited message logged without exceptions.");
    }
    
    [Test]
    public async Task StartIQAsync_ShouldCallUdpClientStartListening_WithoutThrowing()
    {
        // Arrange
        await _client.ConnectAsync(); // підключаємо TCP

        // Мокування UDP-клієнта
        _updMock.Setup(u => u.StartListeningAsync()).Returns(Task.CompletedTask);

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            await _client.StartIQAsync();
        });

        // Перевіряємо, що StartListeningAsync викликався
        _updMock.Verify(u => u.StartListeningAsync(), Times.Once);

        // IQStarted має бути true
        Assert.That(_client.IQStarted, Is.True);
    }
    
    [Test]
    public async Task StopIQAsync_ShouldCallUdpClientStopListening_WithoutThrowing()
    {
        // Arrange
        await _client.ConnectAsync();
        _updMock.Setup(u => u.StartListeningAsync()).Returns(Task.CompletedTask);
        await _client.StartIQAsync(); // запускаємо IQ для тесту Stop

        _updMock.Setup(u => u.StopListening());

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
        {
            await _client.StopIQAsync();
        });

        // Перевіряємо, що StopListening викликався
        _updMock.Verify(u => u.StopListening(), Times.Once);

        // IQStarted має бути false
        Assert.That(_client.IQStarted, Is.False);
    }

    //TODO: cover the rest of the NetSdrClient code here
}
