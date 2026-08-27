using System;
using System.Collections.ObjectModel;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using Windows.Data.Json;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace Synaptrix8._1
{
    public sealed partial class MainPage : Page
    {
        // Observables to hold our chat lists
        public ObservableCollection<ChatItem> RecentChats { get; set; }
        public ObservableCollection<ChatItem> MessengerChats { get; set; }
        public ObservableCollection<ChatItem> DiscordChats { get; set; }
        public ObservableCollection<ChatItem> TelegramChats { get; set; }
        public ObservableCollection<ChatItem> GMessagesChats { get; set; }
        public ObservableCollection<InviteItem> PendingInvites { get; set; } = new ObservableCollection<InviteItem>();
        public static event EventHandler<NewMessageEventArgs> MessageReceived;
        public static event EventHandler<TypingEventArgs> TypingChanged;
        public static event EventHandler<ReadReceiptEventArgs> ReadReceiptReceived;
        // This will hold a map of "@meta_123... : Elisa's Friend" across the whole app!
        public static System.Collections.Generic.Dictionary<string, string> DisplayNameCache = new System.Collections.Generic.Dictionary<string, string>();


        private string _nextBatchToken = null;
        private bool _isSyncLoopRunning = false;
        private bool _isInitialLoad = true;
        private bool _isSyncing = false;

        public MainPage()
        {
            this.InitializeComponent();

            // Explicitly set the items source without touching the global DataContext!
            InvitesListView.ItemsSource = PendingInvites;

            this.NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Required;

            // Initialize the collections
            RecentChats = new ObservableCollection<ChatItem>();
            MessengerChats = new ObservableCollection<ChatItem>();
            DiscordChats = new ObservableCollection<ChatItem>();
            TelegramChats = new ObservableCollection<ChatItem>();
            GMessagesChats = new ObservableCollection<ChatItem>();

            // Bind the ListViews to the collections
            RecentListView.ItemsSource = RecentChats;
            MessengerListView.ItemsSource = MessengerChats;
            DiscordListView.ItemsSource = DiscordChats;
            TelegramListView.ItemsSource = TelegramChats;
            GMessagesListView.ItemsSource = GMessagesChats;

            this.Loaded += MainPage_Loaded;
        }

        private bool _isInitialized = false;

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {

            // Wipe the Live Tile clean because we are looking at the app!
            Windows.UI.Notifications.ToastNotificationManager.History.Clear();
            Windows.UI.Notifications.TileUpdateManager.CreateTileUpdaterForApplication().Clear();
            RegisterBackgroundTask();

            if (!_isInitialized)
            {
                _isInitialized = true;

                // 1. Instantly load the offline cache if we have one!
                string cachedJson = await LoadCacheSecurelyAsync();
                if (!string.IsNullOrEmpty(cachedJson))
                {
                    System.Diagnostics.Debug.WriteLine("Loading from secure offline cache...");

                    // We need a dummy serverUrl and token just to pass to the parser, 
                    // but we won't actually make network calls for existing images.
                    await ParseAndDisplayRoomsAsync(cachedJson, "", "");
                }

                // 2. Check settings: Do we auto-sync now, or wait for the user to push the button?
                if (ConfigManager.AutoSyncOnLaunch)
                {
                    var ignoredTask = StartSyncLoopAsync();
                }
                else if (!string.IsNullOrEmpty(cachedJson))
                {
                    // Even if AutoSync is off, we still want live messages!
                    // We extract the next_batch token from the cache so the long-poll knows where to start.
                    JsonObject root;
                    if (JsonObject.TryParse(cachedJson, out root))
                    {
                        _nextBatchToken = root.GetNamedString("next_batch", "");

                        // We set this to FALSE because we already loaded the rooms, we only want the live stream!
                        _isInitialLoad = false;
                        var ignoredTask = StartSyncLoopAsync();
                    }
                }
            }
        }

        private async Task PerformInitialSyncAsync()
        {
            // Prevent two syncs from running at the exact same time!
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
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

                    long cacheBuster = DateTime.UtcNow.Ticks;

                    string url = _isInitialLoad ?
                        $"{serverUrl}/_matrix/client/v3/sync?timeout=0&cb={cacheBuster}" :
                        $"{serverUrl}/_matrix/client/v3/sync?since={_nextBatchToken}&timeout=30000&cb={cacheBuster}";

                    HttpResponseMessage response = await client.GetAsync(new Uri(url));
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonString = await response.Content.ReadAsStringAsync();

                        JsonObject root;
                        if (JsonObject.TryParse(jsonString, out root))
                        {
                            _nextBatchToken = root.GetNamedString("next_batch", _nextBatchToken ?? "");
                            if (!string.IsNullOrEmpty(_nextBatchToken))
                            {
                                Windows.Storage.ApplicationData.Current.LocalSettings.Values["BackgroundSyncToken"] = _nextBatchToken;
                            }
                        }

                        await ParseAndDisplayRoomsAsync(jsonString, serverUrl, accessToken);

                        // THE FIX: ONLY save the cache if it's the massive initial sync payload!
                        if (_isInitialLoad)
                        {
                            await SaveCacheSecurelyAsync(jsonString);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync network timeout/error: {ex.Message}");
                await Task.Delay(5000);
            }
            finally
            {
                // Always unlock the sync when done!
                _isSyncing = false;
            }
        }

        private async Task StartSyncLoopAsync()
        {
            if (_isSyncLoopRunning) return;
            _isSyncLoopRunning = true;

            System.Diagnostics.Debug.WriteLine("Starting Matrix long-polling loop...");

            while (_isSyncLoopRunning)
            {
                await PerformInitialSyncAsync();

                // After the very first successful pass finishes, turn off the initial load flag
                if (_isInitialLoad && !string.IsNullOrEmpty(_nextBatchToken))
                {
                    _isInitialLoad = false;
                }
            }
        }

        private string CleanReplyFallback(string body)
        {
            if (string.IsNullOrEmpty(body) || !body.StartsWith(">")) return body;

            int splitIndex = body.IndexOf("\n\n");
            if (splitIndex == -1) splitIndex = body.IndexOf("\r\n\r\n");

            if (splitIndex != -1)
            {
                return body.Substring(splitIndex).Trim();
            }

            return body;
        }

        public static async Task<Uri> GetCachedAvatarAsync(string mxcUri, string serverUrl, string accessToken, bool downloadIfMissing = true)
        {
            if (string.IsNullOrEmpty(mxcUri) || !mxcUri.StartsWith("mxc://")) return null;

            string rawMxc = mxcUri.Replace("mxc://", "");
            // Replace slashes so it is a valid Windows file name
            string fileName = rawMxc.Replace("/", "_") + ".png";

            Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            try
            {
                // Check if we already cached it!
                Windows.Storage.StorageFile existingFile = await localFolder.GetFileAsync(fileName);

                return new Uri($"ms-appdata:///local/{fileName}");
            }
            catch
            {
                // File doesn't exist locally, we need to download it securely
                if (!downloadIfMissing) return null;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Attach the required auth header for the media server!
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", accessToken);
                    Uri downloadUri = new Uri($"{serverUrl}/_matrix/client/v1/media/download/{rawMxc}");

                    var response = await client.GetAsync(downloadUri);

                    if (response.IsSuccessStatusCode)
                    {
                        var buffer = await response.Content.ReadAsBufferAsync();
                        Windows.Storage.StorageFile file = await localFolder.CreateFileAsync(fileName, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                        await Windows.Storage.FileIO.WriteBufferAsync(file, buffer);

                        return new Uri($"ms-appdata:///local/{fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to download avatar: {ex.Message}");
            }

            return null;
        }

        private async Task ParseAndDisplayRoomsAsync(string jsonString, string serverUrl, string accessToken)
        {
            JsonObject root;
            if (!JsonObject.TryParse(jsonString, out root)) return;

            if (!root.ContainsKey("rooms")) return;
            var rooms = root.GetNamedObject("rooms");
            if (!rooms.ContainsKey("join")) return;
            var joinedRooms = rooms.GetNamedObject("join");

            // --- NEW: Grab the current user ID from the Vault safely ---
            string currentUserId = "";
            try
            {
                var vault = new Windows.Security.Credentials.PasswordVault();
                var credsVault = vault.FindAllByResource("MatrixServer");
                if (credsVault.Count > 0) currentUserId = credsVault[0].UserName.ToLower();
            }
            catch { }

            // --- NEW: Parse Pending Invites! ---
            try
            {
                if (_isInitialLoad)
                {
                    // No Dispatcher needed, we are already on the UI thread!
                    PendingInvites.Clear();
                }

                if (rooms.ContainsKey("invite"))
                {
                    var invitedRooms = rooms.GetNamedObject("invite");
                    foreach (var inviteItem in invitedRooms)
                    {
                        string inviteRoomId = inviteItem.Key;
                        string inviteRoomName = "Unknown Room";
                        string inviterId = "Someone";

                        var inviteObj = inviteItem.Value.GetObject();

                        if (inviteObj.ContainsKey("invite_state"))
                        {
                            var inviteState = inviteObj.GetNamedObject("invite_state");

                            if (inviteState.ContainsKey("events"))
                            {
                                var inviteEvents = inviteState.GetNamedArray("events");
                                foreach (var evtObj in inviteEvents)
                                {
                                    var evt = evtObj.GetObject();
                                    string type = evt.GetNamedString("type", "");

                                    if (type == "m.room.name" && evt.ContainsKey("content"))
                                    {
                                        inviteRoomName = evt.GetNamedObject("content").GetNamedString("name", inviteRoomName);
                                    }
                                    else if (type == "m.room.member" && evt.ContainsKey("state_key"))
                                    {
                                        string stateKey = evt.GetNamedString("state_key", "");
                                        if (stateKey.ToLower() == currentUserId)
                                        {
                                            inviterId = evt.GetNamedString("sender", inviterId);
                                        }
                                    }
                                }
                            }
                        }

                        bool inviteExists = false;
                        foreach (var existing in PendingInvites)
                        {
                            if (existing.RoomId == inviteRoomId) inviteExists = true;
                        }

                        if (!inviteExists)
                        {
                            PendingInvites.Add(new InviteItem(inviteRoomId, inviteRoomName, $"Invited by {inviterId}"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CRITICAL PARSE ERROR in Invites: {ex.Message}");
            }

            // --- NEW: Safely clear the UI only if we are doing a master sync! ---
            if (_isInitialLoad)
            {
                RecentChats.Clear();
                MessengerChats.Clear();
                DiscordChats.Clear();
                TelegramChats.Clear();
                GMessagesChats.Clear();
            }

            foreach (var roomItem in joinedRooms)
            {
                string roomId = roomItem.Key;
                JsonObject roomData = roomItem.Value.GetObject();

                ChatItem existingChat = null;
                foreach (var chat in RecentChats)
                {
                    if (chat.RoomId == roomId)
                    {
                        existingChat = chat;
                        break;
                    }
                }

                // 1. PRESERVE EXISTING DATA: Don't let Matrix delta-syncs wipe our names!
                string roomName = existingChat != null ? existingChat.DisplayName : "Unknown Room";
                string lastMessage = existingChat != null ? existingChat.LastMessage : "";
                BridgeType detectedNetwork = existingChat != null ? existingChat.Network : BridgeType.Matrix;
                string exactRoomAvatar = "";
                string fallbackMemberAvatar = "";

                bool explicitNameFoundInDelta = false;
                bool isSpaceRoom = false; // <--- THE GHOST KILLER
                int humanMemberCount = 0;
                string firstOtherMemberName = "";
                string firstOtherMemberAvatar = "";

                // 2. Parse State Events
                if (roomData.ContainsKey("state"))
                {
                    var stateEvents = roomData.GetNamedObject("state").GetNamedArray("events", new JsonArray());
                    foreach (var stateEvent in stateEvents)
                    {
                        var evt = stateEvent.GetObject();
                        string type = evt.GetNamedString("type", "");

                        // MATRIX SPACES FIX: If this is an invisible folder, flag it for destruction!
                        if (type == "m.room.create")
                        {
                            var content = evt.GetNamedObject("content", new JsonObject());
                            if (content.GetNamedString("type", "") == "m.space")
                            {
                                isSpaceRoom = true;
                            }
                        }
                        else if (type == "m.room.name")
                        {
                            string n = evt.GetNamedObject("content", new JsonObject()).GetNamedString("name", "");
                            if (!string.IsNullOrEmpty(n))
                            {
                                roomName = n;
                                explicitNameFoundInDelta = true;
                            }
                        }
                        else if (type == "m.room.avatar")
                        {
                            exactRoomAvatar = evt.GetNamedObject("content", new JsonObject()).GetNamedString("url", "");
                        }
                        else if (type == "m.room.member")
                        {
                            string stateKey = evt.GetNamedString("state_key", "").ToLower();
                            var content = evt.GetNamedObject("content", new JsonObject());
                            string membership = content.GetNamedString("membership", "");

                            if (membership == "join" || membership == "invite")
                            {
                                string displayName = content.GetNamedString("displayname", "");
                                if (string.IsNullOrEmpty(displayName)) displayName = stateKey.Split(':')[0].TrimStart('@');

                                DisplayNameCache[stateKey] = displayName;

                                if (stateKey.Contains("facebook") || stateKey.Contains("messenger") || stateKey.Contains("meta"))
                                    detectedNetwork = BridgeType.Messenger;
                                else if (stateKey.Contains("discord"))
                                    detectedNetwork = BridgeType.Discord;
                                else if (stateKey.Contains("telegram"))
                                    detectedNetwork = BridgeType.Telegram;
                                else if (stateKey.Contains("gmessages") || stateKey.Contains("sms") || stateKey.Contains("google"))
                                    detectedNetwork = BridgeType.GMessages;

                                string fbCfg = ConfigManager.FacebookId?.ToLower() ?? "";
                                bool isSelf = (stateKey == currentUserId) || (!string.IsNullOrEmpty(fbCfg) && stateKey.Contains(fbCfg));

                                if (!isSelf && !stateKey.Contains("bot"))
                                {
                                    humanMemberCount++;
                                    if (string.IsNullOrEmpty(firstOtherMemberName))
                                    {
                                        firstOtherMemberName = displayName;
                                        firstOtherMemberAvatar = content.GetNamedString("avatar_url", "");
                                    }
                                }
                            }
                        }
                    }
                }

                // DESTROY SPACES: If it's a bridge grouping folder, skip processing it entirely!
                if (isSpaceRoom) continue;

                // 3. THE FIX: Allow renaming if it's brand new OR if it was previously stuck as a placeholder!
                bool isStuckPlaceholder = existingChat != null && (existingChat.DisplayName == "New Chat" || existingChat.DisplayName == "Unknown Room");

                if ((existingChat == null || isStuckPlaceholder) && !explicitNameFoundInDelta)
                {
                    if (humanMemberCount == 1 && !string.IsNullOrEmpty(firstOtherMemberName))
                    {
                        roomName = firstOtherMemberName; // 1-on-1 DM
                        fallbackMemberAvatar = firstOtherMemberAvatar;
                    }
                    else if (humanMemberCount > 1)
                    {
                        roomName = $"{firstOtherMemberName} + {humanMemberCount - 1} others"; // Group chat
                    }
                    else
                    {
                        roomName = "New Chat"; // Empty room, but will be allowed to update later!
                    }
                }

                string finalAvatarMxc = !string.IsNullOrEmpty(exactRoomAvatar) ? exactRoomAvatar : fallbackMemberAvatar;

                string formattedTime = existingChat != null ? existingChat.FormattedTimestamp : "Just now";

                bool hasNewTimelineMessage = false;
                string newMsgBody = "";
                string newMsgSender = "";
                string rawNewMsgSender = "";
                long newMsgTs = 0;
                string newMsgEventId = "";
                string newMsgMxcUrl = "";


                string liveReplySender = "";
                string liveReplyBody = "";
                // 2. Extract Timeline Events (Optimized Reverse Scan)
                if (roomData.ContainsKey("timeline"))
                {
                    var timelineEvents = roomData.GetNamedObject("timeline").GetNamedArray("events", new JsonArray());

                    // Loop backward to find the most recent text or media message, ignoring read receipts!
                    for (int i = (int)timelineEvents.Count - 1; i >= 0; i--)
                    {
                        var evt = timelineEvents[i].GetObject();
                        string msgType = evt.GetNamedString("type", "");

                        if (msgType == "m.room.message")
                        {
                            newMsgEventId = evt.GetNamedString("event_id", "");

                            if (evt.ContainsKey("origin_server_ts"))
                            {
                                long originServerTs = (long)evt.GetNamedNumber("origin_server_ts", 0);
                                formattedTime = FormatMatrixTimestamp(originServerTs);
                                newMsgTs = originServerTs;
                            }

                            var content = evt.GetNamedObject("content", new JsonObject());
                            string msgTypeContent = content.GetNamedString("msgtype", "m.text");
                            lastMessage = content.GetNamedString("body", "Sent an attachment");

                            // Clean quote fallback so the Recent Chats list doesn't show "> User: ..."
                            lastMessage = CleanReplyFallback(lastMessage);

                            // Extract reply info for live events

                            if (content.ContainsKey("m.relates_to"))
                            {
                                var relatesTo = content.GetNamedObject("m.relates_to");
                                if (relatesTo.ContainsKey("m.in_reply_to"))
                                {
                                    string rawBody = content.GetNamedString("body", "");
                                    int split = rawBody.IndexOf("\n\n");
                                    if (split == -1) split = rawBody.IndexOf("\r\n\r\n");

                                    if (split != -1 && rawBody.StartsWith(">"))
                                    {
                                        string quote = rawBody.Substring(0, split).Replace(">", "").Trim();
                                        liveReplySender = "In reply to";
                                        liveReplyBody = quote;
                                    }
                                }
                            }

                            if (msgTypeContent == "m.image")
                            {
                                newMsgMxcUrl = content.GetNamedString("url", "");
                                lastMessage = "📷 Image";
                            }
                            else if (msgTypeContent == "m.file" || msgTypeContent == "m.video" || msgTypeContent == "m.audio")
                            {
                                newMsgMxcUrl = content.GetNamedString("url", "");
                                lastMessage = $"📁 {lastMessage}";
                            }

                            hasNewTimelineMessage = true;
                            newMsgBody = lastMessage;

                            string rawSender = evt.GetNamedString("sender", "Unknown");
                            rawNewMsgSender = rawSender;
                            string cleanSender = rawSender.ToLower().Split(':')[0].TrimStart('@');

                            if (DisplayNameCache.ContainsKey(rawSender.ToLower()))
                            {
                                cleanSender = DisplayNameCache[rawSender.ToLower()];
                            }
                            newMsgSender = cleanSender;

                            break; // Found the newest actual message, stop scanning backward!
                        }
                    }
                }

                // --- NEW: Extract Ephemeral Events (Typing Indicators) ---
                try
                {
                    if (roomData.ContainsKey("ephemeral"))
                    {
                        var ephemeralEvents = roomData.GetNamedObject("ephemeral").GetNamedArray("events", new JsonArray());
                        foreach (var evtObj in ephemeralEvents)
                        {
                            var evt = evtObj.GetObject();
                            string type = evt.GetNamedString("type", "");

                            if (type == "m.typing")
                            {
                                var content = evt.GetNamedObject("content", new JsonObject());

                                // Safely check if the user_ids array actually exists before looping
                                if (content.ContainsKey("user_ids"))
                                {
                                    var userIds = content.GetNamedArray("user_ids");
                                    System.Collections.Generic.List<string> typers = new System.Collections.Generic.List<string>();

                                    foreach (var userIdObj in userIds)
                                    {
                                        string typingUser = userIdObj.GetString().ToLower();

                                        if (typingUser != currentUserId)
                                        {
                                            string displayTyper = typingUser.Split(':')[0].TrimStart('@');
                                            if (DisplayNameCache.ContainsKey(typingUser))
                                            {
                                                displayTyper = DisplayNameCache[typingUser];
                                            }
                                            typers.Add(displayTyper);
                                        }
                                    }

                                    string typingText = "";
                                    if (typers.Count == 1) typingText = $"{typers[0]} is typing...";
                                    else if (typers.Count > 1) typingText = "Several people are typing...";

                                    TypingChanged?.Invoke(this, new TypingEventArgs(roomId, typingText));
                                }
                            }
                            else if (type == "m.receipt")
                            {
                                var content = evt.GetNamedObject("content", new JsonObject());
                                foreach (var eventNode in content)
                                {
                                    string receiptEventId = eventNode.Key;
                                    var receiptTypes = eventNode.Value.GetObject();

                                    if (receiptTypes.ContainsKey("m.read"))
                                    {
                                        var readByUsers = receiptTypes.GetNamedObject("m.read");
                                        foreach (var userNode in readByUsers)
                                        {
                                            string readByUserId = userNode.Key.ToLower();

                                            // --- NEW: Check against your configured Bridge IDs ---
                                            bool isSelf = (readByUserId == currentUserId);

                                            string fbCfg = ConfigManager.FacebookId?.ToLower() ?? "";
                                            string dsCfg = ConfigManager.DiscordId?.ToLower() ?? "";
                                            string tgCfg = ConfigManager.TelegramId?.ToLower() ?? "";
                                            string gmCfg = ConfigManager.GMessagesId?.ToLower() ?? "";

                                            if (!string.IsNullOrEmpty(fbCfg) && readByUserId.Contains(fbCfg)) isSelf = true;
                                            if (!string.IsNullOrEmpty(dsCfg) && readByUserId.Contains(dsCfg)) isSelf = true;
                                            if (!string.IsNullOrEmpty(tgCfg) && readByUserId.Contains(tgCfg)) isSelf = true;
                                            if (!string.IsNullOrEmpty(gmCfg) && readByUserId.Contains(gmCfg)) isSelf = true;


                                            // Only render the receipt if it's actually someone else!
                                            if (!isSelf)
                                            {
                                                string displayReader = readByUserId.Split(':')[0].TrimStart('@');
                                                if (DisplayNameCache.ContainsKey(readByUserId))
                                                {
                                                    displayReader = DisplayNameCache[readByUserId];
                                                }

                                                ReadReceiptReceived?.Invoke(this, new ReadReceiptEventArgs(roomId, receiptEventId, displayReader));
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
                    System.Diagnostics.Debug.WriteLine($"Typing parse error: {ex.Message}");
                }

                // If it's a background update and no new message dropped, skip UI clutter
                // Hide totally blank ghost rooms, but keep legitimate empty chats!
                if (existingChat == null && (roomName == "Unknown Room") && string.IsNullOrEmpty(lastMessage)) continue;

                // 3. Update the UI collections safely
                if (existingChat != null)
                {
                    existingChat.DisplayName = roomName;
                    existingChat.LastMessage = lastMessage;
                    existingChat.FormattedTimestamp = formattedTime;

                    RecentChats.Remove(existingChat);
                    RecentChats.Insert(0, existingChat);
                }
                else
                {
                    var chatModel = new ChatItem(roomId, roomName, lastMessage, formattedTime, detectedNetwork);
                    if (!string.IsNullOrEmpty(finalAvatarMxc))
                    {
                        chatModel.AvatarUrl = await GetCachedAvatarAsync(finalAvatarMxc, serverUrl, accessToken);
                    }

                    RecentChats.Insert(0, chatModel);

                    switch (detectedNetwork)
                    {
                        case BridgeType.Messenger: MessengerChats.Insert(0, chatModel); break;
                        case BridgeType.Discord: DiscordChats.Insert(0, chatModel); break;
                        case BridgeType.Telegram: TelegramChats.Insert(0, chatModel); break;
                        case BridgeType.GMessages: GMessagesChats.Insert(0, chatModel); break;
                    }
                }

                // 4. Global Events & Live Tiles
                if (hasNewTimelineMessage)
                {
                    MessageReceived?.Invoke(this, new NewMessageEventArgs(roomId, rawNewMsgSender, newMsgBody, newMsgTs, newMsgEventId, newMsgMxcUrl, liveReplySender, liveReplyBody));

                    if (!_isInitialLoad && !string.IsNullOrEmpty(newMsgSender))
                    {
                        string rawSenderLower = rawNewMsgSender.ToLower();
                        bool isOwnMessage = false;

                        string fbCfg = ConfigManager.FacebookId?.ToLower() ?? "";
                        string dsCfg = ConfigManager.DiscordId?.ToLower() ?? "";
                        string tgCfg = ConfigManager.TelegramId?.ToLower() ?? "";
                        string gmCfg = ConfigManager.GMessagesId?.ToLower() ?? "";

                        if (!string.IsNullOrEmpty(fbCfg) && rawSenderLower.Contains(fbCfg)) isOwnMessage = true;
                        if (!string.IsNullOrEmpty(dsCfg) && rawSenderLower.Contains(dsCfg)) isOwnMessage = true;
                        if (!string.IsNullOrEmpty(tgCfg) && rawSenderLower.Contains(tgCfg)) isOwnMessage = true;
                        if (!string.IsNullOrEmpty(gmCfg) && rawSenderLower.Contains(gmCfg)) isOwnMessage = true;

                        try
                        {
                            var vault = new Windows.Security.Credentials.PasswordVault();
                            var creds = vault.FindAllByResource("MatrixServer");
                            if (creds.Count > 0 && rawSenderLower == creds[0].UserName.ToLower())
                            {
                                isOwnMessage = true;
                            }
                        }
                        catch { }

                        if (!isOwnMessage)
                        {
                            UpdateLiveTile(newMsgSender, newMsgBody);
                        }
                    }
                }
            }
        }

        private string FormatMatrixTimestamp(long unixTimeMilliseconds)
        {
            if (unixTimeMilliseconds == 0) return "Just now";

            // The Unix Epoch
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            // Add the Matrix milliseconds and convert to the phone's local time zone
            DateTime messageTime = epoch.AddMilliseconds(unixTimeMilliseconds).ToLocalTime();
            DateTime now = DateTime.Now;

            if (messageTime.Date == now.Date)
            {
                // Today: show short time (e.g., 10:42 AM)
                return messageTime.ToString("t");
            }
            else if (now.Date - messageTime.Date < TimeSpan.FromDays(7))
            {
                // Within the last week: show day of the week (e.g., Monday)
                return messageTime.ToString("dddd");
            }
            else
            {
                // Older than a week: show short month and day (e.g., Aug 17)
                return messageTime.ToString("MMM dd");
            }
        }

        // --- XAML EVENT HANDLERS ---

        private void OnChatItemClicked(object sender, ItemClickEventArgs e)
        {
            var clickedChat = (ChatItem)e.ClickedItem;
            // Pass the entire clickedChat object as a parameter to the ChatPage
            Frame.Navigate(typeof(ChatPage), clickedChat);
        }

        private async Task SaveCacheSecurelyAsync(string jsonString)
        {
            try
            {
                // "LOCAL=user" means only the current logged-in user on this physical device can decrypt it
                DataProtectionProvider provider = new DataProtectionProvider("LOCAL=user");

                IBuffer plainBuffer = CryptographicBuffer.ConvertStringToBinary(jsonString, BinaryStringEncoding.Utf8);
                IBuffer protectedBuffer = await provider.ProtectAsync(plainBuffer);

                StorageFile file = await ApplicationData.Current.LocalFolder.CreateFileAsync("matrix_sync_cache.dat", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteBufferAsync(file, protectedBuffer);

                System.Diagnostics.Debug.WriteLine("JSON cache encrypted and saved.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save cache: {ex.Message}");
            }
        }

        private async Task<string> LoadCacheSecurelyAsync()
        {
            try
            {
                StorageFile file = await ApplicationData.Current.LocalFolder.GetFileAsync("matrix_sync_cache.dat");
                IBuffer protectedBuffer = await FileIO.ReadBufferAsync(file);

                // We don't need the "LOCAL=user" descriptor to decrypt, the OS knows!
                DataProtectionProvider provider = new DataProtectionProvider();
                IBuffer plainBuffer = await provider.UnprotectAsync(protectedBuffer);

                return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, plainBuffer);
            }
            catch
            {
                return null; // Cache doesn't exist or is corrupted, no big deal!
            }
        }

        private async void OnJoinInviteClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var button = sender as Button;
            var invite = button?.DataContext as InviteItem;
            if (invite == null) return;

            button.Content = "Joining...";
            button.IsEnabled = false;

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            creds[0].RetrievePassword();
            string accessToken = creds[0].Password;

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();
            string encodedRoomId = Uri.EscapeDataString(invite.RoomId);

            try
            {
                using (var client = new Windows.Web.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", accessToken);

                    Uri joinUri = new Uri($"{serverUrl}/_matrix/client/v3/join/{encodedRoomId}");
                    var content = new Windows.Web.Http.HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                    var response = await client.PostAsync(joinUri, content);
                    if (response.IsSuccessStatusCode)
                    {
                        // Remove it from the pending list visually
                        for (int i = PendingInvites.Count - 1; i >= 0; i--)
                        {
                            if (PendingInvites[i].RoomId == invite.RoomId)
                            {
                                PendingInvites.RemoveAt(i);
                                break;
                            }
                        }

                        // DO NOT reset the token. DO NOT force a master sync.
                        // Let the background Matrix long-polling loop naturally catch the newly joined room!
                    }
                }
            }
            catch { }

            if (PendingInvites.Contains(invite))
            {
                button.Content = "Join";
                button.IsEnabled = true;
            }
        }

        public static void UpdateLiveTile(string senderName, string messageBody)
        {
            try
            {
                var tileUpdater = Windows.UI.Notifications.TileUpdateManager.CreateTileUpdaterForApplication();
                tileUpdater.EnableNotificationQueue(true);

                // 1. Create the Wide Tile XML using the ultra-reliable Text04 template
                var wideXml = Windows.UI.Notifications.TileUpdateManager.GetTemplateContent(Windows.UI.Notifications.TileTemplateType.TileWide310x150Text04);
                var wideText = wideXml.GetElementsByTagName("text");
                if (wideText.Length > 0)
                {
                    // Text04 only has one text block, so we merge the name and message!
                    wideText[0].InnerText = $"{senderName}: {messageBody}";
                }

                // 2. Create the Square Tile XML fallback
                var squareXml = Windows.UI.Notifications.TileUpdateManager.GetTemplateContent(Windows.UI.Notifications.TileTemplateType.TileSquare150x150Text04);
                var squareText = squareXml.GetElementsByTagName("text");
                if (squareText.Length > 0)
                {
                    // The square tile is smaller, so we just show the sender's name!
                    squareText[0].InnerText = senderName;
                }

                // 3. Merge the Square template into the Wide template
                var visualNode = wideXml.GetElementsByTagName("visual")[0];
                var squareBindingNode = squareXml.GetElementsByTagName("binding")[0];
                var importedSquareNode = wideXml.ImportNode(squareBindingNode, true);
                visualNode.AppendChild(importedSquareNode);

                // 4. Send the unified payload to the Start screen!
                var notification = new Windows.UI.Notifications.TileNotification(wideXml);
                tileUpdater.Update(notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update Live Tile: {ex.Message}");
            }
        }

        private async void OnSyncClicked(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Manual Sync Forced...");

            // 1. Force the lock open and reset the token
            _isSyncing = false;
            _isInitialLoad = true;
            _nextBatchToken = null;

            // DO NOT clear the collections here! It causes a blank screen.
            // We will clear them deep inside the parser only when new data is ready.
            await PerformInitialSyncAsync();
        }

        private async void OnNewChatClicked(object sender, RoutedEventArgs e)
        {
            var inputTextBox = new TextBox
            {
                PlaceholderText = "e.g. #room:yourserver.com",
                IsSpellCheckEnabled = false,
                IsTextPredictionEnabled = false
            };

            var dialog = new ContentDialog()
            {
                Title = "Start or Join Chat",
                Content = inputTextBox,
                PrimaryButtonText = "Join",
                SecondaryButtonText = "Cancel"
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputTextBox.Text))
            {
                await StartOrJoinChatAsync(inputTextBox.Text.Trim());
            }
        }

        private async Task StartOrJoinChatAsync(string inputTarget)
        {
            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count == 0) return;

            creds[0].RetrievePassword();
            string accessToken = creds[0].Password;

            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            string serverUrl = urlObj?.ToString();

            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken)) return;

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", accessToken);
                HttpResponseMessage response;

                try
                {
                    if (inputTarget.StartsWith("@"))
                    {
                        // 1. Direct Message (DM): Create a private room inviting the user/bot
                        Uri createRoomUri = new Uri($"{serverUrl}/_matrix/client/v3/createRoom");

                        JsonObject payload = new JsonObject();
                        JsonArray inviteArray = new JsonArray { JsonValue.CreateStringValue(inputTarget) };

                        payload.Add("invite", inviteArray);
                        payload.Add("is_direct", JsonValue.CreateBooleanValue(true));
                        payload.Add("preset", JsonValue.CreateStringValue("trusted_private_chat"));

                        HttpStringContent content = new HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                        response = await client.PostAsync(createRoomUri, content);
                    }
                    else
                    {
                        // 2. Room ID or Room Alias: Join existing room
                        string encodedTarget = Uri.EscapeDataString(inputTarget);
                        Uri joinUri = new Uri($"{serverUrl}/_matrix/client/v3/join/{encodedTarget}");
                        HttpStringContent content = new HttpStringContent("{}", Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                        response = await client.PostAsync(joinUri, content);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        // Force an immediate sync so the room appears in the Recent tab instantly
                        await PerformInitialSyncAsync();
                    }
                    else
                    {
                        var dialog = new Windows.UI.Popups.MessageDialog("Could not find or open a chat with that target. Check the Matrix ID and try again.", "Error");
                        await dialog.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start/join chat: {ex.Message}");
                }
            }
        }

        private async void OnAboutClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new Windows.UI.Popups.MessageDialog(
                "Synaptrix 8.1\n\nA native Windows Phone 8.1 purpose built Matrix client.\n\nDeveloped by: ElisaC\n\nVersion 1.0.0",
                "About Synaptrix");
            await dialog.ShowAsync();
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(SettingsPage));
        }

        private async void RegisterBackgroundTask()
        {
            // 1. Ask the user/OS for permission to run in the background
            var accessStatus = await Windows.ApplicationModel.Background.BackgroundExecutionManager.RequestAccessAsync(); //
            if (accessStatus == Windows.ApplicationModel.Background.BackgroundAccessStatus.Denied) return;

            string taskName = "MatrixSyncTask";

            // 2. Unregister the old task if it already exists
            foreach (var task in Windows.ApplicationModel.Background.BackgroundTaskRegistration.AllTasks)
            {
                if (task.Value.Name == taskName)
                {
                    task.Value.Unregister(true); //
                }
            }
            try
            {
                // Register the new Timer
                Windows.ApplicationModel.Background.BackgroundTaskBuilder builder = new Windows.ApplicationModel.Background.BackgroundTaskBuilder();
                builder.Name = taskName;
                builder.TaskEntryPoint = "Synaptrix8._1.Background.SyncBackgroundTask";

                // Windows Phone accepts 15 or 30 here. Let's try 15!
                builder.SetTrigger(new Windows.ApplicationModel.Background.TimeTrigger(15, false));
                var task = builder.Register();

                System.Diagnostics.Debug.WriteLine($"[SUCCESS] Task Registered: {task.TaskId}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CRASH] Failed to register task: {ex.Message}");
            }
        }
    }
}