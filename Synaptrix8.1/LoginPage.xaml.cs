using System;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.Web.Http;
using Windows.Data.Json;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Synaptrix8._1
{
    public sealed partial class LoginPage : Page
    {
        public LoginPage()
        {
            this.InitializeComponent();

            this.Loaded += LoginPage_Loaded;
        }

        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            CheckForExistingLogin();
        }

        private void CheckForExistingLogin()
        {
            var vault = new PasswordVault();
            try
            {
                var credentialList = vault.FindAllByResource("MatrixServer");
                if (credentialList.Count > 0)
                {
                    Frame.Navigate(typeof(MainPage));
                }
            }
            catch (Exception)
            {

            }
        }

        private async void OnLoginClicked(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            LoginProgress.Visibility = Visibility.Visible;
            LoginButton.IsEnabled = false;

            string server = ServerUrlBox.Text.TrimEnd('/');
            string user = UsernameBox.Text.Trim();
            string pass = PasswordBox.Password;

            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                ShowError("Please fill in all fields.");
                return;
            }

            JsonObject payload = new JsonObject
            {
                { "type", JsonValue.CreateStringValue("m.login.password") },
                { "user", JsonValue.CreateStringValue(user) },
                { "password", JsonValue.CreateStringValue(pass) }
            };

            await AuthenticateAsync(server, payload);
        }

        private async Task AuthenticateAsync(string serverUrl, JsonObject payload)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    Uri requestUri = new Uri($"{serverUrl}/_matrix/client/v3/login");
                    HttpStringContent content = new HttpStringContent(payload.Stringify(), Windows.Storage.Streams.UnicodeEncoding.Utf8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(requestUri, content);
                    string responseString = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        JsonObject jsonResponse = JsonObject.Parse(responseString);
                        string accessToken = jsonResponse.GetNamedString("access_token");
                        string userId = jsonResponse.GetNamedString("user_id");

                        SaveCredentials(serverUrl, userId, accessToken);

                        Frame.Navigate(typeof(MainPage));
                    }
                    else
                    {

                        try
                        {
                            JsonObject errorJson = JsonObject.Parse(responseString);
                            ShowError(errorJson.GetNamedString("error"));
                        }
                        catch
                        {
                            ShowError($"HTTP {response.StatusCode}: {response.ReasonPhrase}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError("Connection failed. Check your server URL and network connection.");
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        private void SaveCredentials(string serverUrl, string userId, string accessToken)
        {
            var vault = new PasswordVault();
            vault.Add(new PasswordCredential("MatrixServer", userId, accessToken));

            ApplicationData.Current.LocalSettings.Values["HomeserverUrl"] = serverUrl;
        }

        private void ShowError(string message)
        {
            LoginProgress.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = true;
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}