using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ConsoleApp3
{
    public class ABXClient
    {
        private const string Host = "localhost";
        private const int Port = 3000;
        private const int MaxRetries = 5;

        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected;

        public ABXClient()
        {
            ConnectWithRetry();
        }

        private void ConnectWithRetry()
        {
            int attempt = 0;
            while (attempt < MaxRetries)
            {
                try
                {
                    _client = new TcpClient(Host, Port);
                    _stream = _client.GetStream();
                    _isConnected = true;
                    Console.WriteLine("Connection established.");
                    return;
                }
                catch (SocketException ex)
                {
                    attempt++;
                    Console.WriteLine($"Connection attempt {attempt} failed: {ex.Message}");
                    Thread.Sleep(2000); // Wait before retrying
                }
            }

            throw new Exception("Failed to establish connection after multiple attempts.");
        }

        public void StreamAllPackets()
        {
            int retryCount = 0;
            const int maxRetries = 3;

            while (retryCount < maxRetries)
            {
                try
                {
                    SendRequest(1); // Request to stream all packets
                    List<Packet> packets = ReceivePackets();

                    // Check for missing sequences and request them
                    List<int> missingSequences = FindMissingSequences(packets);
                    foreach (int seq in missingSequences)
                    {
                        if (_isConnected) // Ensure connection is still open before sending
                        {
                            SendRequest(2, seq); // Request to resend specific packet
                            packets.AddRange(ReceivePackets());
                        }
                    }

                    // Write to JSON
                    WriteToJson(packets);

                    // Exit loop if successful
                    break;
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Network error: {ex.Message}");
                    LogError(ex);
                    retryCount++;
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.ConnectionReset)
                    {
                        Console.WriteLine("Connection reset by the server.");
                        retryCount++;
                    }
                    else
                    {
                        Console.WriteLine($"Socket error: {ex.Message}");
                        LogError(ex);
                        retryCount++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                    LogError(ex);
                    retryCount++;
                }

                if (retryCount < maxRetries)
                {
                    Console.WriteLine("Retrying connection...");
                    Close();
                    Thread.Sleep(2000); // Wait before retrying
                    ConnectWithRetry(); // Attempt to reconnect
                }
                else
                {
                    Console.WriteLine("Max retries reached. Exiting.");
                }
            }
        }


        private void SendRequest(byte callType, int resendSeq = 0)
        {
            if (_isConnected && _stream.CanWrite)
            {
                try
                {
                    byte[] payload = new byte[5];
                    payload[0] = callType;
                    if (callType == 2)
                    {
                        payload[1] = (byte)resendSeq;
                    }
                    _stream.Write(payload, 0, payload.Length);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending request: {ex.Message}");
                    LogError(ex);
                    _isConnected = false;
                    Close(); // Close the connection if there is an error
                }

              
            }
            else
            {
                Console.WriteLine("Cannot send request: stream is closed.");
            }
        }

        private List<Packet> ReceivePackets()
        {
            List<Packet> packets = new List<Packet>();
            byte[] buffer = new byte[17]; // size of one packet
            int bytesRead;

            try
            {
                while (_isConnected && (bytesRead = _stream.Read(buffer, 0, buffer.Length)) != 0)
                {
                    if (bytesRead == 17)
                    {
                        try
                        {
                            Packet packet = ParsePacket(buffer);
                            packets.Add(packet);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing packet: {ex.Message}");
                            LogError(ex);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Incomplete packet received, ignoring.");
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Network error: {ex.Message}");
                LogError(ex);
                _isConnected = false;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Console.WriteLine("Connection reset by the server.");
                }
                else
                {
                    Console.WriteLine($"Socket error during receive: {ex.Message}");
                }
                LogError(ex);
                _isConnected = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                LogError(ex);
                _isConnected = false;
            }
            finally
            {
                // Close the connection gracefully
                Close();
            }

            return packets;
        }


        private Packet ParsePacket(byte[] data)
        {
            if (data.Length != 17)
            {
                throw new ArgumentException("Invalid packet size.");
            }

            string symbol = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            char buySellIndicator = (char)data[4];
            int quantity = BitConverter.ToInt32(data, 5);
            int price = BitConverter.ToInt32(data, 9);
            int sequence = BitConverter.ToInt32(data, 13);

            return new Packet
            {
                Symbol = symbol,
                BuySellIndicator = buySellIndicator,
                Quantity = quantity,
                Price = price,
                Sequence = sequence
            };
        }

        private List<int> FindMissingSequences(List<Packet> packets)
        {
            List<int> missingSequences = new List<int>();
            packets.Sort((p1, p2) => p1.Sequence.CompareTo(p2.Sequence));

            for (int i = 0; i < packets.Count - 1; i++)
            {
                int currentSeq = packets[i].Sequence;
                int nextSeq = packets[i + 1].Sequence;
                for (int seq = currentSeq + 1; seq < nextSeq; seq++)
                {
                    missingSequences.Add(seq);
                }
            }

            return missingSequences;
        }

        private void WriteToJson(List<Packet> packets)
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(packets);
                File.WriteAllText("output.json", jsonString);
                Console.WriteLine("JSON output written to output.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing JSON: {ex.Message}");
                LogError(ex);
            }
        }

        private void LogError(Exception ex)
        {
            File.AppendAllText("error.log", $"{DateTime.Now}: {ex.Message}\n");
        }

        public void Close()
        {
            try
            {
                if (_stream != null && _stream.CanWrite)
                {
                    _stream.Close();
                }
                _client?.Close();
                _isConnected = false;
                Console.WriteLine("Connection closed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing connection: {ex.Message}");
                LogError(ex);
            }
        }

    }

    public class Packet
    {
        public string Symbol { get; set; }
        public char BuySellIndicator { get; set; }
        public int Quantity { get; set; }
        public int Price { get; set; }
        public int Sequence { get; set; }
    }
}
