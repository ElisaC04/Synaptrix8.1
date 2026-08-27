
using System;
using Windows.ApplicationModel.Background;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using Windows.Web.Http;
using Windows.Web.Http.Headers;
using Windows.Security.Credentials;
using Windows.Storage;

namespace Synaptrix8._1.Background
{
    public sealed class SyncBackgroundTask : IBackgroundTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            BackgroundTaskDeferral deferral = taskInstance.GetDeferral();

            try
            {
                var vault = new PasswordVault();
                var creds = vault.FindAllByResource("MatrixServer");
                if (creds.Count == 0) return;

                creds[0].RetrievePassword();
                string accessToken = creds[0].Password;
                string serverUrl = ApplicationData.Current.LocalSettings.Values["HomeserverUrl"]?.ToString();
                string nextBatch = ApplicationData.Current.LocalSettings.Values["BackgroundSyncToken"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(nextBatch)) return;

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Authorization = new HttpCredentialsHeaderValue("Bearer", accessToken);
                    Uri requestUri = new Uri($"{serverUrl}/_matrix/client/v3/sync?since={nextBatch}&timeout=0");

                    var response = await client.GetAsync(requestUri);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonString = await response.Content.ReadAsStringAsync();
                        Windows.Data.Json.JsonObject root;

                        if (Windows.Data.Json.JsonObject.TryParse(jsonString, out root))
                        {
                            ApplicationData.Current.LocalSettings.Values["BackgroundSyncToken"] = root.GetNamedString("next_batch", nextBatch);

                            if (root.ContainsKey("rooms"))
                            {
                                var joinedRooms = root.GetNamedObject("rooms").GetNamedObject("join", new Windows.Data.Json.JsonObject());
                                int newMessagesCount = 0;

                                string latestSender = "";
                                string latestBody = "";

                                foreach (var room in joinedRooms)
                                {
                                    var timeline = room.Value.GetObject().GetNamedObject("timeline", new Windows.Data.Json.JsonObject());
                                    var events = timeline.GetNamedArray("events", new Windows.Data.Json.JsonArray());

                                    foreach (var evtObj in events)
                                    {
                                        var evt = evtObj.GetObject();
                                        if (evt.GetNamedString("type", "") == "m.room.message")
                                        {
                                            string sender = evt.GetNamedString("sender", "");
                                            if (sender.ToLower() != creds[0].UserName.ToLower())
                                            {
                                                newMessagesCount++;

                                                latestSender = sender.Split(':')[0].TrimStart('@');

                                                var content = evt.GetNamedObject("content", new Windows.Data.Json.JsonObject());
                                                string msgType = content.GetNamedString("msgtype", "m.text");

                                                if (msgType == "m.image") latestBody = "📷 Image";
                                                else if (msgType == "m.file" || msgType == "m.video" || msgType == "m.audio") latestBody = "📁 Media";
                                                else latestBody = content.GetNamedString("body", "Sent a message");
                                            }
                                        }
                                    }
                                }

                                if (newMessagesCount > 0)
                                {
                                    ShowToastNotification("Synaptrix", $"You have {newMessagesCount} new message{(newMessagesCount > 1 ? "s" : "")}!");
                                    System.Threading.Tasks.Task.Delay(500).Wait();

                                    UpdateLiveTile(latestSender, latestBody, newMessagesCount);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Background task failed: {ex.Message}");
            }
            finally
            {
                deferral.Complete(); //
            }
        }

        private void ShowToastNotification(string title, string content)
        {
            ToastTemplateType toastTemplate = ToastTemplateType.ToastText02;
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(toastTemplate);
            XmlNodeList textElements = toastXml.GetElementsByTagName("text");
            textElements[0].AppendChild(toastXml.CreateTextNode(title));
            textElements[1].AppendChild(toastXml.CreateTextNode(content));

            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(toastXml));
        }

        private void UpdateLiveTile(string senderName, string messageBody, int count)
        {
            try
            {
                var tileUpdater = ToastNotificationManager.CreateToastNotifier();
                var updater = TileUpdateManager.CreateTileUpdaterForApplication();
                updater.EnableNotificationQueue(true);

                var wideXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileWide310x150Text04);
                var wideText = wideXml.GetElementsByTagName("text");
                if (wideText.Length > 0)
                {
                    wideText[0].InnerText = $"{senderName}: {messageBody}";
                }

                var squareXml = TileUpdateManager.GetTemplateContent(TileTemplateType.TileSquare150x150Text04);
                var squareText = squareXml.GetElementsByTagName("text");
                if (squareText.Length > 0)
                {
                    squareText[0].InnerText = $"{count} New Message{(count > 1 ? "s" : "")}";
                }

                var visualNode = wideXml.GetElementsByTagName("visual")[0];
                var squareBindingNode = squareXml.GetElementsByTagName("binding")[0];
                var importedSquareNode = wideXml.ImportNode(squareBindingNode, true);
                visualNode.AppendChild(importedSquareNode);

                var notification = new TileNotification(wideXml);
                updater.Update(notification);
            }
            catch (Exception ex)
            {

            }
        }
    }
}
