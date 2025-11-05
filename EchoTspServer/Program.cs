using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EchoServer
{
    public interface IUdpClientLite : IDisposable
    {
        int Send(byte[] datagram, int bytes, IPEndPoint endPoint);
    }
    
    // адаптер над System.Net.Sockets.UdpClient
    internal sealed class UdpClientAdapter : IUdpClientLite
    {
        private readonly UdpClient _inner = new UdpClient();
        public int Send(byte[] datagram, int bytes, IPEndPoint endPoint) => _inner.Send(datagram, bytes, endPoint);
        public void Dispose() => _inner.Dispose();
    }
    
    public class EchoServer
    {
        private readonly int _port;
        private TcpListener _listener;
        private CancellationTokenSource _cancellationTokenSource;

        public bool IsRunning => _listener != null && _listener.Server?.IsBound == true;
        
        //constuctor
        public EchoServer(int port)
        {
            _port = port;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();
            Console.WriteLine($"Server started on port {_port}.");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_listener.Pending())
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        Console.WriteLine("Client connected.");
                        _ = Task.Run(() => HandleClientAsync(client, _cancellationTokenSource.Token));
                    }
                    else
                    {
                        await Task.Delay(25, _cancellationTokenSource.Token); 
                        // дрібна пауза, щоб Stop() встиг прервати цикл
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Listener has been closed
                    break;
                }
                catch (OperationCanceledException)
                {
                    //_listener.Stop() кинув — виходимо 
                    break;
                }
            }

            Console.WriteLine("Server shutdown.");
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;

                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        // Echo back the received message
                        await stream.WriteAsync(buffer, 0, bytesRead, token);
                        Console.WriteLine($"Echoed {bytesRead} bytes to the client.");
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
                finally
                {
                    client.Close();
                    Console.WriteLine("Client disconnected.");
                }
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _listener.Stop();
            _cancellationTokenSource.Dispose();
            Console.WriteLine("Server stopped.");
        }

        public static async Task Main(string[] args)
        {
            EchoServer server = new EchoServer(5000);

            // Start the server in a separate task
            _ = Task.Run(() => server.StartAsync());

            string host = "127.0.0.1"; // Target IP
            int port = 60000;          // Target Port
            int intervalMilliseconds = 5000; // Send every 3 seconds

            using (var sender = new UdpTimedSender(host, port))
            {
                Console.WriteLine("Press any key to stop sending...");
                sender.StartSending(intervalMilliseconds);

                Console.WriteLine("Press 'q' to quit...");
                while (Console.ReadKey(intercept: true).Key != ConsoleKey.Q)
                {
                    // Just wait until 'q' is pressed
                }

                sender.StopSending();
                server.Stop();
                Console.WriteLine("Sender stopped.");
            }
        }
    }


    public class UdpTimedSender : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private Timer _timer;
        private ushort i = 0;
        
        private readonly IUdpClientLite _udpClient;

        public UdpTimedSender(string host, int port, IUdpClientLite udp = null)
        {
            _host = host;
            _port = port;
            _udpClient = udp ?? new UdpClientAdapter(); // за замовчуванням реальний клієнт
        }

        public void StartSending(int intervalMilliseconds)
        {
            if (_timer != null)
                throw new InvalidOperationException("Sender is already running.");

            _timer = new Timer(SendMessageCallback, null, 0, intervalMilliseconds);
        }
        
        private void SendMessageCallback(object state)
        {
            try
            {
                //dummy data
                Random rnd = new Random();
                byte[] samples = new byte[1024];
                rnd.NextBytes(samples);
                i++;

                byte[] msg = (new byte[] { 0x04, 0x84 }).Concat(BitConverter.GetBytes(i)).Concat(samples).ToArray();
                var endpoint = new IPEndPoint(IPAddress.Parse(_host), _port);

                _udpClient.Send(msg, msg.Length, endpoint);
                Console.WriteLine($"Message sent to {_host}:{_port} ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        public void StopSending()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            StopSending();
            _udpClient.Dispose();
        }
    }
}