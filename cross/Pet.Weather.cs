using System;
using System.Runtime.Serialization;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [DataMember] internal bool Weather;
        [DataMember] internal string WeatherCity;
        [DataMember] internal string WeatherSaid;
    }

    internal sealed partial class Pet
    {
        private Weather weather;
        private double weatherAt;

        internal bool WeatherOn { get { return Saved.Weather; } }
        internal string WeatherCity { get { return Saved.WeatherCity; } }

        private void StartWeather()
        {
            weather = new Weather();
            weather.On = () => Saved.Weather;
            weather.City = () => Saved.WeatherCity;
            weather.Said = () => Saved.WeatherSaid;
            weather.Saying = said => { Saved.WeatherSaid = said; Save(); };
            Hands.Forecast = weather.Do;
            Hands.WeatherOn = () => Saved.Weather;
        }

        internal void SetWeather(bool on)
        {
            Saved.Weather = on;
            weather.Forget();
            Save();
        }

        internal void SetWeatherCity(string typed)
        {
            string city = (typed ?? "").Trim();
            if (city.Length > 80) city = city.Substring(0, 80);
            Saved.WeatherCity = city.Length == 0 ? null : city;
            weather.Forget();
            Save();
            Reply(++asking, "(weather)", city.Length == 0 ? "I've forgotten your weather city." : Saved.Weather ? "Got it, I'll check the weather for " + city + "." : "Saved " + city + ". Turn on Weather and I'll check it for you.");
        }

        private bool CheckWeather()
        {
            if (quitting || weather == null || Now < weatherAt) return false;
            weatherAt = Now + 5;
            weather.Poll();
            if (!weather.Waiting || away || dragging || pressed || outro != null || ContextMenu.IsOpen || Talk.Typing || Listening || Talk.Aloud || !Talk.Quiet || reminderLine != null) return false;
            string line = weather.Take();
            if (line == null) return false;
            ReminderSays(line);
            return true;
        }
    }
}
