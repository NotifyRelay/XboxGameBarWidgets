using NotifyRelayGamebar.Models;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Windows.Data.Json;
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
                var notification = await ParseJsonMessage(message);
                if (notification != null)
                {
                    await _viewModel.AddNotification(notification);
                }
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error processing notification message: {0}", message);
            }
        }

        private async Task<NotificationModel> ParseJsonMessage(string jsonString)
        {
            try
            {
                var json = JsonObject.Parse(jsonString);
                
                string appName = json.TryGetValue("appName", out var appNameValue) ? appNameValue.GetString() : string.Empty;
                string title = json.TryGetValue("title", out var titleValue) ? titleValue.GetString() : string.Empty;
                string body = json.TryGetValue("body", out var bodyValue) ? bodyValue.GetString() : string.Empty;
                string iconUrl = json.TryGetValue("iconUrl", out var iconUrlValue) && iconUrlValue.ValueType != JsonValueType.Null ? iconUrlValue.GetString() : string.Empty;

                var notification = new NotificationModel
                {
                    AppName = appName,
                    Title = title,
                    Body = body,
                    ReceivedTime = DateTime.Now,
                    IsMediaNotification = false
                };

                if (!string.IsNullOrEmpty(iconUrl))
                {
                    try
                    {
                        var uri = new Uri(iconUrl);
                        notification.IconUri = uri;
                        notification.IconImage = await LoadImageFromUri(uri);
                    }
                    catch (Exception ex)
                    {
                        Timber.Log(LoggerLevel.Warn, ex, "Error loading icon from URL: {0}", iconUrl);
                    }
                }

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
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(await RandomAccessStreamReference.CreateFromUri(uri).OpenReadAsync());
                return bitmap;
            }
            catch (Exception ex)
            {
                Timber.Log(LoggerLevel.Error, ex, "Error loading image from URI: {0}", uri.AbsoluteUri);
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