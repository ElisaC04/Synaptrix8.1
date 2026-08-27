using System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Threading.Tasks;
using Windows.Web.Http;
using Windows.Data.Json;

namespace Synaptrix8._1
{
    public sealed partial class SettingsPage : Page
    {
        private string _accessToken;
        private string _serverUrl;
        private string _userId;

        public SettingsPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            App.FilePicked -= OnFilePicked;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var ignored = UpdateServerStatusUIAsync();

            FacebookTextBox.Text = ConfigManager.FacebookId ?? "";
            DiscordTextBox.Text = ConfigManager.DiscordId ?? "";
            TelegramTextBox.Text = ConfigManager.TelegramId ?? "";
            GMessagesIdBox.Text = ConfigManager.GMessagesId ?? "";


            AutoSyncToggle.IsOn = ConfigManager.AutoSyncOnLaunch;
            AutoDownloadToggle.IsOn = ConfigManager.AutoDownloadImages;

            App.FilePicked += OnFilePicked;

            var vault = new Windows.Security.Credentials.PasswordVault();
            var creds = vault.FindAllByResource("MatrixServer");
            if (creds.Count > 0)
            {
                creds[0].RetrievePassword();
                _accessToken = creds[0].Password;
                _userId = creds[0].UserName;
            }

            if (!string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_serverUrl))
            {
                var loadTask = LoadProfileDataAsync();
            }
        }

        private async Task LoadProfileDataAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", _accessToken);

                    Uri uri = new Uri($"{_serverUrl}/_matrix/client/v3/profile/{Uri.EscapeDataString(_userId)}");
                    var response = await client.GetAsync(uri);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonString = await response.Content.ReadAsStringAsync();
                        JsonObject root;
                        if (JsonObject.TryParse(jsonString, out root))
                        {
                            string currentName = root.GetNamedString("displayname", "");
                            DisplayNameBox.Text = currentName;

                            if (!string.IsNullOrEmpty(currentName))
                            {
                                ProfileInitial.Text = currentName.Substring(0, 1).ToUpper();
                            }

                            string avatarMxc = root.GetNamedString("avatar_url", "");
                            if (!string.IsNullOrEmpty(avatarMxc))
                            {
                                var cachedUri = await MainPage.GetCachedAvatarAsync(avatarMxc, _serverUrl, _accessToken, true);
                                if (cachedUri != null)
                                {
                                    var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage(cachedUri);
                                    ProfileImage.Source = bitmap;
                                    ProfileInitial.Visibility = Visibility.Collapsed;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                ProfileStatusText.Text = "Could not reach server to load profile.";
            }
        }

        private async void OnSaveNameClicked(object sender, RoutedEventArgs e)
        {
            string newName = DisplayNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName) || string.IsNullOrEmpty(_accessToken)) return;

            SaveNameButton.IsEnabled = false;
            SaveNameButton.Content = "...";
            ProfileStatusText.Text = "";

            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", _accessToken);
                    Uri uri = new Uri($"{_serverUrl}/_matrix/client/v3/profile/{Uri.EscapeDataString(_userId)}/displayname");

                    JsonObject payload = new JsonObject();
                    payload.Add("displayname", JsonValue.CreateStringValue(newName));

                    var content = new HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                    var response = await client.PutAsync(uri, content);

                    if (response.IsSuccessStatusCode)
                    {
                        ProfileStatusText.Text = "Display name updated globally!";
                        ProfileInitial.Text = newName.Substring(0, 1).ToUpper();
                    }
                    else
                    {
                        ProfileStatusText.Text = "Server rejected the name change.";
                    }
                }
            }
            catch
            {
                ProfileStatusText.Text = "Network error. Try again.";
            }

            SaveNameButton.IsEnabled = true;
            SaveNameButton.Content = "Update";
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            ConfigManager.FacebookId = FacebookTextBox.Text.Trim();
            ConfigManager.DiscordId = DiscordTextBox.Text.Trim();
            ConfigManager.TelegramId = TelegramTextBox.Text.Trim();
            ConfigManager.GMessagesId = GMessagesIdBox.Text.Trim();
            ConfigManager.AutoSyncOnLaunch = AutoSyncToggle.IsOn;
            ConfigManager.AutoDownloadImages = AutoDownloadToggle.IsOn;

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async Task UpdateServerStatusUIAsync()
        {
            object urlObj;
            Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("HomeserverUrl", out urlObj);
            _serverUrl = urlObj?.ToString();

            if (string.IsNullOrEmpty(_serverUrl))
            {
                ServerStatusTextBlock.Text = "Status: No Server Configured";
                ServerStatusTextBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
                return;
            }

            ServerStatusTextBlock.Text = $"Checking connection to {_serverUrl}...";
            ServerStatusTextBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Gray);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var response = await client.GetAsync(new Uri($"{_serverUrl}/_matrix/client/versions"));
                    if (response.IsSuccessStatusCode)
                    {
                        ServerStatusTextBlock.Text = $"Connected to:\n{_serverUrl}";
                        ServerStatusTextBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.LightGreen);
                    }
                    else
                    {
                        ServerStatusTextBlock.Text = $"Couldn't connect to:\n{_serverUrl}";
                        ServerStatusTextBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
                    }
                }
            }
            catch
            {
                ServerStatusTextBlock.Text = $"Server Unreachable:\n{_serverUrl}";
                ServerStatusTextBlock.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Red);
            }
        }

        private void OnChangeAvatarClicked(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".png");

            picker.PickSingleFileAndContinue();
        }

        private async void OnFilePicked(object sender, Windows.Storage.StorageFile file)
        {
            if (file == null || string.IsNullOrEmpty(_accessToken)) return;

            ChangeAvatarButton.IsEnabled = false;
            ChangeAvatarButton.Content = "Uploading...";
            ProfileStatusText.Text = "Uploading image...";

            try
            {
                using (var client = new Windows.Web.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", _accessToken);

                    var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                    var streamContent = new Windows.Web.Http.HttpStreamContent(stream);
                    streamContent.Headers.ContentType = new Windows.Web.Http.Headers.HttpMediaTypeHeaderValue(file.ContentType);

                    Uri uploadUri = new Uri($"{_serverUrl}/_matrix/media/v3/upload?filename={file.Name}");
                    var uploadResponse = await client.PostAsync(uploadUri, streamContent);

                    if (uploadResponse.IsSuccessStatusCode)
                    {
                        string responseString = await uploadResponse.Content.ReadAsStringAsync();
                        JsonObject jsonResponse;

                        if (JsonObject.TryParse(responseString, out jsonResponse))
                        {
                            string mxcUri = jsonResponse.GetNamedString("content_uri", "");

                            Uri profileUri = new Uri($"{_serverUrl}/_matrix/client/v3/profile/{Uri.EscapeDataString(_userId)}/avatar_url");

                            JsonObject payload = new JsonObject();
                            payload.Add("avatar_url", JsonValue.CreateStringValue(mxcUri));

                            var putContent = new Windows.Web.Http.HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");
                            var profileResponse = await client.PutAsync(profileUri, putContent);

                            if (profileResponse.IsSuccessStatusCode)
                            {
                                ProfileStatusText.Text = "Profile picture updated globally!";

                                var bitmap = new Windows.UI.Xaml.Media.Imaging.BitmapImage();
                                stream.Seek(0);
                                await bitmap.SetSourceAsync(stream);
                                ProfileImage.Source = bitmap;
                                ProfileInitial.Visibility = Visibility.Collapsed;
                            }
                            else
                            {
                                ProfileStatusText.Text = "Failed to link avatar to profile.";
                            }
                        }
                    }
                    else
                    {
                        ProfileStatusText.Text = "Failed to upload image to server.";
                    }
                }
            }
            catch (Exception ex)
            {
                ProfileStatusText.Text = "Network error during upload.";
            }

            ChangeAvatarButton.IsEnabled = true;
            ChangeAvatarButton.Content = "Change Picture";
        }

        private async void OnLogoutClicked(object sender, RoutedEventArgs e)
        {
            var dialog = new Windows.UI.Popups.MessageDialog("Are you sure you want to log out? This will clear all offline data from this device.", "Logout");

            dialog.Commands.Add(new Windows.UI.Popups.UICommand("Yes", async (cmd) =>
            {
                try
                {
                    var folder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var files = await folder.GetFilesAsync();
                    foreach (var file in files)
                    {
                        if (file.Name.EndsWith(".dat") || file.Name.EndsWith(".png"))
                        {
                            await file.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete);
                        }
                    }
                }
                catch { }

                Windows.Storage.ApplicationData.Current.LocalSettings.Values.Clear();

                var vault = new Windows.Security.Credentials.PasswordVault();
                try
                {
                    var credentials = vault.FindAllByResource("MatrixServer");
                    foreach (var cred in credentials)
                    {
                        vault.Remove(cred);
                    }
                }
                catch { }

                Frame.Navigate(typeof(LoginPage));
                Frame.BackStack.Clear();
            }));
            dialog.Commands.Add(new Windows.UI.Popups.UICommand("Cancel"));
            await dialog.ShowAsync();
        }
    }
}