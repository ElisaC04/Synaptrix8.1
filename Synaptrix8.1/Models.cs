using System;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace Synaptrix8._1
{
    public enum BridgeType
    {
        Messenger,
        Discord,
        Telegram,
        Matrix,
        GMessages
    }

    public class ChatItem
    {
        public string RoomId { get; set; }
        public string DisplayName { get; set; }
        public string LastMessage { get; set; }
        public string FormattedTimestamp { get; set; }
        public Uri AvatarUrl { get; set; }
        public BridgeType Network { get; set; }

        public ChatItem(string roomId, string name, string message, string time, BridgeType network)
        {
            RoomId = roomId;
            DisplayName = name;
            LastMessage = message;
            FormattedTimestamp = time;
            Network = network;
        }

        public string Initial => string.IsNullOrEmpty(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpper();

        public string BridgeLabel
        {
            get
            {
                switch (Network)
                {
                    case BridgeType.Messenger: return "META";
                    case BridgeType.Discord: return "DISCORD";
                    case BridgeType.Telegram: return "TG";
                    case BridgeType.GMessages: return "SMS";
                    default: return "MATRIX";
                }
            }
        }

        public SolidColorBrush BadgeColor
        {
            get
            {
                switch (Network)
                {
                    case BridgeType.Messenger:
                        return new SolidColorBrush(Colors.DodgerBlue);
                    case BridgeType.Discord:
                        return new SolidColorBrush(Colors.MediumPurple);
                    case BridgeType.Telegram:
                        return new SolidColorBrush(Colors.CornflowerBlue);
                    case BridgeType.GMessages:
                        return new SolidColorBrush(Colors.Khaki);
                    default:
                        return new SolidColorBrush(Colors.Gray);
                }
            }
        }
    }

    public class NewMessageEventArgs : EventArgs
    {
        public string RoomId { get; private set; }
        public string Sender { get; private set; }
        public string Body { get; private set; }
        public long Timestamp { get; private set; }
        public string EventId { get; private set; }
        public string MxcUrl { get; private set; }
        public string ReplySender { get; private set; }
        public string ReplyBody { get; private set; }

        public NewMessageEventArgs(string roomId, string sender, string body, long ts, string eventId, string mxcUrl = "", string replySender = "", string replyBody = "")
        {
            RoomId = roomId;
            Sender = sender;
            Body = body;
            Timestamp = ts;
            EventId = eventId;
            MxcUrl = mxcUrl;
            ReplySender = replySender;
            ReplyBody = replyBody;
        }
    }

    public class InviteItem
    {
        public string RoomId { get; set; }
        public string RoomName { get; set; }
        public string Inviter { get; set; }

        public InviteItem(string roomId, string roomName, string inviter)
        {
            RoomId = roomId;
            RoomName = roomName;
            Inviter = inviter;
        }
    }

    public class MessageItem : System.ComponentModel.INotifyPropertyChanged
    {
        public string EventId { get; set; }
        public string Sender { get; set; }
        public string Body { get; set; }
        public string Timestamp { get; set; }
        public bool IsOwn { get; set; }
        public string ImageUrl { get; set; }
        public string RawMediaUrl { get; set; }
        public string ReplySender { get; set; }
        public string ReplyBody { get; set; }
        public Windows.UI.Xaml.Visibility ReplyVisibility =>
            string.IsNullOrEmpty(ReplyBody) ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;

        public Windows.UI.Xaml.Visibility ImageVisibility => string.IsNullOrEmpty(ImageUrl) ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;
        public Windows.UI.Xaml.Visibility TextVisibility => string.IsNullOrEmpty(Body) ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;

        public Windows.UI.Xaml.Visibility DownloadButtonVisibility =>
            (!string.IsNullOrEmpty(RawMediaUrl) && string.IsNullOrEmpty(ImageUrl)) ?
            Windows.UI.Xaml.Visibility.Visible : Windows.UI.Xaml.Visibility.Collapsed;

        public MessageItem(string eventId, string sender, string body, string timestamp, bool isOwn, string imageUrl = "", string rawMediaUrl = "", string replySender = "", string replyBody = "")
        {
            EventId = eventId;
            Sender = sender;
            Body = body;
            Timestamp = timestamp;
            IsOwn = isOwn;
            ImageUrl = imageUrl;
            RawMediaUrl = rawMediaUrl;
            ReplySender = replySender;
            ReplyBody = replyBody;
        }

        public Windows.UI.Xaml.HorizontalAlignment BubbleAlignment =>
            IsOwn ? Windows.UI.Xaml.HorizontalAlignment.Right : Windows.UI.Xaml.HorizontalAlignment.Left;

        public Windows.UI.Xaml.Visibility NameVisibility =>
            IsOwn ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;

        public Windows.UI.Xaml.Media.SolidColorBrush BubbleColor =>
            IsOwn ?
            (Windows.UI.Xaml.Media.SolidColorBrush)Windows.UI.Xaml.Application.Current.Resources["PhoneAccentBrush"] :
            new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 60, 60, 60));

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        private System.Collections.Generic.List<string> _readers = new System.Collections.Generic.List<string>();
        private string _readReceiptsText = "";

        public string ReadReceiptsText => _readReceiptsText;

        public Windows.UI.Xaml.Visibility ReadReceiptVisibility =>
            string.IsNullOrEmpty(_readReceiptsText) ? Windows.UI.Xaml.Visibility.Collapsed : Windows.UI.Xaml.Visibility.Visible;

        public void AddReader(string name)
        {
            if (!_readers.Contains(name))
            {
                _readers.Add(name);
                RefreshReceiptText();
            }
        }

        public void RemoveReader(string name)
        {
            if (_readers.Contains(name))
            {
                _readers.Remove(name);
                RefreshReceiptText();
            }
        }

        private void RefreshReceiptText()
        {
            if (_readers.Count == 0)
                _readReceiptsText = "";
            else if (_readers.Count == 1)
                _readReceiptsText = $"Seen by {_readers[0]}";
            else if (_readers.Count == 2)
                _readReceiptsText = $"Seen by {_readers[0]} and {_readers[1]}";
            else
                _readReceiptsText = $"Seen by {_readers[0]} and {_readers.Count - 1} others";

            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ReadReceiptsText)));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ReadReceiptVisibility)));
        }
    }

    public class TypingEventArgs : EventArgs
    {
        public string RoomId { get; private set; }
        public string TypersText { get; private set; }

        public TypingEventArgs(string roomId, string typersText)
        {
            RoomId = roomId;
            TypersText = typersText;
        }
    }

    public class ReadReceiptEventArgs : EventArgs
    {
        public string RoomId { get; private set; }
        public string EventId { get; private set; }
        public string ReaderName { get; private set; }

        public ReadReceiptEventArgs(string roomId, string eventId, string readerName)
        {
            RoomId = roomId;
            EventId = eventId;
            ReaderName = readerName;
        }
    }
}
