using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class WeatherPlace
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "latitude")] public double Latitude;
        [DataMember(Name = "longitude")] public double Longitude;
        [DataMember(Name = "country")] public string Country;
        [DataMember(Name = "country_code")] public string Code;
        [DataMember(Name = "admin1")] public string Region;

        internal bool Fahrenheit { get { return Regex.IsMatch(Code ?? "", "^(US|LR|MM|BS|KY|PW|FM|MH)$", RegexOptions.IgnoreCase); } }
    }

    [DataContract]
    internal sealed class WeatherPlaces
    {
        [DataMember(Name = "results")] public List<WeatherPlace> Results;
    }

    [DataContract]
    internal sealed class WeatherNow
    {
        [DataMember(Name = "time")] public string Time;
        [DataMember(Name = "temperature_2m")] public double? Temperature;
        [DataMember(Name = "apparent_temperature")] public double? Feels;
        [DataMember(Name = "weather_code")] public int? Code;
        [DataMember(Name = "wind_speed_10m")] public double? Wind;
    }

    [DataContract]
    internal sealed class WeatherDays
    {
        [DataMember(Name = "time")] public string[] Time;
        [DataMember(Name = "weather_code")] public int?[] Code;
        [DataMember(Name = "temperature_2m_max")] public double?[] High;
        [DataMember(Name = "temperature_2m_min")] public double?[] Low;
        [DataMember(Name = "precipitation_probability_max")] public int?[] Rain;
        [DataMember(Name = "precipitation_sum")] public double?[] Fall;
        [DataMember(Name = "snowfall_sum")] public double?[] Snow;
        [DataMember(Name = "wind_gusts_10m_max")] public double?[] Gusts;
        [DataMember(Name = "uv_index_max")] public double?[] Uv;
    }

    [DataContract]
    internal sealed class WeatherHours
    {
        [DataMember(Name = "time")] public string[] Time;
        [DataMember(Name = "precipitation_probability")] public int?[] Rain;
        [DataMember(Name = "weather_code")] public int?[] Code;
    }

    [DataContract]
    internal sealed class WeatherReport
    {
        [DataMember(Name = "current")] public WeatherNow Now;
        [DataMember(Name = "daily")] public WeatherDays Days;
        [DataMember(Name = "hourly")] public WeatherHours Hours;
    }

    internal sealed class Weather
    {
        internal const string Geocoder = "https://geocoding-api.open-meteo.com/v1/search";
        internal const string Forecaster = "https://api.open-meteo.com/v1/forecast";
        internal const string OffLine = "Weather is off. Turn it on in my Settings under WEATHER and type your city there, and I'll check Open-Meteo for you.";
        internal const string NoCity = "What city should I check? Type yours in my Settings under WEATHER, or ask like what's the weather in Sydney.";
        internal const int Fresh = 20;
        internal const int Every = 30;

        internal Func<bool> On = delegate { return false; };
        internal Func<string> City = delegate { return null; };
        internal Func<string> Said = delegate { return null; };
        internal Action<string> Saying = delegate { };
        internal Func<DateTime> Clock = delegate { return DateTime.Now; };
        internal Func<string, string> Fetch = Download;

        private readonly object gate = new object();
        private readonly Dictionary<string, WeatherPlace> places = new Dictionary<string, WeatherPlace>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KeyValuePair<DateTime, WeatherReport>> reports = new Dictionary<string, KeyValuePair<DateTime, WeatherReport>>(StringComparer.OrdinalIgnoreCase);
        private DateTime checkedAt = DateTime.MinValue;
        private string pending, pendingKind, pendingDay;
        private int checking;

        internal static string Download(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Capri-Desktop";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(body, Encoding.UTF8))
            {
                char[] buffer = new char[4096];
                StringBuilder text = new StringBuilder();
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    text.Append(buffer, 0, read);
                    if (text.Length > 400000) throw new IOException("Open-Meteo sent too much.");
                }
                return text.ToString();
            }
        }

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return new DataContractJsonSerializer(typeof(T)).ReadObject(input) as T;
        }

        internal static string Code(string name) { return Uri.EscapeDataString(name ?? ""); }

        internal static string PlaceUrl(string name)
        {
            return Geocoder + "?name=" + Code(name) + "&count=10&language=en&format=json";
        }

        internal static string ReportUrl(WeatherPlace place)
        {
            return Forecaster + "?latitude=" + place.Latitude.ToString("0.####", CultureInfo.InvariantCulture) + "&longitude=" + place.Longitude.ToString("0.####", CultureInfo.InvariantCulture)
                + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m"
                + "&hourly=precipitation_probability,weather_code"
                + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,precipitation_sum,snowfall_sum,wind_gusts_10m_max,uv_index_max"
                + "&timezone=auto&forecast_days=7";
        }

        internal static WeatherPlace Pick(string json, string asked)
        {
            WeatherPlaces found = Read<WeatherPlaces>(json);
            if (found == null || found.Results == null || found.Results.Count == 0) return null;
            string[] parts = (asked ?? "").Split(',');
            string hint = parts.Length > 1 ? string.Join(",", parts, 1, parts.Length - 1).Trim() : "";
            if (hint.Length > 0)
                foreach (WeatherPlace place in found.Results)
                    if (Same(place.Country, hint) || Same(place.Region, hint) || Same(place.Code, hint)) return place;
            return found.Results[0];
        }

        private static bool Same(string value, string hint)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string a = value.Trim().ToLowerInvariant(), b = hint.Trim().ToLowerInvariant();
            return a == b || a.StartsWith(b, StringComparison.Ordinal) || b == "usa" && a == "us" || b == "uk" && a == "gb";
        }

        private WeatherPlace Place(string name)
        {
            string key = Regex.Replace((name ?? "").Trim(), @"\s+", " ");
            if (key.Length == 0) return null;
            lock (gate)
            {
                WeatherPlace known;
                if (places.TryGetValue(key, out known)) return known;
            }
            string search = key.Split(',')[0].Trim();
            WeatherPlace place = Pick(Fetch(PlaceUrl(search)), key);
            if (place != null) lock (gate) places[key] = place;
            return place;
        }

        private WeatherReport Report(WeatherPlace place)
        {
            string key = place.Latitude.ToString("0.##", CultureInfo.InvariantCulture) + "," + place.Longitude.ToString("0.##", CultureInfo.InvariantCulture);
            DateTime now = Clock();
            lock (gate)
            {
                KeyValuePair<DateTime, WeatherReport> kept;
                if (reports.TryGetValue(key, out kept) && (now - kept.Key).TotalMinutes < Fresh && now >= kept.Key) return kept.Value;
            }
            WeatherReport report = Read<WeatherReport>(Fetch(ReportUrl(place)));
            if (report == null || report.Days == null || report.Days.Time == null || report.Days.Time.Length == 0) throw new InvalidDataException("Open-Meteo's forecast was empty.");
            lock (gate) reports[key] = new KeyValuePair<DateTime, WeatherReport>(now, report);
            return report;
        }

        internal static string Kind(int? code)
        {
            switch (code ?? -1)
            {
                case 0: return "clear";
                case 1: return "mostly clear";
                case 2: return "partly cloudy";
                case 3: return "overcast";
                case 45: case 48: return "foggy";
                case 51: case 53: return "drizzly";
                case 55: return "drizzling heavily";
                case 56: case 57: return "freezing drizzle";
                case 61: return "light rain";
                case 63: return "rain";
                case 65: return "heavy rain";
                case 66: case 67: return "freezing rain";
                case 71: return "light snow";
                case 73: return "snow";
                case 75: return "heavy snow";
                case 77: return "snow grains";
                case 80: return "light showers";
                case 81: return "showers";
                case 82: return "violent showers";
                case 85: return "snow showers";
                case 86: return "heavy snow showers";
                case 95: return "thunderstorms";
                case 96: case 99: return "thunderstorms with hail";
                default: return "unsettled";
            }
        }

        internal static bool Wet(int? code)
        {
            int c = code ?? -1;
            return c >= 51 && c <= 67 || c >= 80 && c <= 82 || c >= 95;
        }

        private static string Degrees(double? celsius, bool fahrenheit)
        {
            if (celsius == null) return "?";
            double value = fahrenheit ? celsius.Value * 9 / 5 + 32 : celsius.Value;
            return Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + (fahrenheit ? "°F" : "°C");
        }

        private static string Speed(double? kmh, bool fahrenheit)
        {
            double value = kmh ?? 0;
            return fahrenheit ? Math.Round(value / 1.609).ToString("0", CultureInfo.InvariantCulture) + " mph" : Math.Round(value).ToString("0", CultureInfo.InvariantCulture) + " km/h";
        }

        private static T At<T>(T[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] : default(T);
        }

        private static DateTime Date(string value)
        {
            DateTime date;
            DateTime.TryParseExact(value ?? "", new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
            return date;
        }

        private static string Hour(DateTime time)
        {
            int hour = time.Hour % 12 == 0 ? 12 : time.Hour % 12;
            return hour.ToString(CultureInfo.InvariantCulture) + (time.Hour < 12 ? "am" : "pm");
        }

        private static string DayName(WeatherReport report, int index)
        {
            if (index == 0) return "today";
            if (index == 1) return "tomorrow";
            return "on " + Date(At(report.Days.Time, index)).ToString("dddd", CultureInfo.InvariantCulture);
        }

        private static string Day(WeatherReport report, int index, bool fahrenheit)
        {
            WeatherDays days = report.Days;
            int? rain = At(days.Rain, index);
            return Kind(At(days.Code, index)) + ", " + Degrees(At(days.Low, index), fahrenheit) + " to " + Degrees(At(days.High, index), fahrenheit)
                + (rain == null ? "" : ", " + rain.Value.ToString(CultureInfo.InvariantCulture) + "% chance of rain");
        }

        internal static int Find(WeatherReport report, string when)
        {
            string asked = (when ?? "").Trim().ToLowerInvariant();
            if (asked.Length == 0 || asked == "today" || asked == "tonight" || asked == "now") return 0;
            if (asked == "tomorrow") return 1;
            for (int i = 0; i < report.Days.Time.Length; i++)
                if (asked.Contains(Date(report.Days.Time[i]).ToString("dddd", CultureInfo.InvariantCulture).ToLowerInvariant())) return i;
            return -1;
        }

        internal static string Tell(WeatherReport report, WeatherPlace place, ToolArguments args)
        {
            bool f = place.Fahrenheit;
            string where = place.Name;
            string action = (args.Action ?? "now").ToLowerInvariant();
            string when = (args.When ?? "").ToLowerInvariant();
            if (action == "week" || when.Contains("week"))
            {
                bool weekend = when.Contains("weekend");
                List<string> lines = new List<string>();
                for (int i = 0; i < report.Days.Time.Length; i++)
                {
                    DayOfWeek weekday = Date(report.Days.Time[i]).DayOfWeek;
                    if (weekend && weekday != DayOfWeek.Saturday && weekday != DayOfWeek.Sunday) continue;
                    lines.Add(Date(report.Days.Time[i]).ToString("ddd", CultureInfo.InvariantCulture) + ": " + Day(report, i, f));
                    if (weekend && lines.Count == 2) break;
                }
                return (weekend ? "This weekend in " : "The week in ") + where + ":\n" + string.Join("\n", lines.ToArray());
            }
            int index = Find(report, when);
            if (index < 0) return "I can only see the next 7 days for " + where + ". Ask about today, tomorrow or a day this week.";
            string day = DayName(report, index);
            if (action == "rain")
            {
                int? chance = At(report.Days.Rain, index);
                int? code = At(report.Days.Code, index);
                bool snow = code >= 71 && code <= 77 || code == 85 || code == 86;
                string umbrella = chance >= 50 ? (snow ? " Rug up, it's snow." : " Take an umbrella.") : chance >= 25 ? " Maybe pack an umbrella." : " No umbrella needed.";
                return (chance == null ? "I couldn't get a rain chance" : "There's a " + chance.Value.ToString(CultureInfo.InvariantCulture) + "% chance of rain") + " in " + where + " " + day + ", " + Kind(code) + "." + umbrella;
            }
            if (index == 0 && report.Now != null && report.Now.Temperature != null)
            {
                WeatherNow now = report.Now;
                string feels = now.Feels != null && Math.Abs(now.Feels.Value - now.Temperature.Value) >= 2 ? ", feels like " + Degrees(now.Feels, f) : "";
                return "It's " + Degrees(now.Temperature, f) + " and " + Kind(now.Code) + " in " + where + " right now" + feels + ", wind " + Speed(now.Wind, f) + ". Today: " + Day(report, 0, f) + ".";
            }
            return Capital(day) + " in " + where + ": " + Day(report, index, f) + ".";
        }

        private static string Capital(string text) { return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1); }

        internal static string Summary(WeatherReport report, WeatherPlace place)
        {
            return "Weather in " + place.Name + " " + DayName(report, 0) + ": " + Day(report, 0, place.Fahrenheit) + (report.Days.Time.Length > 1 ? "; tomorrow " + Day(report, 1, place.Fahrenheit) : "") + ".";
        }

        internal static string Extreme(WeatherReport report, WeatherPlace place)
        {
            WeatherDays d = report.Days;
            bool f = place.Fahrenheit;
            string where = place.Name;
            int code = At(d.Code, 0) ?? -1;
            double high = At(d.High, 0) ?? 0, low = At(d.Low, 0) ?? 0, gusts = At(d.Gusts, 0) ?? 0, fall = At(d.Fall, 0) ?? 0, snow = At(d.Snow, 0) ?? 0, uv = At(d.Uv, 0) ?? 0;
            if (code == 96 || code == 99) return "Weather warning for " + where + ": thunderstorms with hail today. Stay inside when it hits and keep your car under cover.";
            if (gusts >= 90) return "Weather warning for " + where + ": damaging winds today, gusts up to " + Speed(gusts, f) + ". Tie down anything loose.";
            if (high >= 40) return "Weather warning for " + where + ": extreme heat today, up to " + Degrees(high, f) + ". Drink water and stay out of the sun.";
            if (low <= -20) return "Weather warning for " + where + ": extreme cold today, down to " + Degrees(low, f) + ". Rug up properly if you go out.";
            if (code == 75 || code == 86 || snow >= 15) return "Weather warning for " + where + ": heavy snow today. Take it slow if you're going anywhere.";
            if (code == 65 || code == 82 || fall >= 40) return "Weather warning for " + where + ": heavy rain today. Watch for flooding on the roads.";
            if (code == 66 || code == 67) return "Weather warning for " + where + ": freezing rain today. Roads and paths will be icy.";
            if (code == 95) return "Weather warning for " + where + ": thunderstorms today. Stay inside when it hits.";
            if (uv >= 11) return "Weather warning for " + where + ": extreme UV today, " + Math.Round(uv).ToString("0", CultureInfo.InvariantCulture) + ". Sunscreen and a hat if you go out.";
            return null;
        }

        internal static string Umbrella(WeatherReport report, WeatherPlace place)
        {
            WeatherHours hours = report.Hours;
            if (hours == null || hours.Time == null) return null;
            DateTime from = report.Now != null ? Date(report.Now.Time) : DateTime.MinValue;
            if (from == DateTime.MinValue) return null;
            DateTime until = from.Date.AddHours(22);
            for (int i = 0; i < hours.Time.Length; i++)
            {
                DateTime time = Date(hours.Time[i]);
                if (time < from.AddMinutes(-59) || time > until) continue;
                int chance = At(hours.Rain, i) ?? 0;
                if (chance >= 60 && Wet(At(hours.Code, i)))
                    return "Take an umbrella if you head out, there's a " + chance.ToString(CultureInfo.InvariantCulture) + "% chance of rain around " + Hour(time) + " in " + place.Name + ".";
            }
            return null;
        }

        internal string Do(ToolArguments args)
        {
            args = args ?? new ToolArguments();
            string asked = (args.Name ?? "").Trim();
            if (!On()) return OffLine;
            string city = asked.Length > 0 ? asked : (City() ?? "").Trim();
            if (city.Length == 0) return NoCity;
            try
            {
                WeatherPlace place = Place(city);
                if (place == null) return "I couldn't find a place called " + city + " on Open-Meteo. Try the city with its country, like Paris, France.";
                WeatherReport report = Report(place);
                if (args.Action == "summary") return Summary(report, place);
                return Tell(report, place, args);
            }
            catch (Exception error)
            {
                Brain.Log("weather", -1, error.GetType().Name);
                return "I couldn't reach Open-Meteo for the weather just now. Try again in a minute.";
            }
        }

        internal void Forget() { lock (gate) { places.Clear(); reports.Clear(); checkedAt = DateTime.MinValue; pending = null; } }

        internal void Poll()
        {
            if (!On() || string.IsNullOrWhiteSpace(City())) return;
            DateTime now = Clock();
            if (now.Hour < 6 || now.Hour >= 21) return;
            lock (gate)
            {
                if (pending != null || (now - checkedAt).TotalMinutes < Every && now >= checkedAt) return;
                checkedAt = now;
            }
            if (Interlocked.Exchange(ref checking, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Look(); }
                catch (Exception error) { Brain.Log("weather", -1, error.GetType().Name); }
                finally { Interlocked.Exchange(ref checking, 0); }
            });
        }

        internal void Look()
        {
            string city = (City() ?? "").Trim();
            if (!On() || city.Length == 0) return;
            WeatherPlace place = Place(city);
            if (place == null) return;
            WeatherReport report = Report(place);
            string day = At(report.Days.Time, 0) ?? Clock().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string said = Said() ?? "";
            string warning = said.Contains(day + " warn") ? null : Extreme(report, place);
            string umbrella = warning != null || said.Contains(day + " umbrella") || said.Contains(day + " warn") ? null : Umbrella(report, place);
            lock (gate)
            {
                if (warning != null) { pending = warning; pendingKind = "warn"; pendingDay = day; }
                else if (umbrella != null) { pending = umbrella; pendingKind = "umbrella"; pendingDay = day; }
            }
        }

        internal bool Waiting { get { lock (gate) return pending != null; } }

        internal string Take()
        {
            lock (gate)
            {
                if (pending == null) return null;
                if (!On()) { pending = null; return null; }
                string line = pending, kind = pendingKind, day = pendingDay;
                pending = null;
                string said = Said() ?? "";
                List<string> kept = new List<string>();
                foreach (string part in said.Split(';'))
                    if (part.StartsWith(day + " ", StringComparison.Ordinal)) kept.Add(part);
                kept.Add(day + " " + kind);
                Saying(string.Join(";", kept.ToArray()));
                return line;
            }
        }

        private const string Topic = @"weather|forecast|temperature|temp outside|rain(?:ing|y)?|snow(?:ing)?|umbrella|storms?|stormy|thunder(?:storms?)?|hail|sunny|windy|hot|cold|warm|chilly|freezing|humid";
        private const string When = @"today|tonight|tomorrow|this week(?:end)?|the week(?:end)?|next week|(?:on |this |next )?(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)|this (?:morning|afternoon|evening)";
        private static readonly Regex Shape = new Regex(@"^(?:what(?:'?s| is| will| does)|how(?:'?s| is| hot| cold| warm| will)|is it|will it|is there|are there|does it|do i|should i|am i|gonna|any|tell me|give me|check|get|show me|what about|weather|forecast|temperature|chance of)\b", RegexOptions.IgnoreCase);
        private static readonly Regex Spoken = new Regex(@"\b(?:" + Topic + @")\b", RegexOptions.IgnoreCase);
        private static readonly Regex Subject = new Regex(@"\b(?:weather|forecast|temperature|umbrella)\b|\bit\b|\bchance of\b|\bany (?:rain|snow|storms?)\b|\b(?:rain|snow|storm)(?:ing)? (?:today|tonight|tomorrow|this|on|later)\b", RegexOptions.IgnoreCase);
        private static readonly Regex DayWords = new Regex(@"\b(" + When + @")\b", RegexOptions.IgnoreCase);
        private static readonly Regex Where = new Regex(@"\b(?:in|for|at|over in|out in)\s+([\p{L}][\p{L}\p{M} .'\-]*(?:,\s*[\p{L}][\p{L}\p{M} .'\-]*)?)$", RegexOptions.IgnoreCase);
        private static readonly Regex NotPlace = new Regex(@"^(?:the |a |an |my |your |here|there|outside|me|us|it|this|that|today|tonight|tomorrow|now|later|the (?:morning|afternoon|evening|week|weekend)|a (?:bit|while)|" + When + @")\b", RegexOptions.IgnoreCase);
        private static readonly Regex Elsewhere = new Regex(@"\b(?:in (?:minecraft|fortnite|roblox|game|the game|gta|the sun|space)|of (?:the|my|your|a|an)\b|cpu|gpu|pc|computer|laptop|phone|water|oven|food|coffee|tea|shower|bath|room temp|my room|movie|song|music|video|news|stocks?|game)\b", RegexOptions.IgnoreCase);

        internal static ToolArguments Request(string said)
        {
            string text = Regex.Replace((said ?? "").Replace('’', '\'').Trim(), @"[\s?!.]+$", "");
            text = Regex.Replace(text, @"^(?:(?:hey|yo|ok|okay)[\s,]+)?(?:capri[\s,]+)?", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+(?:capri|please|pls|right now|rn)$", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+", " ");
            if (text.Length == 0 || text.Length > 90) return null;
            if (!Shape.IsMatch(text) || !Spoken.IsMatch(text) || !Subject.IsMatch(text) || Elsewhere.IsMatch(text)) return null;
            if (Regex.IsMatch(text, @"\b(?:and|then|also)\b\s+(?!(?:is it|will it|will there|what about|how about|any|tomorrow|today|tonight)\b)", RegexOptions.IgnoreCase)) return null;
            if (Regex.IsMatch(text, @"^(?:what|how)(?:'?s| is) (?:the )?(?:weather|forecast) (?:app|website|site|channel|widget)\b|\b(?:open|launch|search|google|play)\b", RegexOptions.IgnoreCase)) return null;
            ToolArguments args = new ToolArguments();
            Match day = DayWords.Match(text);
            if (day.Success) args.When = Regex.Replace(day.Groups[1].Value.ToLowerInvariant(), @"^(?:on|this|next) (?=\w+day$)", "");
            string rest = day.Success ? (text.Substring(0, day.Index) + " " + text.Substring(day.Index + day.Length)).Trim() : text;
            rest = Regex.Replace(Regex.Replace(rest, @"\s+(?:like|looking|be like|gonna be|going to be|for)$", "", RegexOptions.IgnoreCase), @"\s+", " ").Trim(' ', ',');
            Match where = Where.Match(rest);
            if (where.Success)
            {
                string place = Regex.Replace(where.Groups[1].Value, @"\s+(?:like|looking|be like|gonna be|going to be|today|tonight|tomorrow|now)$", "", RegexOptions.IgnoreCase).Trim(' ', ',', '.');
                if (place.Length > 1 && place.Length <= 48 && !NotPlace.IsMatch(place)) args.Name = place;
            }
            bool wet = Regex.IsMatch(text, @"\b(?:rain(?:ing|y)?|umbrella|snow(?:ing)?|storms?|stormy|hail|wet)\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(text, @"\bweather\b", RegexOptions.IgnoreCase);
            bool week = args.When != null && args.When.Contains("week") || args.When == null && Regex.IsMatch(text, @"\bforecast\b", RegexOptions.IgnoreCase);
            args.Action = wet ? "rain" : week ? "week" : args.When != null && args.When != "today" && args.When != "tonight" ? "day" : "now";
            if (week && args.When == null) args.When = "this week";
            return args;
        }

        internal static string Check()
        {
            const string place = "{\"results\":[{\"name\":\"Springfield\",\"latitude\":39.8,\"longitude\":-89.64,\"country\":\"United States\",\"country_code\":\"US\",\"admin1\":\"Illinois\"},{\"name\":\"Sydney\",\"latitude\":-33.87,\"longitude\":151.21,\"country\":\"Australia\",\"country_code\":\"AU\",\"admin1\":\"New South Wales\"}],\"generationtime_ms\":0.5}";
            const string calm = "{\"current\":{\"time\":\"2026-11-16T09:00\",\"temperature_2m\":21.4,\"apparent_temperature\":18.9,\"weather_code\":2,\"wind_speed_10m\":14.0},"
                + "\"hourly\":{\"time\":[\"2026-11-16T08:00\",\"2026-11-16T09:00\",\"2026-11-16T15:00\",\"2026-11-16T23:00\"],\"precipitation_probability\":[0,10,70,90],\"weather_code\":[2,2,61,63]},"
                + "\"daily\":{\"time\":[\"2026-11-16\",\"2026-11-17\",\"2026-11-18\",\"2026-11-19\",\"2026-11-20\",\"2026-11-21\",\"2026-11-22\"],\"weather_code\":[61,0,3,null,95,1,2],\"temperature_2m_max\":[24.0,26.2,null,22,23,25,27],\"temperature_2m_min\":[16.0,15.1,14,13,12,15,16],"
                + "\"precipitation_probability_max\":[70,5,20,null,80,10,0],\"precipitation_sum\":[3.2,0,0,0,12,0,0],\"snowfall_sum\":[0,0,0,0,0,0,0],\"wind_gusts_10m_max\":[30,20,25,40,70,20,10],\"uv_index_max\":[7,9,5,6,4,8,9]}}";
            string wild = calm.Replace("\"wind_gusts_10m_max\":[30,", "\"wind_gusts_10m_max\":[95,");
            List<string> urls = new List<string>();
            int fetches = 0;
            string body = calm;
            string said = null;
            bool on = true;
            DateTime clock = new DateTime(2026, 11, 16, 9, 5, 0);
            Weather weather = new Weather();
            weather.On = delegate { return on; };
            weather.City = delegate { return "Sydney, Australia"; };
            weather.Said = delegate { return said; };
            weather.Saying = delegate(string value) { said = value; };
            weather.Clock = delegate { return clock; };
            weather.Fetch = delegate(string url) { urls.Add(url); fetches++; return url.StartsWith(Geocoder, StringComparison.Ordinal) ? place : body; };
            string[,] routes =
            {
                { "what's the weather", "now", null, null }, { "Capri, what's the weather like tomorrow?", "day", "tomorrow", null },
                { "how's the weather in Paris, France", "now", null, "Paris, France" }, { "will it rain today", "rain", "today", null },
                { "do I need an umbrella tomorrow", "rain", "tomorrow", null }, { "what's the forecast for this weekend", "week", "this weekend", null },
                { "what's the forecast", "week", "this week", null }, { "is it cold in Melbourne", "now", null, "Melbourne" },
                { "what's the weather on friday in Tokyo", "day", "friday", "Tokyo" }, { "how hot is it outside", "now", null, null },
                { "is it going to snow in Denver this weekend", "rain", "this weekend", "Denver" }
            };
            for (int i = 0; i < routes.GetLength(0); i++)
            {
                ToolArguments heard = Request(routes[i, 0]);
                if (heard == null || heard.Action != routes[i, 1] || heard.When != routes[i, 2] || heard.Name != routes[i, 3])
                    return "Weather heard \"" + routes[i, 0] + "\" as " + (heard == null ? "nothing" : heard.Action + "/" + heard.When + "/" + heard.Name);
            }
            foreach (string not in new[] { "i love rainy weather", "open the weather app", "how hot is my cpu", "what's the temperature of the sun", "google weather tomorrow", "it's so cold in here lol", "what's the weather in minecraft", "what's the weather in Sydney and open spotify", "is it raining in the game", "the weather was nice yesterday" })
                if (Request(not) != null) return "Weather took \"" + not + "\".";
            string now = weather.Do(new ToolArguments { Action = "now" });
            if (now != "It's 21°C and partly cloudy in Sydney right now, feels like 19°C, wind 14 km/h. Today: light rain, 16°C to 24°C, 70% chance of rain.") return "Weather's now line was " + now;
            if (urls.Count != 2 || !urls[0].StartsWith(Geocoder + "?name=Sydney&", StringComparison.Ordinal) || !urls[1].Contains("latitude=-33.87&longitude=151.21") || !urls[1].Contains("timezone=auto")) return "Weather asked Open-Meteo wrong: " + string.Join(" ", urls.ToArray());
            if (urls.Exists(url => Regex.IsMatch(url, @"geoip|ipapi|ipinfo|ip-api|[?&](?:ip|apikey|key)=", RegexOptions.IgnoreCase))) return "Weather looked them up by IP or sent a key.";
            weather.Do(new ToolArguments { Action = "day", When = "tomorrow" });
            if (fetches != 2) return "Weather didn't keep its forecast for a few minutes.";
            string tomorrow = weather.Do(new ToolArguments { Action = "day", When = "tomorrow" });
            if (tomorrow != "Tomorrow in Sydney: clear, 15°C to 26°C, 5% chance of rain.") return "Weather's tomorrow line was " + tomorrow;
            string gaps = weather.Do(new ToolArguments { Action = "day", When = "thursday" });
            if (gaps != "On Thursday in Sydney: unsettled, 13°C to 22°C.") return "Weather's line with missing values was " + gaps;
            string rain = weather.Do(new ToolArguments { Action = "rain", When = "today" });
            if (rain != "There's a 70% chance of rain in Sydney today, light rain. Take an umbrella.") return "Weather's rain line was " + rain;
            string weekend = weather.Do(new ToolArguments { Action = "week", When = "this weekend" });
            if (weekend != "This weekend in Sydney:\nSat: mostly clear, 15°C to 25°C, 10% chance of rain\nSun: partly cloudy, 16°C to 27°C, 0% chance of rain") return "Weather's weekend was " + weekend;
            string far = weather.Do(new ToolArguments { Action = "day", When = "christmas" });
            if (!far.StartsWith("I can only see the next 7 days", StringComparison.Ordinal)) return "Weather guessed past its forecast: " + far;
            weather.Do(new ToolArguments { Name = "Springfield" });
            string us = weather.Do(new ToolArguments { Name = "Springfield", Action = "now" });
            if (!us.StartsWith("It's 71°F and partly cloudy in Springfield", StringComparison.Ordinal) || !us.Contains("wind 9 mph")) return "Weather didn't use Fahrenheit in the US: " + us;
            weather.Look();
            if (!weather.Waiting) return "Weather missed the umbrella reminder.";
            string umbrella = weather.Take();
            if (umbrella != "Take an umbrella if you head out, there's a 70% chance of rain around 3pm in Sydney." || said != "2026-11-16 umbrella") return "Weather's umbrella line was " + umbrella + " / " + said;
            weather.Look();
            if (weather.Waiting) return "Weather said the umbrella twice in a day.";
            body = wild;
            weather.Forget();
            weather.Look();
            string warning = weather.Take();
            if (warning != "Weather warning for Sydney: damaging winds today, gusts up to 95 km/h. Tie down anything loose." || said != "2026-11-16 umbrella;2026-11-16 warn") return "Weather's warning was " + warning + " / " + said;
            weather.Forget();
            weather.Look();
            if (weather.Waiting) return "Weather warned twice in a day.";
            said = "2026-11-15 warn;2026-11-15 umbrella";
            weather.Forget();
            weather.Look();
            if (weather.Take() == null || said != "2026-11-16 warn") return "Weather kept yesterday's warnings: " + said;
            on = false;
            if (weather.Do(new ToolArguments()) != OffLine) return "Weather answered while it was off.";
            int before = fetches;
            weather.Forget();
            weather.Poll();
            weather.Look();
            if (fetches != before || weather.Waiting) return "Weather fetched while it was off.";
            on = true;
            weather.City = delegate { return ""; };
            if (weather.Do(new ToolArguments()) != NoCity) return "Weather guessed a city.";
            weather.City = delegate { return "Nowhere"; };
            weather.Fetch = delegate(string url) { return "{\"generationtime_ms\":0.1}"; };
            if (!weather.Do(new ToolArguments()).StartsWith("I couldn't find a place called Nowhere", StringComparison.Ordinal)) return "Weather didn't say it couldn't find a city.";
            weather.City = delegate { return "Sydney"; };
            weather.Fetch = delegate(string url) { throw new WebException("offline"); };
            if (weather.Do(new ToolArguments()) != "I couldn't reach Open-Meteo for the weather just now. Try again in a minute.") return "Weather didn't say Open-Meteo was down.";
            if (Pick(place, "Sydney, Australia").Name != "Sydney" || Pick(place, "Springfield").Name != "Springfield" || Pick("{}", "x") != null) return "Weather picked the wrong place.";
            return null;
        }
    }
}
