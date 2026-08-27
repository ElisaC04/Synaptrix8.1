using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using Windows.Data.Json;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using Windows.Storage.Streams;


namespace Synaptrix8._1
{
    public sealed partial class ChatPage : Page
    {
        private ChatItem _currentChat;
        private string _paginationToken = "";
        private bool _isLoadingOlder = false;
        public ObservableCollection<MessageItem> Messages { get; set; }
        private MessageItem _replyingToMessage = null;

        public ChatPage()
        {
            this.InitializeComponent();
            Messages = new ObservableCollection<MessageItem>();
            MessagesListView.ItemsSource = Messages;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var chat = e.Parameter as ChatItem;
            if (chat != null)
            {
                _currentChat = chat;
                RoomNameTitle.Text = _currentChat.DisplayName.ToUpper();

                MainPage.MessageReceived += OnGlobalMessageReceived;
                MainPage.TypingChanged += OnGlobalTypingChanged;
                App.FilePicked -= OnFilePicked;
                MainPage.ReadReceiptReceived += OnGlobalReadReceiptReceived;

                await LoadMessagesAsync();
            }
            App.FilePicked += OnFilePicked;

        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            MainPage.MessageReceived -= OnGlobalMessageReceived;
            App.FilePicked -= OnFilePicked;
            MainPage.ReadReceiptReceived -= OnGlobalReadReceiptReceived;
        }

        private async void OnGlobalReadReceiptReceived(object sender, ReadReceiptEventArgs e)
        {
            if (_currentChat != null && e.RoomId == _currentChat.RoomId)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var msg in Messages)
                    {
                        msg.RemoveReader(e.ReaderName);
                    }

                    for (int i = Messages.Count - 1; i >= 0; i--)
                    {
                        if (Messages[i].EventId == e.EventId)
                        {
                            Messages[i].AddReader(e.ReaderName);
                            break;
                        }
                    }
                });
            }
        }

        private async void OnFilePicked(object sender, Windows.Storage.StorageFile file)
        {
            if (file == null) return;

            AttachMenuButton.IsEnabled = false;
            AttachMenuButton.Content = "⏳";

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            creds[0].RetrievePassword();
            string accessToken = creds[0].Password;

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();

            try
            {
                using (var client = new Windows.Web.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", accessToken);

                    var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                    var streamContent = new Windows.Web.Http.HttpStreamContent(stream);
                    streamContent.Headers.ContentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue(file.ContentType);

                    Uri uploadUri = new Uri($"{serverUrl}/_matrix/media/v3/upload?filename={file.Name}");
                    var uploadResponse = await client.PostAsync(uploadUri, streamContent);

                    if (uploadResponse.IsSuccessStatusCode)
                    {
                        string responseString = await uploadResponse.Content.ReadAsStringAsync();
                        Windows.Data.Json.JsonObject jsonResponse;

                        if (Windows.Data.Json.JsonObject.TryParse(responseString, out jsonResponse))
                        {
                            string mxcUri = jsonResponse.GetNamedString("content_uri", "");

                            string mime = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;

                            string msgType = mime.StartsWith("image") ? "m.image" : "m.file";
                            Windows.Data.Json.JsonObject infoObject = new Windows.Data.Json.JsonObject
                            {
                                { "mimetype", Windows.Data.Json.JsonValue.CreateStringValue(file.ContentType) }
                            };

                            Windows.Data.Json.JsonObject payload = new Windows.Data.Json.JsonObject
                            {
                                { "msgtype", Windows.Data.Json.JsonValue.CreateStringValue("m.image") },
                                { "body", Windows.Data.Json.JsonValue.CreateStringValue(file.Name) },
                                { "url", Windows.Data.Json.JsonValue.CreateStringValue(mxcUri) },
                                { "info", infoObject } // Give the bridge what it wants!
                            };

                            string txnId = Guid.NewGuid().ToString();
                            string encodedRoomId = Uri.EscapeDataString(_currentChat.RoomId);
                            Uri sendUri = new Uri($"{serverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/send/m.room.message/{txnId}");

                            var sendContent = new Windows.Web.Http.HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                            await client.PutAsync(sendUri, sendContent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload failed: {ex.Message}");
            }

            AttachMenuButton.IsEnabled = true;
            AttachMenuButton.Content = "📎";
        }

        private async void OnGlobalTypingChanged(object sender, TypingEventArgs e)
        {
            if (_currentChat != null && e.RoomId == _currentChat.RoomId)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    if (string.IsNullOrEmpty(e.TypersText))
                    {
                        TypingIndicatorBorder.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        TypingIndicatorText.Text = e.TypersText;
                        TypingIndicatorBorder.Visibility = Visibility.Visible;
                    }
                });
            }
        }

        private void ExtractReply(JsonObject content, ref string body, out string replySender, out string replyBody)
        {
            replySender = "";
            replyBody = "";

            if (!content.ContainsKey("m.relates_to")) return;

            var relatesTo = content.GetNamedObject("m.relates_to");
            if (!relatesTo.ContainsKey("m.in_reply_to")) return;

            var inReplyTo = relatesTo.GetNamedObject("m.in_reply_to");
            string inReplyToEventId = inReplyTo.GetNamedString("event_id", "");

            if (!string.IsNullOrEmpty(inReplyToEventId))
            {
                foreach (var msg in Messages)
                {
                    if (msg.EventId == inReplyToEventId)
                    {
                        replySender = msg.Sender;
                        replyBody = !string.IsNullOrEmpty(msg.Body) ? msg.Body : "📷 Image";
                        break;
                    }
                }
            }

            string trimmed = body.TrimStart();
            if (trimmed.StartsWith(">"))
            {
                int splitIndex = body.IndexOf("\n\n");
                if (splitIndex == -1) splitIndex = body.IndexOf("\r\n\r\n");

                if (splitIndex != -1)
                {
                    string fallbackQuote = body.Substring(0, splitIndex);
                    body = body.Substring(splitIndex).Trim();

                    if (string.IsNullOrEmpty(replyBody))
                    {
                        string cleanQuote = fallbackQuote.Replace("\r", "").Replace("> ", "").Replace(">", "").Trim();

                        if (cleanQuote.StartsWith("<") && cleanQuote.Contains(">"))
                        {
                            int end = cleanQuote.IndexOf('>');
                            string user = cleanQuote.Substring(1, end - 1);
                            replySender = user.Split(':')[0].TrimStart('@');
                            if (MainPage.DisplayNameCache.ContainsKey(user.ToLower()))
                            {
                                replySender = MainPage.DisplayNameCache[user.ToLower()];
                            }
                            replyBody = cleanQuote.Substring(end + 1).Trim();
                        }
                        else
                        {
                            replySender = "In reply to";
                            replyBody = cleanQuote;
                        }
                    }
                }
            }
        }

        private async Task LoadMessagesAsync()
        {
            LoadingProgress.Visibility = Visibility.Visible;
            Messages.Clear();

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            var activeCred = creds[0];
            activeCred.RetrievePassword();
            string accessToken = activeCred.Password;
            string currentUserId = activeCred.UserName;

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();

            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken)) return;

            string cachedJson = await LoadRoomCacheSecurelyAsync(_currentChat.RoomId);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                ParseMessages(cachedJson, currentUserId);
            }

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", accessToken);
                string encodedRoomId = Uri.EscapeDataString(_currentChat.RoomId);
                Uri requestUri = new Uri($"{serverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/messages?dir=b&limit=40");

                try
                {
                    var response = await client.GetAsync(requestUri);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonString = await response.Content.ReadAsStringAsync();

                        Messages.Clear();
                        ParseMessages(jsonString, currentUserId);

                        await SaveRoomCacheSecurelyAsync(_currentChat.RoomId, jsonString);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Network error loading messages (you might be offline): {ex.Message}");
                }
            }

            LoadingProgress.Visibility = Visibility.Collapsed;
        }

        private async void ParseMessages(string jsonString, string currentUserId)
        {
            JsonObject root;
            if (!JsonObject.TryParse(jsonString, out root)) return;

            if (root.ContainsKey("end"))
            {
                _paginationToken = root.GetNamedString("end", "");
            }

            var chunkArray = root.GetNamedArray("chunk", new JsonArray());

            for (int i = chunkArray.Count - 1; i >= 0; i--)
            {
                var evt = chunkArray[i].GetObject();
                string msgType = evt.GetNamedString("type", "");

                if (msgType == "m.room.message")
                {
                    string sender = evt.GetNamedString("sender", "Unknown").ToLower();
                    string displaySender = sender.Split(':')[0].TrimStart('@');
                    string eventId = evt.GetNamedString("event_id", "");

                    if (MainPage.DisplayNameCache.ContainsKey(sender))
                    {
                        displaySender = MainPage.DisplayNameCache[sender];
                    }

                    bool isOwn = sender == currentUserId.ToLower();

                    string fbCfg = ConfigManager.FacebookId.ToLower() ?? "";
                    string dsCfg = ConfigManager.DiscordId.ToLower() ?? "";
                    string tgCfg = ConfigManager.TelegramId.ToLower() ?? "";
                    string gmCfg = ConfigManager.GMessagesId?.ToLower() ?? "";

                    if (!string.IsNullOrEmpty(fbCfg) && (sender.Contains("messenger") || sender.Contains("meta") || sender.Contains("facebook")) && sender.Contains(fbCfg))
                    {
                        displaySender = "Me";
                        isOwn = true;
                    }
                    else if (!string.IsNullOrEmpty(dsCfg) && sender.Contains("discord") && sender.Contains(dsCfg))
                    {
                        displaySender = "Me";
                        isOwn = true;
                    }
                    else if (!string.IsNullOrEmpty(tgCfg) && sender.Contains("telegram") && sender.Contains(tgCfg))
                    {
                        displaySender = "Me";
                        isOwn = true;
                    }
                    else if (!string.IsNullOrEmpty(gmCfg) && (sender.Contains("gmessages") || sender.Contains("sms")) && sender.Contains(gmCfg)) // NEW
                    {
                        displaySender = "Me";
                        isOwn = true;
                    }


                    long ts = (long)evt.GetNamedNumber("origin_server_ts", 0);
                    string timeString = FormatMatrixTimestamp(ts);

                    var content = evt.GetNamedObject("content", new JsonObject());
                    string msgTypeContent = content.GetNamedString("msgtype", "m.text");
                    string body = content.GetNamedString("body", "[Unsupported Event]");
                    string replySender = "";
                    string replyBody = "";
                    ExtractReply(content, ref body, out replySender, out replyBody);

                    string imageUrl = "";

                    string rawMediaUrl = "";
                    if (msgTypeContent == "m.image")
                    {
                        string mxcUrl = content.GetNamedString("url", "");
                        if (!string.IsNullOrEmpty(mxcUrl))
                        {
                            body = "";

                            var vault = new Windows.Security.Credentials.PasswordVault();
                            var creds = vault.FindAllByResource("MatrixServer");
                            string activeToken = "";
                            if (creds.Count > 0)
                            {
                                creds[0].RetrievePassword();
                                activeToken = creds[0].Password;
                            }

                            object urlObj;
                            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
                            string srv = urlObj?.ToString();

                            var cachedUri = await MainPage.GetCachedAvatarAsync(mxcUrl, srv, activeToken, ConfigManager.AutoDownloadImages);

                            if (cachedUri != null)
                            {
                                imageUrl = cachedUri.ToString();
                                rawMediaUrl = "";
                            }
                            else
                            {
                                rawMediaUrl = mxcUrl;
                            }
                        }
                    }
                    else if (msgTypeContent == "m.file" || msgTypeContent == "m.video" || msgTypeContent == "m.audio")
                    {
                        rawMediaUrl = content.GetNamedString("url", "");
                        body = $"📁 {body}";
                    }

                    Messages.Add(new MessageItem(eventId, displaySender, body, timeString, isOwn, imageUrl, rawMediaUrl, replySender, replyBody));
                }
            }
            if (chunkArray.Count > 0)
            {
                string newestEventId = chunkArray[0].GetObject().GetNamedString("event_id", "");
                var ignoredReceipt = SendReadReceiptAsync(newestEventId);
            }

            if (Messages.Count > 0)
            {
                MessagesListView.ScrollIntoView(Messages[Messages.Count - 1]);
            }
        }

        private string FormatMatrixTimestamp(long unixTimeMilliseconds)
        {
            if (unixTimeMilliseconds == 0) return "";
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime messageTime = epoch.AddMilliseconds(unixTimeMilliseconds).ToLocalTime();
            return messageTime.ToString("t");
        }

        private async void OnSendClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            string messageText = MessageInputBox.Text.Trim();
            if (string.IsNullOrEmpty(messageText)) return;

            MessageInputBox.Text = "";

            string optimisticReplySender = _replyingToMessage != null ? _replyingToMessage.Sender : "";
            string optimisticReplyBody = _replyingToMessage != null ? (!string.IsNullOrEmpty(_replyingToMessage.Body) ? _replyingToMessage.Body : "📷 Image") : "";

            Messages.Add(new MessageItem("", "Me", messageText, "Sending...", true, "", "", optimisticReplySender, optimisticReplyBody));
            MessagesListView.ScrollIntoView(Messages[Messages.Count - 1]);

            SendButton.IsEnabled = false;

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count > 0)
            {
                var activeCred = creds[0];
                activeCred.RetrievePassword();
                string accessToken = activeCred.Password;

                object urlObj;
                Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
                string serverUrl = urlObj?.ToString();

                if (!string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(accessToken))
                {
                    JsonObject payload = new JsonObject();
                    payload.Add("msgtype", JsonValue.CreateStringValue("m.text"));

                    if (_replyingToMessage != null)
                    {
                        string fallbackBody = $"> <{_replyingToMessage.Sender}> {_replyingToMessage.Body}\n\n{messageText}";
                        payload.Add("body", JsonValue.CreateStringValue(fallbackBody));

                        string formattedBody = $"<mx-reply><blockquote>In reply to <strong>{_replyingToMessage.Sender}</strong><br>{_replyingToMessage.Body}</blockquote></mx-reply>{messageText}";
                        payload.Add("format", JsonValue.CreateStringValue("org.matrix.custom.html"));
                        payload.Add("formatted_body", JsonValue.CreateStringValue(formattedBody));

                        JsonObject inReplyTo = new JsonObject();
                        inReplyTo.Add("event_id", JsonValue.CreateStringValue(_replyingToMessage.EventId));

                        JsonObject relatesTo = new JsonObject();
                        relatesTo.Add("m.in_reply_to", inReplyTo);

                        payload.Add("m.relates_to", relatesTo);

                        _replyingToMessage = null;
                        ReplyPreviewBox.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        payload.Add("body", JsonValue.CreateStringValue(messageText));
                    }

                    string txnId = Guid.NewGuid().ToString();
                    string encodedRoomId = Uri.EscapeDataString(_currentChat.RoomId);
                    Uri requestUri = new Uri($"{serverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/send/m.room.message/{txnId}");

                    using (HttpClient client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", accessToken);
                        HttpStringContent content = new HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                        try
                        {
                            await client.PutAsync(requestUri, content);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to send: {ex.Message}");
                        }
                    }
                }
            }

            SendButton.IsEnabled = true;
            MessageInputBox.Focus(Windows.UI.Xaml.FocusState.Programmatic);
        }

        private void OnMediaMenuClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".png");

            picker.PickSingleFileAndContinue();
        }

        private void OnDocumentMenuClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;

            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;

            picker.FileTypeFilter.Add(".pdf");
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".doc");
            picker.FileTypeFilter.Add(".docx");
            picker.FileTypeFilter.Add(".zip");

            picker.PickSingleFileAndContinue();
        }

        private async void OnGlobalMessageReceived(object sender, NewMessageEventArgs e)
        {
            if (_currentChat != null && e.RoomId == _currentChat.RoomId)
            {
                var vault = new Windows.Security.Credentials.PasswordVault();
                var creds = vault.FindAllByResource("MatrixServer");
                string currentUserId = creds.Count > 0 ? creds[0].UserName : "";

                string rawSender = e.Sender.ToLower();
                string displaySender = rawSender.Split(':')[0].TrimStart('@');

                if (MainPage.DisplayNameCache.ContainsKey(rawSender))
                {
                    displaySender = MainPage.DisplayNameCache[rawSender];
                }

                bool isOwn = rawSender == currentUserId.ToLower();

                string fbCfg = ConfigManager.FacebookId.ToLower() ?? "";
                string dsCfg = ConfigManager.DiscordId.ToLower() ?? "";
                string tgCfg = ConfigManager.TelegramId.ToLower() ?? "";
                string gmCfg = ConfigManager.GMessagesId?.ToLower() ?? "";

                if (!string.IsNullOrEmpty(fbCfg) && (rawSender.Contains("messenger") || rawSender.Contains("meta") || rawSender.Contains("facebook")) && rawSender.Contains(fbCfg))
                {
                    displaySender = "Me";
                    isOwn = true;
                }
                else if (!string.IsNullOrEmpty(dsCfg) && rawSender.Contains("discord") && rawSender.Contains(dsCfg))
                {
                    displaySender = "Me";
                    isOwn = true;
                }
                else if (!string.IsNullOrEmpty(tgCfg) && rawSender.Contains("telegram") && rawSender.Contains(tgCfg))
                {
                    displaySender = "Me";
                    isOwn = true;
                }
                else if (!string.IsNullOrEmpty(gmCfg) && (rawSender.Contains("gmessages") || rawSender.Contains("sms")) && rawSender.Contains(gmCfg)) // NEW
                {
                    displaySender = "Me";
                    isOwn = true;
                }

                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                DateTime messageTime = epoch.AddMilliseconds(e.Timestamp).ToLocalTime();
                string timeString = messageTime.ToString("t");

                string localImageUrl = "";
                string rawMediaUrl = e.MxcUrl;
                string displayBody = e.Body;

                if (!string.IsNullOrEmpty(e.MxcUrl))
                {
                    if (string.IsNullOrEmpty(displayBody))
                    {
                        string activeToken = "";
                        if (creds.Count > 0)
                        {
                            creds[0].RetrievePassword();
                            activeToken = creds[0].Password;
                        }

                        object urlObj;
                        Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
                        string srv = urlObj?.ToString();

                        var cachedUri = await MainPage.GetCachedAvatarAsync(e.MxcUrl, srv, activeToken, ConfigManager.AutoDownloadImages);

                        if (cachedUri != null)
                        {
                            localImageUrl = cachedUri.ToString();
                        }
                        else
                        {
                            rawMediaUrl = e.MxcUrl;
                        }
                    }
                    else
                    {
                        rawMediaUrl = e.MxcUrl;
                    }
                }

                var ignoredReceipt = SendReadReceiptAsync(e.EventId);

                var ignored = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    foreach (var msg in Messages)
                    {
                        if (!string.IsNullOrEmpty(msg.EventId) && msg.EventId == e.EventId) return;
                    }

                    if (isOwn)
                    {
                        for (int i = Messages.Count - 1; i >= 0; i--)
                        {
                            var msg = Messages[i];
                            if (msg.IsOwn && msg.Body == displayBody && (msg.Timestamp == "Sending..." || string.IsNullOrEmpty(msg.EventId)))
                            {
                                msg.EventId = e.EventId;
                                msg.Timestamp = timeString;
                                return;
                            }
                        }
                    }

                    Messages.Add(new MessageItem(e.EventId, displaySender, displayBody, timeString, isOwn, localImageUrl, rawMediaUrl, e.ReplySender, e.ReplyBody));
                    MessagesListView.ScrollIntoView(Messages[Messages.Count - 1]);
                });
            }
        }

        private async Task SendReadReceiptAsync(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            var activeCred = creds[0];
            activeCred.RetrievePassword();
            string accessToken = activeCred.Password;

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();

            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken)) return;

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", accessToken);

                string encodedRoomId = Uri.EscapeDataString(_currentChat.RoomId);
                string encodedEventId = Uri.EscapeDataString(eventId);

                Uri requestUri = new Uri($"{serverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/receipt/m.read/{encodedEventId}");

                HttpStringContent content = new HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                try
                {
                    await client.PostAsync(requestUri, content);
                    System.Diagnostics.Debug.WriteLine($"[MATRIX] Read receipt sent for {eventId}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to send read receipt: {ex.Message}");
                }
            }
        }

        private async void OnDownloadAttachmentClicked(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var msg = btn?.DataContext as MessageItem;

            if (msg == null || string.IsNullOrEmpty(msg.RawMediaUrl)) return;

            btn.IsEnabled = false;
            btn.Content = "⏳ Downloading...";

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            string activeToken = "";
            if (creds.Count > 0)
            {
                creds[0].RetrievePassword();
                activeToken = creds[0].Password;
            }

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string srv = urlObj?.ToString();

            if (string.IsNullOrEmpty(msg.Body))
            {
                var cachedUri = await MainPage.GetCachedAvatarAsync(msg.RawMediaUrl, srv, activeToken);
                string localPath = cachedUri != null ? cachedUri.ToString() : "";

                int index = Messages.IndexOf(msg);
                if (index >= 0)
                {
                    Messages[index] = new MessageItem(msg.EventId, msg.Sender, msg.Body, msg.Timestamp, msg.IsOwn, localPath, "");
                }
            }
            else
            {
                string strippedMxc = msg.RawMediaUrl.Replace("mxc://", "");
                string httpUrl = $"{srv}/_matrix/media/v3/download/{strippedMxc}";

                await Windows.System.Launcher.LaunchUriAsync(new Uri(httpUrl));

                btn.IsEnabled = true;
                btn.Content = "⬇️ Download Media";
            }
        }

        private async Task SaveRoomCacheSecurelyAsync(string roomId, string jsonString)
        {
            try
            {
                var provider = new DataProtectionProvider("LOCAL=user");
                IBuffer plainBuffer = CryptographicBuffer.ConvertStringToBinary(jsonString, BinaryStringEncoding.Utf8);
                IBuffer protectedBuffer = await provider.ProtectAsync(plainBuffer);

                string safeId = roomId.Replace("!", "").Replace(":", "_");
                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync($"room_{safeId}.dat", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBufferAsync(file, protectedBuffer);
            }
            catch { }
        }

        private async Task<string> LoadRoomCacheSecurelyAsync(string roomId)
        {
            try
            {
                string safeId = roomId.Replace("!", "").Replace(":", "_");
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync($"room_{safeId}.dat");
                IBuffer protectedBuffer = await FileIO.ReadBufferAsync(file);

                var provider = new DataProtectionProvider();
                IBuffer plainBuffer = await provider.UnprotectAsync(protectedBuffer);

                return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, plainBuffer);
            }
            catch { return null; }
        }

        private async void OnLoadOlderClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            if (_isLoadingOlder || string.IsNullOrEmpty(_paginationToken)) return;

            _isLoadingOlder = true;
            LoadOlderButton.Content = "Loading...";
            LoadOlderButton.IsEnabled = false;

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            creds[0].RetrievePassword();
            string accessToken = creds[0].Password;
            string currentUserId = creds[0].UserName.ToLower();

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();
            string encodedRoomId = Uri.EscapeDataString(_currentChat.RoomId);

            try
            {
                using (var client = new Windows.Web.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", accessToken);

                    Uri uri = new Uri($"{serverUrl}/_matrix/client/v3/rooms/{encodedRoomId}/messages?dir=b&from={_paginationToken}&limit=30");
                    var response = await client.GetAsync(uri);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonString = await response.Content.ReadAsStringAsync();
                        JsonObject root;

                        if (JsonObject.TryParse(jsonString, out root))
                        {
                            if (root.ContainsKey("end"))
                            {
                                _paginationToken = root.GetNamedString("end", "");
                            }

                            if (root.ContainsKey("chunk"))
                            {
                                var chunk = root.GetNamedArray("chunk");
                                for (int i = 0; i < chunk.Count; i++)
                                {
                                    var evt = chunk[i].GetObject();
                                    string msgType = evt.GetNamedString("type", "");

                                    if (msgType == "m.room.message")
                                    {
                                        string eventId = evt.GetNamedString("event_id", "");
                                        string rawSender = evt.GetNamedString("sender", "Unknown");
                                        string rawSenderLower = rawSender.ToLower();

                                        bool isOwn = rawSenderLower == currentUserId;
                                        string fbCfg = ConfigManager.FacebookId?.ToLower() ?? "";
                                        string dsCfg = ConfigManager.DiscordId?.ToLower() ?? "";
                                        string tgCfg = ConfigManager.TelegramId?.ToLower() ?? "";
                                        string gmCfg = ConfigManager.GMessagesId?.ToLower() ?? "";

                                        if (!string.IsNullOrEmpty(fbCfg) && rawSenderLower.Contains(fbCfg)) isOwn = true;
                                        if (!string.IsNullOrEmpty(dsCfg) && rawSenderLower.Contains(dsCfg)) isOwn = true;
                                        if (!string.IsNullOrEmpty(tgCfg) && rawSenderLower.Contains(tgCfg)) isOwn = true;
                                        if (!string.IsNullOrEmpty(gmCfg) && rawSenderLower.Contains(gmCfg)) isOwn = true;

                                        string displaySender = rawSender.Split(':')[0].TrimStart('@');
                                        if (MainPage.DisplayNameCache.ContainsKey(rawSenderLower))
                                        {
                                            displaySender = MainPage.DisplayNameCache[rawSenderLower];
                                        }
                                        if (isOwn) displaySender = "Me";

                                        string timeString = "Older";
                                        if (evt.ContainsKey("origin_server_ts"))
                                        {
                                            long ts = (long)evt.GetNamedNumber("origin_server_ts", 0);
                                            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                                            timeString = epoch.AddMilliseconds(ts).ToLocalTime().ToString("HH:mm");
                                        }

                                        var content = evt.GetNamedObject("content", new JsonObject());
                                        string msgTypeContent = content.GetNamedString("msgtype", "m.text");
                                        string body = content.GetNamedString("body", "");
                                        string replySender, replyBody;
                                        ExtractReply(content, ref body, out replySender, out replyBody);


                                        string imageUrl = "";
                                        string rawMediaUrl = "";

                                        if (msgTypeContent == "m.image")
                                        {
                                            rawMediaUrl = content.GetNamedString("url", "");
                                            body = "";

                                            string cleanId = rawMediaUrl.Replace("mxc://", "").Replace("/", "_");
                                            string expectedFilename = cleanId + ".png";
                                            try
                                            {
                                                var localFile = await Windows.Storage.ApplicationData.Current.LocalFolder.GetFileAsync(expectedFilename);
                                                imageUrl = localFile.Path;
                                                rawMediaUrl = "";
                                            }
                                            catch
                                            {

                                            }
                                        }
                                        else if (msgTypeContent == "m.file" || msgTypeContent == "m.video" || msgTypeContent == "m.audio")
                                        {
                                            rawMediaUrl = content.GetNamedString("url", "");
                                            body = $"📁 {body}";
                                        }

                                        bool isDuplicate = false;
                                        foreach (var currentMsg in Messages)
                                        {
                                            if (currentMsg.EventId == eventId)
                                            {
                                                isDuplicate = true;
                                                break;
                                            }
                                        }

                                        if (!isDuplicate)
                                        {
                                            Messages.Insert(0, new MessageItem(eventId, displaySender, body, timeString, isOwn, imageUrl, rawMediaUrl, replySender, replyBody));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load older messages: {ex.Message}");
            }

            LoadOlderButton.Content = "Load Older Messages";
            LoadOlderButton.IsEnabled = true;
            _isLoadingOlder = false;
        }

        private void OnMessageHolding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            if (e.HoldingState == Windows.UI.Input.HoldingState.Started)
            {
                var element = sender as Windows.UI.Xaml.FrameworkElement;
                if (element != null)
                {
                    Windows.UI.Xaml.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(element);
                }
            }
        }

        private void OnReplyMenuClicked(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuFlyoutItem;
            var msg = menuItem?.DataContext as MessageItem;

            if (msg != null && !string.IsNullOrEmpty(msg.EventId))
            {
                _replyingToMessage = msg;
                ReplyPreviewSender.Text = $"Replying to {msg.Sender}";
                ReplyPreviewBody.Text = string.IsNullOrEmpty(msg.Body) ? "📷 Image" : msg.Body;

                ReplyPreviewBox.Visibility = Visibility.Visible;
                MessageInputBox.Focus(FocusState.Programmatic);
            }
        }

        private void OnCancelReplyClicked(object sender, RoutedEventArgs e)
        {
            _replyingToMessage = null;
            ReplyPreviewBox.Visibility = Visibility.Collapsed;
        }
    }
}