using Windows.Storage;

namespace Synaptrix8._1
{
    public static class ConfigManager
    {
        private static ApplicationDataContainer Settings
        {
            get { return ApplicationData.Current.LocalSettings; }
        }

        public static string FacebookId
        {
            get
            {
                var val = Settings.Values["Cfg_FacebookId"];
                return val != null ? val.ToString() : "";
            }
            set { Settings.Values["Cfg_FacebookId"] = value; }
        }

        public static string DiscordId
        {
            get
            {
                var val = Settings.Values["Cfg_DiscordId"];
                return val != null ? val.ToString() : "";
            }
            set { Settings.Values["Cfg_DiscordId"] = value; }
        }

        public static string TelegramId
        {
            get
            {
                var val = Settings.Values["Cfg_TelegramId"];
                return val != null ? val.ToString() : "";
            }
            set { Settings.Values["Cfg_TelegramId"] = value; }
        }

        public static bool AutoDownloadImages
        {
            get
            {
                var val = Settings.Values["Cfg_AutoDownloadImages"];
                return val != null ? (bool)val : false;
            }
            set { Settings.Values["Cfg_AutoDownloadImages"] = value; }
        }

        public static bool AutoSyncOnLaunch
        {
            get
            {
                var val = Settings.Values["Cfg_AutoSyncOnLaunch"];
                return val != null ? (bool)val : true;
            }
            set { Settings.Values["Cfg_AutoSyncOnLaunch"] = value; }
        }

        public static string GMessagesId
        {
            get { return (string)ApplicationData.Current.LocalSettings.Values["GMessagesId"] ?? ""; }
            set { ApplicationData.Current.LocalSettings.Values["GMessagesId"] = value; }
        }
    }
}