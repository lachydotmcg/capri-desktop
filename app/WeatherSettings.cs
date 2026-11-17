using System;
using System.Drawing;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        [DataMember] public bool Weather;
        [DataMember] public string WeatherCity;
        [DataMember] public string WeatherSaid;
    }

    internal sealed partial class Companion
    {
        private Weather weather;

        private void StartWeather()
        {
            weather = new Weather();
            weather.On = delegate { return preferences.Weather; };
            weather.City = delegate { return preferences.WeatherCity; };
            weather.Said = delegate { return preferences.WeatherSaid; };
            weather.Saying = delegate(string said) { preferences.WeatherSaid = said; SavePreferences(); };
            Hands.Forecast = weather.Do;
            Hands.WeatherOn = delegate { return preferences.Weather; };
        }

        internal bool WeatherOn { get { return preferences.Weather; } }

        internal string WeatherCity { get { return preferences.WeatherCity ?? ""; } }

        internal void ToggleWeather()
        {
            preferences.Weather = !preferences.Weather;
            weather.Forget();
            SavePreferences();
        }

        internal void SetWeatherCity(string typed)
        {
            string city = (typed ?? "").Trim();
            if (city.Length > 80) city = city.Substring(0, 80).Trim();
            if (city == WeatherCity) return;
            preferences.WeatherCity = city.Length == 0 ? null : city;
            weather.Forget();
            SavePreferences();
        }

        private bool CheckWeather()
        {
            if (quitting || weather == null) return false;
            weather.Poll();
            if (!weather.Waiting) return false;
            if (preferences.OnlyWhenAsked) { weather.Take(); return false; }
            if (outro != null || dragging || pressed || menu.Visible || typer.Visible) return false;
            if (Voicing || speech != null || talkState == TalkState.Talking || working == null && talkState == TalkState.Thinking && !InChat) return false;
            if (working == null && (asideWaiting == asking || toolEnds.Exists(end => end.Ask == asking && end.Replied))) return false;
            string line = weather.Take();
            if (line == null) return false;
            if (hidden && !fullScreenHidden) SetHidden(false);
            reminderUp = true;
            reminderText = true;
            reminderTicket = -1;
            bool bubbleOnly = InChat || Hushed;
            if (working != null)
            {
                asideShown = line;
                asideUntil = clock.Elapsed.TotalSeconds + BubbleHold(line.Length);
                if (docked) GameHud.Say(line, LettersPerSecond);
                UpdateTalk(clock.Elapsed.TotalSeconds);
                if (!bubbleOnly && job != null) SayAside(line, job);
            }
            else
            {
                heard = "reminder";
                box.Prompt = "";
                reminderTicket = remarkTicket = ++asking;
                if (bubbleOnly) Begin(remarkTicket, line, TalkState.Talking, null);
                else
                {
                    Answer(remarkTicket, line, TalkState.Talking);
                    if (reply == null && answerVoice != asking) Begin(remarkTicket, line, TalkState.Talking, null);
                }
            }
            if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            return true;
        }
    }

    internal sealed partial class Mainframe
    {
        private Toggle weatherSwitch;
        private Entry weatherCity;

        private void BuildWeather(int y)
        {
            Section("WEATHER >", 0, y);
            weatherSwitch = Switch("Weather", 0, y + 24, delegate { if (!syncing) capri.ToggleWeather(); });
            weatherSwitch.AccessibleName = "Weather. Opt in to forecasts from Open-Meteo for the city you type; off by default";
            Caption("City", 28, y + 56, Type(9.5f), Rose);
            weatherCity = Input(ValueAt, y + 52, ValueWide, 22);
            weatherCity.Cue = "Your city";
            weatherCity.Box.AccessibleName = "City for the weather";
            weatherCity.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseWeatherCity(); } };
            weatherCity.Box.LostFocus += delegate { UseWeatherCity(); };
            Label hint = Caption("From Open-Meteo, no key. Only the city you\ntype is sent. I never look you up by IP.", 28, y + 80, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
            hint.AutoSize = false;
            hint.Bounds = new Rectangle(28, y + 80, 328, 36);
        }

        private void UseWeatherCity()
        {
            if (syncing) return;
            capri.SetWeatherCity(weatherCity.Box.Text);
            if (!weatherCity.Box.Focused) weatherCity.Box.Text = capri.WeatherCity;
        }

        private void SyncWeather()
        {
            weatherSwitch.Checked = capri.WeatherOn;
            if (!weatherCity.Box.Focused) weatherCity.Box.Text = capri.WeatherCity;
        }

        internal string WeatherShown { get { return (weatherSwitch.Checked ? "on" : "off") + " | " + weatherCity.Box.Text; } }
    }
}
