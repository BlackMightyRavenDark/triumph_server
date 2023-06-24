using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.IO;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;

namespace triumph_server
{
    internal class Program
    {
        private static readonly string selfDir = Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
        private static string publicDir;
        private static readonly Dictionary<string, string> contentTypes = new Dictionary<string, string>();

        static void Main(string[] args)
        {
            publicDir = Path.Combine(selfDir, "public");
            string mimeFilePath = Path.Combine(selfDir, "mime.txt");
            if (File.Exists(mimeFilePath))
            {
                LoadContentTypes(mimeFilePath);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error! mime.txt not found!");
                Console.ForegroundColor = ConsoleColor.White;
            }

            const int serverPort = 7777;
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, serverPort);
            Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(endPoint);
            server.Listen((int)SocketOptionName.MaxConnections);

            Console.WriteLine($"Server started on port {serverPort}");

            while (true)
            {
                Socket client = server.Accept();
                LogEvent($"{client.RemoteEndPoint} is connected");

                ProcessClient(client);
                DisconnectClient(client);
            }

            StopServer(server);
        }

        private static void ProcessClient(Socket client)
        {
            byte[] buffer = new byte[ushort.MaxValue];
            int bytesRead = client.Receive(buffer, 0, buffer.Length, SocketFlags.None);
            if (bytesRead == 0)
            {
                LogEvent($"Zero bytes received from {client.RemoteEndPoint}");
                return;
            }

            string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            string[] strings = msg.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            string[] request = strings[0].Split(new char[] { ' ' }, 3);
            if (request.Length == 3 && request[0] == "GET")
            {
                string fileRequested = request[1] == "/" ? "index.html" : request[1].Remove(0, 1);
                if (fileRequested.StartsWith("catalog"))
                {
                    ProcessCatalogRequest(client, fileRequested.Substring(7));
                    return;
                }

                ProcessFileRequest(client, fileRequested, null);
            }
            else
            {
                SendMessage(client, GenerateResponse(400, "Client error", "Invalid request"));
            }
        }

        private static void ProcessCatalogRequest(Socket client, string request)
        {
            Dictionary<string, string> dict = SplitUrlQueryToDictionary(request);
            if (dict != null && dict.ContainsKey("category"))
            {
                string category = dict["category"];
                if (!string.IsNullOrEmpty(category) && !string.IsNullOrWhiteSpace(category))
                {
                    category = Uri.UnescapeDataString(category).Trim();
                    LogEvent($"{client.RemoteEndPoint} requested a category: {category}");
                    string dir = Path.Combine(publicDir, Path.Combine("images", category));

                    JArray jsonArr = new JArray();
                    if (Directory.Exists(dir))
                    {
                        var files = Directory.GetFiles(dir).Where(str => IsImageFile(str));
                        int n = publicDir.Length;
                        foreach (string str in files)
                        {
                            jsonArr.Add(str.Substring(n));
                        }
                    }

                    byte[] bytes = Encoding.UTF8.GetBytes(jsonArr.ToString());
                    SendData(client, bytes, ".json");
                }
                else
                {
                    SendMessage(client, GenerateResponse(400, "Client error", "No category specified!"));
                }
            }
            else
            {
                SendMessage(client, GenerateResponse(404, "Not found", "Not found!"));
            }
        }

        private static void ProcessFileRequest(Socket client, string requestedFilePath, NameValueCollection headers)
        {
            if (string.IsNullOrEmpty(requestedFilePath) || string.IsNullOrWhiteSpace(requestedFilePath))
            {
                SendMessage(client, GenerateResponse(404, "Client error", "No file specified"));
                return;
            }

            string decodedFilePath = Uri.UnescapeDataString(requestedFilePath);
            LogEvent($"{client.RemoteEndPoint} requested file {decodedFilePath}");

            if (decodedFilePath.StartsWith("/"))
            {
                decodedFilePath = decodedFilePath.Remove(0, 1);
            }

            string fullFilePath = Path.Combine(publicDir, decodedFilePath.Replace("/", "\\"));
            if (File.Exists(fullFilePath))
            {
                SendFileAsStream(client, fullFilePath, 0L, -1L);
            }
            else
            {
                SendMessage(client, GenerateResponse(404, "Not found", "File not found"));
            }
        }

        private static void SendFileAsStream(Socket client, string filePath, long byteStart, long byteEnd)
        {
            try
            {
                using (Stream stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    stream.Position = byteStart;

                    if (byteEnd == -1L || byteEnd < byteStart)
                    {
                        byteEnd = stream.Length == 0L ? 0 : stream.Length - 1L;
                    }
                    long segmentSize = byteStart == byteEnd ? 1L : byteEnd - byteStart + 1;
                    string fileExt = Path.GetExtension(filePath)?.ToLower();
                    int errorCode = segmentSize == stream.Length ? 200 : 206;
                    string t = $"HTTP/1.1 {errorCode} OK\r\nContent-Type: {GetContentType(fileExt)}\r\n" +
                        $"Content-Length: {segmentSize}\r\n" +
                        "Access-Control-Allow-Origin: *\r\n\r\n";
                    byte[] buffer = Encoding.UTF8.GetBytes(t);
                    client.Send(buffer, SocketFlags.None);

                    long remaining = segmentSize;
                    buffer = new byte[4096];
                    while (remaining > 0L && client.Connected)
                    {
                        int bytesToRead = remaining > buffer.LongLength ? buffer.Length : (int)remaining;
                        int bytesRead = stream.Read(buffer, 0, bytesToRead);
                        if (bytesRead <= 0)
                        {
                            break;
                        }

                        client.Send(buffer, 0, bytesRead, SocketFlags.None, out SocketError socketError);
                        if (socketError != SocketError.Success)
                        {
                            System.Diagnostics.Debug.WriteLine($"File transferring error: {socketError}!");
                            break;
                        }

                        remaining -= bytesRead;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private static void SendData(Socket client, byte[] data, string fileExtension)
        {
            string t = $"HTTP/1.1 200 OK\r\n" +
                "Access-Control-Allow-Origin: *\r\n";
            t += $"Content-Type: {GetContentType(fileExtension?.ToLower())}\r\n" +
                $"Content-Length: {data.Length}\r\n\r\n";
            byte[] header = Encoding.UTF8.GetBytes(t);
            byte[] buffer = new byte[header.Length + data.Length];
            for (int i = 0; i < header.Length; ++i)
            {
                buffer[i] = header[i];
            }
            for (int i = 0; i < data.Length; ++i)
            {
                buffer[i + header.Length] = data[i];
            }

            try
            {
                client.Send(buffer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private static void SendMessage(Socket client, string msg)
        {
            byte[] msgBytes = Encoding.UTF8.GetBytes(msg);
            client.Send(msgBytes);
        }

        private static void DisconnectClient(Socket client)
        {
            LogEvent($"{client.RemoteEndPoint} is disconnected");
            client.Shutdown(SocketShutdown.Both);
            client.Close();
        }

        private static void StopServer(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                if (ex is SocketException)
                {
                    System.Diagnostics.Debug.WriteLine($"Socket error {(ex as SocketException).ErrorCode}");
                }
                socket.Close();
            }
        }

        private static void LoadContentTypes(string filePath)
        {
            string fileContent = File.ReadAllText(filePath);
            string[] strings = fileContent.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            foreach (string str in strings)
            {
                if (string.IsNullOrEmpty(str) || string.IsNullOrWhiteSpace(str) || str.StartsWith("#"))
                {
                    continue;
                }

                string[] keyValue = str.Split(new char[] { '|' }, 2);
                if (keyValue != null && keyValue.Length == 2)
                {
                    if (!string.IsNullOrEmpty(keyValue[0]) && !string.IsNullOrWhiteSpace(keyValue[0]))
                    {
                        string contentTypeValueTrimmed = keyValue[0].Trim();
                        string[] extensions = keyValue[1].ToLower().Split(',');
                        if (extensions != null && extensions.Length > 0)
                        {
                            foreach (string extension in extensions)
                            {
                                if (!string.IsNullOrEmpty(extension) && !string.IsNullOrWhiteSpace(extension))
                                {
                                    string extensionTrimmed = extension.Trim();
                                    if (!extensionTrimmed.Contains(" ") && extensionTrimmed.StartsWith("."))
                                    {
                                        contentTypes.Add(extensionTrimmed, contentTypeValueTrimmed);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private static string GenerateResponse(int errorCode, string msg, string body)
        {
            string t = $"HTTP/1.1 {errorCode} {msg}\r\n" +
                "Access-Control-Allow-Origin: *\r\n";
            if (!string.IsNullOrEmpty(body))
            {
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                t += "Content-Type: text/plain; charset=UTF-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n\r\n{body}";
            }
            else
            {
                t += "Content-Length: 0\r\n\r\n";
            }
            return t;
        }

        private static void LogEvent(string eventText)
        {
            string dateTime = DateTime.Now.ToString("yyyy.MM.dd hh:mm:ss");
            Console.WriteLine($"{dateTime}> {eventText}");
        }

        private static string GetContentType(string ext)
        {
            return contentTypes != null && !string.IsNullOrEmpty(ext) && !string.IsNullOrWhiteSpace(ext) &&
                contentTypes.ContainsKey(ext) ? contentTypes[ext] :
                "text/plain; charset=UTF-8";
        }

        public static Dictionary<string, string> SplitUrlQueryToDictionary(string urlQuery)
        {
            if (string.IsNullOrEmpty(urlQuery) || string.IsNullOrWhiteSpace(urlQuery))
            {
                return null;
            }
            if (urlQuery[0] == '?')
            {
                urlQuery = urlQuery.Remove(0, 1);
            }
            return SplitStringToKeyValues(urlQuery, '&', '=');
        }

        public static Dictionary<string, string> SplitStringToKeyValues(
            string inputString, char keySeparator, char valueSeparator)
        {
            if (string.IsNullOrEmpty(inputString) || string.IsNullOrWhiteSpace(inputString))
            {
                return null;
            }
            string[] keyValues = inputString.Split(keySeparator);
            Dictionary<string, string> dict = new Dictionary<string, string>();
            for (int i = 0; i < keyValues.Length; i++)
            {
                if (!string.IsNullOrEmpty(keyValues[i]) && !string.IsNullOrWhiteSpace(keyValues[i]))
                {
                    string[] t = keyValues[i].Split(valueSeparator);
                    dict.Add(t[0], t[1]);
                }
            }
            return dict;
        }

        private static bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            string[] pictureTypes = new string[] { ".png", ".jpg", ".jpeg", ".bmp" };
            return pictureTypes.Contains(ext);
        }
    }
}
