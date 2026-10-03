using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveCaptionsTranslator.Utils;
using LiveCaptionsTranslator.utils;

namespace LiveCaptionsTranslator.models
{
    public class MainWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool topmost = true;
        private bool captionLogEnabled = false;
        private bool latencyShow = false;
        private int originalFontSize = 15;
        private int translatedFontSize = 18;
        private bool closeToTray = true;
        private bool trayHintShown = false;
        private bool autoStart = false;

        public bool Topmost
        {
            get => topmost;
            set
            {
                topmost = value;
                OnPropertyChanged("Topmost");
            }
        }
        public bool CaptionLogEnabled
        {
            get => captionLogEnabled;
            set
            {
                captionLogEnabled = value;
                OnPropertyChanged("CaptionLogEnabled");
            }
        }
        public bool LatencyShow
        {
            get => latencyShow;
            set
            {
                latencyShow = value;
                OnPropertyChanged("LatencyShow");
            }
        }
        public int OriginalFontSize
        {
            get => originalFontSize;
            set
            {
                originalFontSize = value;
                OnPropertyChanged("OriginalFontSize");
            }
        }
        public int TranslatedFontSize
        {
            get => translatedFontSize;
            set
            {
                translatedFontSize = value;
                OnPropertyChanged("TranslatedFontSize");
            }
        }

        // Closing the main window hides it to the tray instead of exiting.
        public bool CloseToTray
        {
            get => closeToTray;
            set
            {
                closeToTray = value;
                OnPropertyChanged("CloseToTray");
            }
        }

        // Whether the "minimized to tray" balloon hint has been shown once.
        public bool TrayHintShown
        {
            get => trayHintShown;
            set
            {
                trayHintShown = value;
                OnPropertyChanged("TrayHintShown");
            }
        }

        // Registers the app in the HKCU Run key (starts minimized to tray).
        public bool AutoStart
        {
            get => autoStart;
            set
            {
                autoStart = value;
                AutoStartUtil.SetEnabled(value);
                OnPropertyChanged("AutoStart");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.ScheduleSave();
        }
    }

    public class OverlayWindowState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private int fontSize = 15;
        private Color fontColor = Color.White;
        private FontBold fontBold = FontBold.None;
        private double fontStroke = 0.0;

        private Color backgroundColor = Color.Black;
        private int opacity = 150;

        public int FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                OnPropertyChanged("FontSize");
            }
        }
        public Color FontColor
        {
            get => fontColor;
            set
            {
                fontColor = value;
                OnPropertyChanged("FontColor");
            }
        }
        public FontBold FontBold
        {
            get => fontBold;
            set
            {
                fontBold = value;
                OnPropertyChanged("FontBold");
            }
        }
        public double FontStroke
        {
            get => fontStroke;
            set
            {
                fontStroke = value;
                OnPropertyChanged("FontStroke");
            }
        }
        public Color BackgroundColor
        {
            get => backgroundColor;
            set
            {
                backgroundColor = value;
                OnPropertyChanged("BackgroundColor");
            }
        }
        public int Opacity
        {
            get => opacity;
            set
            {
                opacity = value;
                OnPropertyChanged("Opacity");
            }
        }

        public void OnPropertyChanged([CallerMemberName] string propName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
            Translator.Setting?.ScheduleSave();
        }
    }
}