using NotifyRelayGamebar.Models;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;
using TimberLog;

namespace NotifyRelayGamebar
{
    public class NotificationService
    {
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private StreamReader _streamReader;
        private bool _isRunning;
        private readonly NotificationViewModel _viewModel;
        private const int LocalPort = 45678;
        private const string LocalHost = "127.0.0.1";

        public event EventHandler<string> ErrorOccurred;

        // Add reference to MediaPlaybackManager
        public MediaPlaybackManager MediaManager { get; set; }

        public NotificationService(NotificationViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public async Task StartAsync()
        {
            _isRunning = true;
            await Task.Run(async () =>
            {
                while (_isRunning)
                {
                    try
                    {
                        await ConnectAndReceiveAsync();
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Error, ex, "NotificationService error");
                        ErrorOccurred?.Invoke(this, ex.Message);
                        await Task.Delay(2000); // 等待2秒后重连
                    }
                }
            });
        }

        public void Stop()
        {
            _isRunning = false;
            Disconnect();
        }

        private async Task ConnectAndReceiveAsync()
        {
            using (_tcpClient = new TcpClient())
            {
                await _tcpClient.ConnectAsync(LocalHost, LocalPort);
                _networkStream = _tcpClient.GetStream();
                _streamReader = new StreamReader(_networkStream, Encoding.UTF8);

                string line;
                while (_isRunning && (line = await _streamReader.ReadLineAsync()) != null)
                {
                    await ProcessMessageAsync(line);
                }
            }
        }

        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                // 只打印消息的前100个字符，避免打印太长的data URL
                string truncatedMessage = message.Length > 100 ? message.Substring(0, 100) + "..." : message;
                Timber.Log(LoggerLevel.Info, "Received message: {0}", truncatedMessage);

                // Check for media update
                try
                {
                    var jsonObject = JsonObject.Parse(message);
                    if (jsonObject.ContainsKey("type") && jsonObject["type"].GetString() == "media_update")
                    {
                        if (MediaManager != null)
                        {
                            var session = new RemoteMediaSession
                            {
                                DeviceId = jsonObject.ContainsKey("deviceId") ? jsonObject["deviceId"].GetString() : "",
                                DeviceName = jsonObject.ContainsKey("deviceName") ? jsonObject["deviceName"].GetString() : "",
                                Title = jsonObject.ContainsKey("title") ? jsonObject["title"].GetString() : "",
                                Artist = jsonObject.ContainsKey("artist") ? jsonObject["artist"].GetString() : "",
                                CoverUrl = jsonObject.ContainsKey("coverUrl") ? jsonObject["coverUrl"].GetString() : "",
                                IsPlaying = jsonObject.ContainsKey("isPlaying") && jsonObject["isPlaying"].GetBoolean()
                            };

                            // Handle empty session (removal)
                            if (string.IsNullOrEmpty(session.Title) && string.IsNullOrEmpty(session.Artist))
                            {
                                MediaManager.UpdateRemoteMediaSession(null);
                            }
                            else
                            {
                                MediaManager.UpdateRemoteMediaSession(session);
                            }
                        }
                        return;
                    }
                }
                catch 
                {
                    // Ignore parsing error here, continue to try parsing as notification
                }

                var notification = ParseJsonMessage(message);
                if (notification != null)
                {
                    Timber.Log(LoggerLevel.Info, "Parsed notification: AppName={0}, Title={1}, IconImage={2}", 
                        notification.AppName, notification.Title, notification.IconImage != null ? "Loaded" : "Null");
                    Timber.Log(LoggerLevel.Info, "Calling AddNotification");
                    await _viewModel.AddNotification(notification);
                    Timber.Log(LoggerLevel.Info, "AddNotification completed");
                }
            }
            catch (Exception ex)
            {
                // 异常时也只打印消息的前100个字符
                string truncatedMessage = message.Length > 100 ? message.Substring(0, 100) + "..." : message;
                Timber.Log(LoggerLevel.Error, ex, "Error processing notification message: {0}", truncatedMessage);
            }
        }

        public async Task SendMediaControlCommandAsync(string deviceId, string command)
        {
            if (_tcpClient == null || !_tcpClient.Connected || _networkStream == null) return;
            
            try
            {
                var payload = new JsonObject();
                payload.Add("action", JsonValue.CreateStringValue("media_control"));
                payload.Add("deviceId", JsonValue.CreateStringValue(deviceId));
                payload.Add("command", JsonValue.CreateStringValue(command));
                
                string json = payload.ToString() + "\n";
                byte[] data = Encoding.UTF8.GetBytes(json);
                
                await _networkStream.WriteAsync(data, 0, data.Length);
                await _networkStream.FlushAsync();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"Error sending command: {ex.Message}");
            }
        }

        private NotificationModel ParseJsonMessage(string jsonString)
        {
            try
            {
                var json = JsonObject.Parse(jsonString);
                
                string appName = json.TryGetValue("appName", out var appNameValue) ? appNameValue.GetString() : string.Empty;
                string title = json.TryGetValue("title", out var titleValue) ? titleValue.GetString() : string.Empty;
                string body = json.TryGetValue("body", out var bodyValue) ? bodyValue.GetString() : string.Empty;
                string iconUrl = json.TryGetValue("iconUrl", out var iconUrlValue) && iconUrlValue.ValueType != JsonValueType.Null ? iconUrlValue.GetString() : string.Empty;

                // 创建NotificationModel对象
                var notification = new NotificationModel
                {
                    AppName = appName,
                    Title = title,
                    Body = body,
                    ReceivedTime = DateTime.Now,
                    IsMediaNotification = false
                };

                // 不在这里加载图标，而是将图标URL传递给ViewModel，让它在UI线程上加载
                notification.IconUrl = iconUrl;

                return notification;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error parsing JSON message: {0}", jsonString);
                return null;
            }
        }

        private async Task<BitmapImage> LoadImageFromUri(Uri uri)
        {
            try
            {
                Timber.Log(LoggerLevel.Info, "Loading image from URI: {0}", uri.AbsoluteUri);
                
                // 使用TaskCompletionSource来处理异步操作的结果
                var tcs = new TaskCompletionSource<BitmapImage>();
                
                await Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(await RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync());
                        Timber.Log(LoggerLevel.Info, "Successfully loaded BitmapImage from URI on UI thread");
                        tcs.SetResult(bitmap);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error loading image from URI: {0}", uri.AbsoluteUri);
                return null;
            }
        }

        private async Task<BitmapImage> LoadImageFromBase64(string base64Url)
        {
            try
            {
                Timber.Log(LoggerLevel.Info, "Processing Base64 URL: {0}", base64Url.Substring(0, Math.Min(base64Url.Length, 50)) + "...");
                
                // 从data: URL中提取Base64数据
                var commaIndex = base64Url.IndexOf(',');
                if (commaIndex == -1)
                {
                    Timber.Log(LoggerLevel.Warn, "Base64 URL does not contain comma separator");
                    return null;
                }

                var base64Data = base64Url.Substring(commaIndex + 1);
                Timber.Log(LoggerLevel.Info, "Extracted Base64 data length: {0}", base64Data.Length);
                
                // 将Base64字符串转换为字节数组
                var bytes = Convert.FromBase64String(base64Data);
                Timber.Log(LoggerLevel.Info, "Decoded bytes length: {0}", bytes.Length);
                
                // 使用TaskCompletionSource来处理异步操作的结果
                var tcs = new TaskCompletionSource<BitmapImage>();
                
                await Windows.ApplicationModel.Core.CoreApplication.MainView.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    try
                    {
                        // 创建IBuffer
                        var buffer = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(bytes);
                        Timber.Log(LoggerLevel.Info, "Created IBuffer with length: {0}", buffer.Length);
                        
                        // 将字节数组转换为InMemoryRandomAccessStream - 所有流操作都在UI线程上执行
                        using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                        {
                            // 将IBuffer写入内存流
                            await stream.WriteAsync(buffer);
                            await stream.FlushAsync();
                            stream.Seek(0);
                            Timber.Log(LoggerLevel.Info, "Wrote buffer to stream, position: {0}", stream.Position);
                            
                            // 在UI线程上创建BitmapImage并设置源
                            var bitmap = new BitmapImage();
                            await bitmap.SetSourceAsync(stream);
                            Timber.Log(LoggerLevel.Info, "Successfully loaded BitmapImage on UI thread");
                            tcs.SetResult(bitmap);
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error loading image from Base64: {0}", base64Url);
                return null;
            }
        }

        private void Disconnect()
        {
            try
            {
                _streamReader?.Dispose();
                _networkStream?.Dispose();
                _tcpClient?.Dispose();
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error disconnecting TCP client");
            }
        }
    }
}