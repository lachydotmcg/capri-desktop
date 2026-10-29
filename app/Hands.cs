using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
#if !CROSS
using System.Windows.Automation;
#endif

namespace CapriDesktop
{
    [DataContract]
    internal sealed class ToolArguments
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "site")] public string Site;
        [DataMember(Name = "search")] public string Search;
        [DataMember(Name = "action")] public string Action;
        [DataMember(Name = "focus")] public string Focus;
        [DataMember(Name = "song")] public string Song;
        [DataMember(Name = "app")] public string App;
        [DataMember(Name = "kind")] public string Kind;
        [DataMember(Name = "task")] public string Task;
        internal string Asked;
        [DataMember(Name = "target")] public string Target;
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "keys")] public string Keys;
        [DataMember(Name = "button")] public string Button;
        [DataMember(Name = "enter")] public string Enter;
        [DataMember(Name = "direction")] public string Direction;
        [DataMember(Name = "seconds")] public string Seconds;
        [DataMember(Name = "minutes")] public string Minutes;
        [DataMember(Name = "level")] public string Level;
        [DataMember(Name = "coordinate")] public double[] Coordinate;
        [DataMember(Name = "start_coordinate")] public double[] From;
        [DataMember(Name = "region")] public double[] Region;
        [DataMember(Name = "scroll_direction")] public string Way;
        [DataMember(Name = "scroll_amount")] public double? Amount;
        [DataMember(Name = "duration")] public double? Duration;
        [DataMember(Name = "repeat")] public double? Repeat;

        internal ToolArguments Copy() { return (ToolArguments)MemberwiseClone(); }
    }

    internal sealed class InstalledApp
    {
        internal readonly string Name;
        internal readonly string Id;
        internal InstalledApp(string name, string id) { Name = name; Id = id; }
    }

    internal sealed class OpenWindow
    {
        internal readonly IntPtr Handle;
        internal readonly string Title;
        internal readonly string Program;
        internal OpenWindow(IntPtr handle, string title, string program) { Handle = handle; Title = title; Program = program; }
    }

    internal static class Hands
    {
        internal const string Note =
            "\nYou have tools for their PC: open (launch, play) their apps and games, switch to, minimize or close apps that are open, play songs or videos they name, open websites or search them, control their music and volume, look at their screen, and use their mouse and keyboard for jobs inside apps and sites. " +
            "Use a tool whenever they ask you to do something like that, and never say you did or are doing it without one. Look at their screen when they ask what you see or what you think of something on it. Only close an app when they ask you to. " +
            "Your own website, where people download you, is " + Home + ". " +
            "They've already given you permission for anything they ask you to do on their PC, so do all of it straight away: never ask which option they meant, never ask if they're sure, never make them confirm or ask for things one at a time, and when they leave the choice to you, like which song, video or game, pick one yourself. " +
            "You can do anything on their PC they ask: send messages and DMs, reply, post, join, leave or end calls, and any other job in their apps with use_computer. Never ask if they want you to, never read a message back to check it first, and never tell them to do it themselves. Mute my mic means mute their mic, not deafen them. Never say you can't do something your tools can do. " +
            "You can press shortcuts: mute or deafen them in Discord, screenshots, clips, recording, Game Bar, show desktop, Task Manager, clipboard history, emoji, voice typing, Live Captions, Narrator, Magnifier, colour filters and Quick Settings. " +
            "You can change Windows settings too, like Bluetooth, Wi-Fi, airplane mode, hotspot, Energy Saver and brightness, with use_computer. " +
            "You can shut down, restart, sleep, lock or sign out of their PC with power: when they ask, do it straight away and never ask if they're sure. " +
            "When they tell you to go to sleep, take a nap or wake up, that's you: use nap, never power. " +
            "When they ask you to remember or forget something about them, or to leave or make a memory, use remember: it saves to your notes, me.md, which you read every chat. Never say you can't keep memories. " +
            "You have a web browser: when they ask something you don't know or that changes, like the weather, news, scores, prices or times, search Google for it instead of guessing or saying you can't. " +
            "Most messages are just chat, not jobs: answer those as yourself, with your own opinions and tastes, and don't offer to open, play or look at anything unless they ask.";

        internal const string Prompt =
            "You are the hands of Capri, a desktop companion on the user's Windows PC. You don't chat.\n" +
            "Act only on their latest request. Earlier messages are context for names, not commands to repeat. Call one tool for each thing they ask for, however many there are (open Discord, Spotify and Steam is three open_app calls): open an app, game or website, search a site, play music, control media or volume, check the current song, switch to, minimize or close an app, or look at their screen when asked about it. Use the names they gave.\n" +
            "If the job needs clicking, typing or more than one step, like opening an app and then searching, going somewhere or making something in it, call use_computer once with the whole job in their words.\n" +
            "Sending a message, DM, reply or post, joining, leaving or ending a call, and any other job inside an app are use_computer jobs: pass their exact words, including what to send. They asked, so it's a job even when it sends, deletes or ends something.\n" +
            "When they name an app, never guess a website for it: use open_app, or use_computer if there's more to do.\n" +
            "If they leave the pick to you, like a song or a video, pick one yourself that fits.\n" +
            "Your own website is " + Home + ": when they ask for your website or page, call open_website with that address.\n" +
            "If they ask something you'd have to look up, like the weather, news, scores, prices, times or facts you're unsure of, call open_website with site google and their question as the search.\n" +
            "To mute, unmute or turn off their mic, call shortcut with discord_mute; to deafen or undeafen them, discord_deafen. Don't ask which: mute means the mic. Screenshots, clips, recording, Game Bar, show desktop, Task Manager, clipboard, emoji, voice typing, Live Captions, Narrator, Magnifier (magnifier_off closes it), colour filters and Quick Settings are shortcuts too.\n" +
            "To turn Bluetooth, Wi-Fi, airplane mode, hotspot, Nearby sharing, Energy Saver or night light on or off, or change the brightness, call use_computer with the job in their words.\n" +
            "To shut down, turn off, restart, sleep, lock or sign out of their PC, call power. They asked, so do it: never ask if they're sure.\n" +
            "When they tell you yourself to go to sleep, take a nap, go to bed or wake up, call nap: that's Capri, not their PC.\n" +
            "Never reply with a question about whether to do what they asked. Call the tool.\n" +
            "If they ask you to remember, note down or forget something about them, or to leave, make or save a memory, call remember with a short note about them in the third person.\n" +
            "Questions about their current screen or song need a tool too. If the target is unclear, pick the closest match yourself. For chat, negated or hypothetical requests, call no tool and reply with only the word: chat";

        internal const string Chat =
            "\nNo tools ran for this message. Answer as yourself, with your own opinions and tastes. Don't claim you opened, played or looked at anything. You keep notes about them in me.md, saved whenever they ask you to remember something, so never say you can't remember things or keep memories; if they asked you to and nothing was saved, ask what they want you to remember. You can search Google for them: if they ask something you don't know or that changes, like the weather, news, scores or prices, say you'll look it up instead of guessing or saying you can't. If they asked you to do something on their PC, like send a message or end a call, never ask whether to: say On it and what you're doing, like On it, sending it now.";

        private static readonly string[,] Specs =
        {
            { "open_app", "Open, launch or play an app or game installed on their PC, like Spotify, Discord, Steam or Roblox.",
              "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The app's name\"}},\"required\":[\"name\"]}" },
            { "open_website", "Open a website in their browser, or search a site like YouTube or Google.",
              "{\"type\":\"object\",\"properties\":{\"site\":{\"type\":\"string\",\"description\":\"The site's name or address, like youtube.com\"},\"search\":{\"type\":\"string\",\"description\":\"Words to search for on that site, if any\"}},\"required\":[\"site\"]}" },
            { "media", "Control the music or video playing on their PC, the PC's volume (up, down, set to a level, mute or unmute), or check what song is playing.",
              "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"What to do\",\"enum\":[\"play_pause\",\"next\",\"previous\",\"volume_up\",\"volume_down\",\"volume_set\",\"mute\",\"unmute\",\"now_playing\"]},\"level\":{\"type\":\"string\",\"description\":\"For volume_set, the volume from 0 to 100. For volume_up or volume_down, how many percent to change it, only if they said\"}},\"required\":[\"action\"]}" },
            { "look_at_screen", "Take a screenshot of their screen and look at it right now: what they're doing, playing or showing you. Use it whenever they ask you to check, look at or see their screen, or ask what something on it is. You can always look, so never say you can't see their screen.",
              "{\"type\":\"object\",\"properties\":{\"focus\":{\"type\":\"string\",\"description\":\"What to look at, if they said\"}}}" },
            { "play_music", "Play a song, playlist, album or artist they name, on Spotify or YouTube. For their liked songs, use kind playlist and song Liked Songs. If they leave the pick to you, pick a track from the Cyberpunk 2077 or Edgerunners soundtrack.",
              "{\"type\":\"object\",\"properties\":{\"song\":{\"type\":\"string\",\"description\":\"What to play, like a song and its artist or a playlist's name\"},\"kind\":{\"type\":\"string\",\"description\":\"What it is\",\"enum\":[\"song\",\"playlist\",\"album\",\"artist\"]},\"app\":{\"type\":\"string\",\"description\":\"Where to play it, if they said\",\"enum\":[\"spotify\",\"youtube\"]}},\"required\":[\"song\"]}" },
            { "app_window", "Switch to, minimize or close an app or game that's open on their PC. Naming a site closes that browser tab.",
              "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"The app's name, or the site in a browser tab\"},\"action\":{\"type\":\"string\",\"description\":\"What to do\",\"enum\":[\"switch_to\",\"minimize\",\"close\"]}},\"required\":[\"name\",\"action\"]}" },
            { "use_computer", "Use their mouse and keyboard for any job inside an app or website: sending a message or DM, joining, leaving or ending a call, or anything that needs clicking, typing or several steps, like opening an app and searching in it.",
              "{\"type\":\"object\",\"properties\":{\"task\":{\"type\":\"string\",\"description\":\"The whole job in their exact words, including any message to send\"}},\"required\":[\"task\"]}" },
            { "shortcut", "Press a keyboard shortcut for them: mute or unmute their mic in Discord (discord_mute), deafen or undeafen in Discord (discord_deafen), take a screenshot, clip the last 30 seconds, start or stop recording, open the Game Bar, show the desktop, open Task Manager, clipboard history or the emoji picker, start voice typing, turn Live Captions, Narrator or colour filters on or off, zoom in with Magnifier or close it, or open Quick Settings.",
              "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\",\"description\":\"Which shortcut\",\"enum\":[\"discord_mute\",\"discord_deafen\",\"screenshot\",\"clip_that\",\"record\",\"game_bar\",\"show_desktop\",\"task_manager\",\"clipboard\",\"emoji\",\"voice_typing\",\"live_captions\",\"narrator\",\"magnifier\",\"magnifier_off\",\"color_filters\",\"quick_settings\"]}},\"required\":[\"name\"]}" },
            { "remember", "Save a short note about them to your notes (me.md) so you remember it next time, like a favourite game or what they're making, or forget a note. Use it whenever they ask you to remember, note down, save or forget something, or to leave or make a memory.",
              "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"description\":\"The note, about them in the third person, or what to forget\"},\"action\":{\"type\":\"string\",\"description\":\"What to do\",\"enum\":[\"remember\",\"forget\"]}},\"required\":[\"text\"]}" },
            { "power", "Shut down, restart, sleep, lock or sign out of their PC, or cancel a shutdown. Do it as soon as they ask, without asking if they're sure. \"Go to sleep\" or \"take a nap\" said to you is about you, not their PC: use nap.",
              "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"What to do\",\"enum\":[\"shut_down\",\"restart\",\"sleep\",\"lock\",\"sign_out\",\"cancel\"]},\"minutes\":{\"type\":\"string\",\"description\":\"How many minutes from now, only if they said when\"}},\"required\":[\"action\"]}" },
            { "nap", "Go to sleep yourself on their screen, or wake up: your own nap, not their PC's. Use it when they tell you to go to sleep, take a nap, go to bed or wake up. You stay asleep until they click you or talk to you.",
              "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"What to do\",\"enum\":[\"sleep\",\"wake\"]}},\"required\":[\"action\"]}" }
        };

        internal static readonly string Tools = Render(false);
        internal static readonly string ClaudeTools = Render(true);
        internal static readonly string Listing = Written();
        private const string LookupWords = @"^(what('?s| is| are)|how('?s| is)|is it|will it|when('?s| is| does| do)|who won|did|check|get|tell me|show me|find)\b.*\b(weather|forecast|temperature|raining|snowing|news|headlines|scores?|stock price|price of|exchange rate|time (is it )?in|opening hours|open (today|now|tonight)|release date|showtimes|rain|snow|sunny|hot out|cold out)\b|^(weather|forecast|news|headlines)\b|^who (won|is winning)\b";
        // Burmese asks with ပေး (for me), လိုက် (go ahead) or ပါ (please) after the verb, and ends a sentence with "။".
        private const string BurmeseAsk = @"(လိုက်|ပေး|စမ်း|ပါ)+(ဦး|ဥ\u102Eး)?(နော်)?[။၊!?.\s]*$";

        private static readonly Regex Asks = new Regex(
            @"^(open|launch|start|run|load up|boot up|fire up|play|shuffle|put on|blast|queue|close|quit|exit|kill|shut down|switch (back |over )?to|back to|go to|bring up|pull up|show me|minimi[sz]e|pause|unpause|resume|stop the music|skip|next|previous|go back|volume|louder|quieter|mute|unmute|search|google|look up|mind (opening|launching|playing|closing))\b" +
            @"|^(turn|make) (it|this|that|the volume|the music|the sound) (up|down|louder|quieter)\b|^(turn (up|down)|lower|raise) (the )?(volume|music|sound)\b|^put (some |a )?(music|song|tunes) on\b|^(get|bring) .+ (going|up)$" +
            @"|^(look at|take a (look|peek)( at)?|check out|check|rate|watch) (this|that|it|my|the|what)\b" +
            @"|^(click|tap|double click|right click|type|press|scroll|go on|get on|hop on|sign in|log in|fill (in|out)|use (my|the) (mouse|keyboard|computer|pc))\b|^(use|make|create|write|draw|search|find) .+ (in|on|with|using|into) (the |my )?\w+" +
            @"|^(shut ?down|turn off|power (off|down)|switch off|restart|reboot|lock|sleep|hibernate|put|send) (my |the |this )?(pc|computer|laptop|system|machine|rig|windows|device|screen)\b|^(shut|turn|power|switch) (my |the |this )?(pc|computer|laptop|system|machine|rig|device) (down|off)\b|^(sign|log) me (out|off)$" +
            @"|^(send|dm|pm|message|text|ping|reply to|respond to|post|tweet)\b|^(tell|ask|say|let|invite|kick|ban|add|react|call|ring|share|pin|block|unblock|unfriend) .+\b(on|in|into|through|via|over) (the |my )?(discord|slack|teams|whatsapp|telegram|signal|messenger|skype|instagram|insta|snapchat|snap|reddit|twitter|tiktok|facebook|roblox|steam|chat|call|vc|server|channel|group chat|gc|dms?)\b" +
            @"|^(end|leave|hang up|disconnect( from)?|join|accept|decline|pick up|answer|reject|hop (in|on|off)|get off) (the |my |this |that |a |our |their )?(\w+('s)? )?(call|vc|voice (chat|channel|call)|video call|meeting|stream|server|channel)\b|^(hang up|disconnect|hop off|get off)( (the )?(call|vc))?$" +
            @"|^(see|tell me|show me) (what|which)\b|^see (this|that|my screen|the screen)\b|^what('?s| is) (on my screen|on the screen|playing|this song|this track|the song|on spotify)\b" +
            @"|^what ((song|track)( is|'s)? (this|playing|on)|am i (doing|playing|watching|listening)|are we (doing|playing|watching|listening)|do you see|can you see)\b|^what do you think (of|about) (this|that|it|my (build|skin|setup))\b|^do you (see|like) (this|that|my (build|skin|setup))\b" +
            @"|^(i (want|need|wanna|would like) (you to |to )?(open|launch|play|listen|watch|search|remember|forget|mute|unmute|deafen)|^(deafen|undeafen|keep in mind|make a note)|let's (play|listen|watch)|find (me )?.+ (on|online)|can (you|u) see)\b" +
            "|" + LookupWords +
            @"|^(ouvre|ouvrir|lancer?|d[ée]marrer?|fermer?|quitter?|r[ée]duis|r[ée]duire|minimiser?|chercher?|rechercher?|mets|mettre|jouer?|passer?|couper?|augmenter?|baisser?|regarder?|afficher?|montre-moi|va sur|aller sur|retourne sur)\b" +
            @"|^(tu vois|qu'est-ce que (tu vois|je fais)|c'est quoi (cette|la) (chanson|musique)|quelle (chanson|musique)|qu'est-ce qu'il y a sur mon [ée]cran)\b" +
            // Spanish: "abre Spotify", "pon Bohemian Rhapsody", "¿me puedes buscar...?" go to her hands too, instead of a chat reply saying she can't.
            @"|^(abr[aeií]r?|[aá]bre(me|lo|la)|inicia(r|lo)?|inicie|lanza(r)?|ejecuta(r|lo)?|arranca(r)?|enciende|prende|activa(r)?|desactiva(r)?|apaga(r)?|cierra|ci[eé]rra(me|lo|la)|cerrar|minimiza(r|lo)?|busca(r)?|b[uú]sca(me|lo)|pon(me|lo|la|le|er)?|reproduce|reproducir|reprod[uú]ce(me|lo)|toca(r)?|t[oó]ca(me|la)|pausa|reanuda|salta|silencia|quita(le)?|sube|s[uú]bele|subir|baja|b[aá]jale|bajar|mu[eé]stra(me)?|ens[eé][ñn]a(me)?|escribe|escr[ií]be(le|me)|manda|m[aá]nda(le|me)|env[ií]a(le|me)?|responde|contesta|ve al?|vete al?|ir al?|entra (al?|en)|haz clic|dale clic|pulsa|presiona|googlea|quiero que (me )?(abras|pongas|reproduzcas|toques|busques|cierres|inicies|lances|mandes|env[ií]es|escribas)|(me )?(abres|pones|pondr[ií]as|abrir[ií]as|buscas|cierras|quitas|subes|bajas|pasas))\b" +
            @"|^(ves|qu[eé] (ves|hay en (mi|la) pantalla|estoy haciendo|est[aá] sonando)|mira (mi pantalla|esto|eso)|qu[eé] (canci[oó]n|m[uú]sica|tema) (es|suena)|(cu[aá]l es|c[oó]mo se llama) (esta|esa) canci[oó]n)\b" +
            // A new install talks in Windows' language, so German, Portuguese, Italian, Polish, Dutch and Russian requests reach her hands too.
            @"|^(([öo]ffne|[öo]ffnen|starte|spiele?|schlie(ß|ss)e?|suche?|zeig(e|\s+mir)?|pausiere|schalte)\b|mach(e)? .+ (auf|an|aus|lauter|leiser)$|(kannst|k[öo]nntest|w[üu]rdest) du\b.*\b([öo]ffnen|starten|spielen|abspielen|schlie(ß|ss)en|suchen|zeigen|anmachen|aufmachen|ausmachen|pausieren)\b)" +
            @"|^(abra|abrir|toque|tocar|coloca|coloque|p[õo]e|bota|fecha|feche|fechar|procura|procure|pesquisa|pesquise|mostra|mostre|pula|pule|aumenta|diminui|(voc[eê] |vc )?(pode|poderia|consegue)s? (me )?(abrir|tocar|colocar|p[ôo]r|fechar|procurar|pesquisar|mostrar|iniciar|pausar))\b" +
            @"|^(apri|aprire|avvia|avviare|lancia|metti|mettimi|riproduci|suona|chiudi|chiudere|cerca|cercare|mostrami|fammi (vedere|sentire)|alza|abbassa|(puoi|potresti|riesci a) (aprire|avviare|mettere|riprodurre|chiudere|cercare|mostrarmi|suonare|alzare|abbassare))\b" +
            @"|^(otw[óo]rz|uruchom|w[łl][aą]cz|pu[śs][ćc]|zamknij|wyszukaj|szukaj|znajd[źz]|poka[żz]|zatrzymaj|(mo[żz]esz|m[óo]g[łl]by[śs]|mog[łl]aby[śs]) (mi )?(otworzy[ćc]|uruchomi[ćc]|w[łl][aą]czy[ćc]|pu[śs]ci[ćc]|zamkn[aą][ćc]|wyszuka[ćc]|znale[źz][ćc]|pokaza[ćc]))(?!\w)" +
            @"|^(((speel|sluit|zoek|toon|pauzeer)\b(?! (je|jij|jy|julle|u|we|wij|ons)\b)|laat (me )?zien\b)|zet .+ op$|(kun|kan|wil) je\b.*\b(openen|starten|spelen|afspelen|opzetten|sluiten|zoeken|laten zien)\b)" +
            @"|^(открой|открыть|запусти|запустить|включи|включить|поставь|поставить|закрой|закрыть|найди|найти|покажи|показать|выключи|сделай (громче|тише)|(можешь|можете|мог бы|могла бы|не могла бы) (ты )?(открыть|запустить|включить|поставить|закрыть|найти|показать))(?!\w)" +
            // Chinese, Japanese, Korean, Thai, Vietnamese, Indonesian, Turkish, Ukrainian, Arabic and Hindi too. Chinese and Thai write
            // no spaces, and Japanese, Korean, Turkish and Hindi put the verb last, so those match where the verb sits.
            @"|^(请|請|麻烦你?|麻煩你?)?(你|您)?(能不能|可不可以|能|可以|会不会|會不會)?(帮我|幫我|给我|給我|替我)?(打开|打開|开启|開啟|启动|啟動|运行|運行|播放|放(一首|首|点|點|些|个|個|歌|音乐|音樂)|来一?首|來一?首|关闭|關閉|关掉|關掉|退出|暂停|暫停|继续播放|繼續播放|下一首|上一首|跳过|跳過|切换到|切換到|搜索|搜尋|搜一下|查一下|找一下|调高|調高|调低|調低|调大|調大|调小|調小|静音|靜音|最小化|关机|關機|重启|重啟|发消息|發消息|把(音量|声音|聲音))" +
            @"|^(看看|看一下)(我的)?(屏幕|螢幕|画面|畫面)|^(你|您)(能)?看到(了)?(什么|什麼)|^(这|這|现在|現在)(是|放的是|在放的是)?(什么|什麼)歌|^我(在|正在)(玩|干|幹|做)(什么|什麼)" +
            @"|(開いて|ひらいて|開けて|あけて|起動して|再生して|流して|かけて|閉じて|とじて|終了して|止めて|とめて|停止して|一時停止して|スキップして|飛ばして|検索して|調べて|探して|上げて|下げて|ミュートして|見せて|見て|送って|切り替えて|最小化して)(ください|くれる|くれない|くれます|くれますか|くれませんか|もらえる|もらえない|もらえますか|もらえませんか|ほしい|欲しい|よ|ね)?[。！？!?\s]*$" +
            @"|何が見える|(この|今の)(曲|歌)(は|って)?(何|なに)|(今|いま)(流れてる|かかってる)(曲|歌)" +
            @"|(열어|켜|꺼|닫아|틀어|재생해|검색해)[.!?。！？\s]*$|(열어|켜|꺼|닫아|틀어|재생해|실행해|일시정지해|멈춰|정지해|검색해|찾아|올려|낮춰|줄여|키워|넘겨|건너뛰어|보여|봐|보내|음소거해|최소화해|종료해) ?(줘|줘요|줄래|줄래요|줄 ?수 ?있어|줄 ?수 ?있어요|주세요|주실래요|주시겠어요)[.!?。！？\s]*$|^(다음|이전) ?(곡|노래)|^(이|지금) ?(노래|곡) ?(뭐야|뭐지|제목)|뭐가 보여|화면에 뭐" +
            // Lao and Khmer write no spaces between words either, so they match from the start, like Thai.
            @"|^(ກະລຸນາ|ຊ່ວຍ)?(ເປີດ|ປິດ|ຫຼິ້ນເພງ|ຫລິ້ນເພງ|ຊອກຫາ|ຄົ້ນຫາ|ເພີ່ມສຽງ|ຫຼຸດສຽງ|ຢຸດເພງ|ຂ້າມເພງ|ສົ່ງຂໍ້ຄວາມ)|^(សូម|ជួយ|សូមជួយ)?(បើក|បិទ|ចាក់ចម្រៀង|ចាក់បទ|ចាក់តន្ត្រី|ស្វែងរក|រកមើល|ដំឡើងសំឡេង|បន្ថយសំឡេង|ឈប់ចាក់|រំលងបទ|ផ្ញើសារ)" +
            @"|^(กรุณา|ช่วย|รบกวน)?(เปิด|ปิด|เล่นเพลง|ค้นหา|เพิ่มเสียง|ลดเสียง|หยุดเพลง|ข้ามเพลง|ส่งข้อความ|ย่อ)|^ขอ(ฟัง)?เพลง|^(ดู|มอง)หน้าจอ|^(เธอ|คุณ)?เห็นอะไร|^(นี่|นี่คือ|ตอนนี้)?(เป็น)?เพลงอะไร" +
            @"|^(làm ơn |hãy |bạn |có thể |giúp (mình|tôi|tớ|em|anh|chị) )*(mở|bật|phát|tắt|đóng|tìm kiếm|tìm|tăng|giảm|tạm dừng|dừng|chuyển|tiếp tục|cho (mình|tôi|tớ|em) (nghe|xem))(?!\w)|^(bài|nhạc) gì (đây|thế|vậy)" +
            @"|^(tolong |coba |bisa(kah)? |(kamu|kau|anda|lu|lo) )*(buka(kan|in)?|jalankan|putar(kan|in)?|puterin|mainkan|setel|tutup|matikan|nyalakan|hidupkan|cari(kan|in)?|naikkan|turunkan|besarkan|kecilkan|keraskan|pelankan|jeda|hentikan|lanjutkan|lewati|tampilkan|tunjukkan|kirim(kan|in)?|lihat (layar|ini|itu))(?!\w)|^lagu apa (ini|yang)" +
            // Turkish "aç" is hungry too ("karnım aç", or "çok aç" on its own, while "sesi çok aç" turns it up a lot), and
            // "çeneni kapat" (shut your mouth), Hindi and Urdu "बकवास बंद करो"
            // (stop the nonsense) and Kazakh "қарным аш" (I'm hungry) are chat, not something for her hands.
            @"|(^|\s)(aç(?<!(karnım|karnımız|karnın)( (çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ|de))? aç)(?<!^((ben|biz)( de)?( (çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ))?|çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ) aç)|açar m[ıi]s[ıi]n|açabilir misin|açsana|çal|çalar m[ıi]s[ıi]n|çalabilir misin|çalsana|oynat|kapat(?<!(çeneni|ağzını) kapat)|kapatır m[ıi]s[ıi]n|kapatabilir misin|kapatsana|ara|arar m[ıi]s[ıi]n|durdur|duraklat|göster|gösterir misin|gönder|kıs|yükselt|sustur|küçült|(şarkıyı|parçayı|bunu) (geç|atla))( lütfen)?[.!?\s]*$|^ekran(ım)?a bak|^bu (şarkı|parça) ne" +
            @"|^(відкрий|відкрити|увімкни|увімкнути|запусти|запустити|постав|поставити|закрий|закрити|вимкни|вимкнути|знайди|знайти|покажи|показати|зроби (гучніше|тихіше)|(можеш|можете|міг би|могла б|чи можеш|чи можете) (ти |ви )?(відкрити|увімкнути|запустити|поставити|закрити|знайти|показати|вимкнути))(?!\w)" +
            @"|^(من فضلك |لو سمحت |ممكن |هل (يمكنك|تستطيع) |يا كابري )*(افتح|إفتح|تفتح|فتح|شغل|شغّل|تشغل|تشغّل|تشغيل|اغلق|أغلق|إغلق|تغلق|إغلاق|اغلاق|سكر|سكّر|اطفئ|أطفئ|طفي|ابحث|إبحث|تبحث|البحث|دور|ارفع|اخفض|وطي|أوقف|اوقف|اعرض|أرني|ارني|ارسل|أرسل|ابعث)(لي)?(?!\w)|^(ماذا|شو) (ترى|تشوف|بتشوف)|^(ما|ايش|شو) (هذه|هاي|هذي) الأغنية" +
            @"|(खोलो|खोलिए|खोलिये|खोल दो|खोल दीजिए|चलाओ|चलाइए|चला दो|चला दीजिए|बजाओ|बजाइए|बजा दो|(?<!(बकवास|ड्रामा|नाटक|मज़ाक|मजाक|बोलना|रोना|चिल्लाना|नखरे|बातें) )बंद (करो|कर दो|कीजिए|करिए)|रोको|रोक दो|ब(ढ़|ढ़?)ाओ|ब(ढ़|ढ़?)ा दो|कम करो|कम कर दो|खोजो|ढूंढो|ढूँढो|ढूंढ दो|ढूँढ दो|दिखाओ|दिखा दो|भेजो|भेज दो|(ओपन|प्ले|क्लोज़|क्लोज|सर्च|स्टार्ट|म्यूट|स्किप|पॉज़|पॉज) (करो|कर दो|कीजिए|करिए)|(स्क्रीन|ये|यह) (देखो|देख लो|देखिए))( ना| न| प्लीज़| प्लीज)?[।.!?\s]*$|(खोल|चला|बजा|बंद कर|दिखा|ढूंढ|ढूँढ) (सकते|सकती|सकोगे|सकोगी)|कौन सा गाना" +
            @"|\b(kholo|khol do|kholdo|chalao|chala do|chalado|bajao|baja do|bajado|(?<!(bakwas|bakwaas|drama|natak|naatak|mazak|mazaak|majak|bolna|rona|chillana|nakhre) )band (karo|kar do|kardo)|roko|badhao|badha do|kam karo|dhundo|dhoondo|dikhao|bhejo|(open|play|close|search|start|mute|unmute|skip|pause|stop|on|off) (karo|kar do|kardo|kar dena|kijiye)|(screen|ye) dekho)( na| please| plz| pls)?[.!?\s]*$|\bkaun ?sa gaana\b" +
            // Swedish, Norwegian, Danish, Finnish, Czech, Slovak, Hungarian, Romanian, Greek and Filipino.
            @"|^((öppna|spela|stäng|sök|visa|pausa|starta|höj|sänk|åpne|spill(?!\s+(the|it|your|ur|my|them|all|everything)\b)|lukk|søk|åbn|spil|luk|søg|vis|skru (op|opp|ned))(?!\w)|(kan|kunde|skulle|vil) du\b.*\b(öppna|spela|stänga|söka|visa|starta|åpne|spille|lukke|søke|vise|starte|åbne|søge)(?!\w))" +
            @"|^((avaa|soita|sulje|etsi|hae|näytä|laita|käynnistä|pysäytä|keskeytä)(?!\w)|(voisitko|voitko)\b.*\b(avata|soittaa|sulkea|etsiä|hakea|näyttää|laittaa|käynnistää)(?!\w))" +
            @"|^((otevři|pusť|zavři|najdi|vyhledej|ukaž|zastav|spusť|přehraj|zesil|ztlum|otvor|pusti|zatvor|nájdi|vyhľadaj|ukáž|spusti|prehraj)(?!\w)|(můžeš|mohl bys|mohla bys|môžeš|mohol by si|mohla by si)\b.*\b(otevřít|pustit|zavřít|najít|vyhledat|ukázat|spustit|přehrát|otvoriť|pustiť|zatvoriť|nájsť|vyhľadať|ukázať|spustiť|prehrať)(?!\w))" +
            @"|^((nyisd|indítsd|játssz|játszd|zárd|keress|keresd|mutasd|állítsd|tedd|kapcsold|hangosítsd|halkítsd)(?!\w)|((meg|el|be|le) )?tudnád\b.*\b(nyitni|indítani|játszani|zárni|keresni|mutatni|kapcsolni)(?!\w))" +
            @"|^((deschide|pune|închide|caută|arată(-mi)?|pornește|porneste|oprește|opreste|redă|dă mai (tare|încet)|da mai (tare|incet))(?!\w)|(poți|poti|ai putea) (să |sa )?(deschizi|pui|închizi|inchizi|cauți|cauti|arăți|pornești|oprești|redai)(?!\w))" +
            @"|^((άνοιξε|ανοιξε|βάλε|βαλε|κλείσε|κλεισε|ψάξε|ψαξε|δείξε|δειξε|παίξε|παιξε|σταμάτα|σταματα|δυνάμωσε|χαμήλωσε|ξεκίνα)(?!\w)|(μπορείς|μπορεις|θα μπορούσες) να (ανοίξεις|ανοιξεις|βάλεις|βαλεις|κλείσεις|κλεισεις|ψάξεις|ψαξεις|δείξεις|δειξεις|παίξεις|παιξεις)(?!\w))" +
            @"|^((buksan|patugtugin|tugtugin|magpatugtog|isara|hanapin|ipakita|lakasan|hinaan|ihinto|i-?play|i-?open|i-?close)(?!\w)|paki(-\w+|bukas|buksan|patugtog|sara|isara|hanap|lakas|hina|play|open|close)(?!\w)|pa-(open|play|close|on|off)(?!\w)|(pwede|puwede|kaya) (mo )?(bang? )?(buksan|patugtugin|isara|hanapin|ipakita|i-?play|i-?open)(?!\w))" +
            // Malay, Swahili, Catalan, and Serbian, which covers Croatian and Bosnian too.
            @"|^(tolong |boleh(kah)? (tak |tidak )?|(awak|engkau) )*(buka(kan)?|pasang(kan)?|mainkan|tutup|cari(kan)?|kuatkan|perlahankan|hentikan|tunjuk(kan)?|hantar(kan)?|tengok (skrin|ni|tu))(?!\w)|^lagu apa (ni|ini)(?!\w)" +
            @"|^((tafadhali |naomba )*(fungua|cheza|funga|tafuta|onyesha|zima|washa|ongeza sauti|punguza sauti|simamisha|tuma)(?!\w)|(je )?(unaweza|waweza) (ku)?(fungua|cheza|funga|tafuta|onyesha|zima|washa|ongeza|punguza|tuma)(?!\w))" +
            @"|^((obre|posa(-me)?|tanca|mostra'?m|atura|reprodueix|engega|a?puja (el )?(volum|so|música)|a?baixa (el )?(volum|so|música))(?!\w)|(em )?(pots|podries) (obrir|posar|tancar|cercar|buscar|mostrar|aturar|reproduir|apujar|abaixar|engegar|apagar)(?!\w))" +
            @"|^((otvori|pusti|zatvori|pronađi|pronadji|nađi|nadji|traži|trazi|pokaži|pokazi|pojačaj|pojacaj|utišaj|utisaj|ugasi|upali|zaustavi|pošalji|posalji)(?!\w)|(možeš li|mozes li|da li možeš|da li mozes|možete li|mozete li) (mi )?(da )?(otvoriš|otvoris|otvoriti|pustiš|pustis|pustiti|zatvoriš|zatvoris|zatvoriti|pronađeš|pronadjes|pronaći|pronaci|pokažeš|pokazes|pokazati|pojačaš|pojacas|pojačati|pojacati|ugasiš|ugasis|ugasiti|upališ|upalis|upaliti)(?!\w))" +
            @"|^((отвори|пусти|затвори|пронађи|нађи|тражи|покажи|појачај|утишај|угаси|упали|заустави|пошаљи)(?!\w)|(можеш ли|да ли можеш|можете ли) (ми )?(да )?(отвориш|пустиш|затвориш|пронађеш|нађеш|покажеш|појачаш|угасиш|упалиш)(?!\w))" +
            // Estonian, Latvian, Lithuanian, Slovenian, Bulgarian and Maltese.
            @"|^((ava(?! (is|was|said|says|and|has|had|loves|likes|wants|thinks)\b)|mängi|sulge|otsi|näita|peata|pane|keera|käivita|saada)(?!\w)|(kas )?(sa |te )?(saad|saaksid|võiksid|saate|saaksite|võiksite) (palun )?(avada|mängida|sulgeda|otsida|näidata|panna|käivitada)(?!\w))" +
            @"|^((atver|atskaņo|aizver|meklē|parādi|apturi|ieslēdz|izslēdz|uzliec|palaid)(?!\w)|(vai )?(tu )?(vari|varētu|varat) (lūdzu )?(atvērt|atskaņot|aizvērt|meklēt|parādīt|ieslēgt|izslēgt|uzlikt|palaist)(?!\w))" +
            @"|^((atidaryk|paleisk|uždaryk|ieškok|surask|parodyk|sustabdyk|įjunk|išjunk|pagarsink|patylink)(?!\w)|(ar )?(gali|galėtum|galite|galėtumėte) (atidaryti|paleisti|uždaryti|ieškoti|surasti|parodyti|sustabdyti|įjungti|išjungti)(?!\w))" +
            @"|^((odpri|predvajaj|zapri|poišči|išči|ustavi|vklopi|izklopi|zaženi|pošlji)(?!\w)|(ali |a )?(mi )?(lahko|bi lahko) (mi )?(odpreš|predvajaš|zapreš|poiščeš|pokažeš|vklopiš|izklopiš|zaženeš|odprl|odprla|predvajal|predvajala)(?!\w))" +
            @"|^((пусни|намери|потърси|спри|увеличи|намали|изключи|прати)(?!\w)|(можеш ли|би ли могла|би ли могъл|може ли) (да )?(отвориш|пуснеш|затвориш|намериш|потърсиш|покажеш|спреш|включиш|изключиш)(?!\w))" +
            @"|^((iftaħ|iftah|doqq|agħlaq|aghlaq|fittex|urini|waqqaf|ixgħel|ixghel|itfi|għolli|gholli|baxxi)(?!\w)|(tista'?|tistax|tistgħu) (tiftaħ|tiftah|iddoqq|tagħlaq|taghlaq|tfittex|turini|twaqqaf|tixgħel|titfi)(?!\w))" +
            // Galician, Basque, Afrikaans, Icelandic, Albanian, Irish and Welsh. Words that are everyday chat in another of her
            // languages, like Portuguese "agora", Vietnamese "cau" or Spanish "vale", are left out.
            @"|^((pecha(me|o|a|lo|la)?|am[oó]sa(me)?)(?!\w)|(podes|poder[ií]as) (abrir|po[ñn]er|pechar|buscar|amosar|reproducir|tocar)(?!\w))" +
            @"|^(ireki|itxi|bilatu|erakutsi|gelditu|bidali|erreproduzitu|piztu|itzali)(?!\w)|(^|\s)(ireki|itxi|bilatu|erakutsi|gelditu|bidali|erreproduzitu|piztu|itzali)( ezazu| ezazue| mesedez)?$|(ireki|itxi|bilatu|erakutsi|gelditu|bidali|piztu|itzali) (dezakezu|ahal duzu|al duzu)(?!\w)" +
            @"|^((soek|wys my|stuur)(?!\w)|maak .+ (oop|toe)$|sit (die|dit|hom|haar)( .+)? (aan|af|harder|sagter)$|(kan|sal) jy\b.*\b(oopmaak|toemaak|speel|soek|wys|stuur|aansit|afsit)(?!\w))" +
            @"|^((opnaðu|opnadu|spilaðu|spiladu|lokaðu|lokadu|leitaðu|leitadu|sýndu|syndu|stöðvaðu|stodvadu|stoppaðu|stoppadu|sendu|hækkaðu|haekkadu|lækkaðu|laekkadu|kveiktu|slökktu|slokktu|settu)(?!\w)|(geturðu|geturdu|gætirðu|gaetirdu)\b.*\b(opnað|opnad|spilað|spilad|lokað|lokad|leitað|leitad|sýnt|synt|sett|kveikt|slökkt)(?!\w)|viltu (opna|loka|leita|sýna|syna|setja|kveikja|slökkva|slokkva|senda)(?!\w))" +
            @"|^((hape|luaj|mbyll|mbylle|kërko|kerko|më trego|me trego|ndalo|dërgo|dergo|ndiz)(?!\w)|(a )?(mund|mundesh|mundeni) (të |te |ta |t'i )?(hapësh|hapesh|luash|mbyllësh|mbyllesh|kërkosh|kerkosh|tregosh|ndezësh|ndezesh|dërgosh|dergosh)(?!\w))" +
            @"|^((oscail|seinn|dún|cuardaigh|taispeáin|taispeain|seol|ardaigh|ísligh|isligh|múch)(?!\w)|an (féidir|feidir) leat\b.*\b(oscailt|sheinm|sheinnt|chasadh|dhúnadh|dhunadh|chuardach|thaispeáint|thaispeaint|mhúchadh)(?!\w))" +
            @"|^((agor|agorwch|chwarae|chwaraea|chwaraewch|caea|caewch|chwilia|chwiliwch|dangos|dangosa|dangoswch|stopia|anfon|anfona|anfonwch|diffodd|diffodda)(?!\w)|(alli di|allet ti|wnei di|fedri di|gallwch chi|wnewch chi) (plîs |plis )?(agor|chwarae|cau|chwilio|dangos|anfon|diffodd|troi)(?!\w))" +
            // Belarusian and Macedonian; Azerbaijani, Uzbek, Kazakh, Mongolian and Nepali put the verb last.
            @"|^(адкрый|ўключы|уключы|прайграй|пастаў|зачыні|знайдзі|пакажы|спыні|выключы|дашлі|зрабі (гучней|цішэй)|(можаш|можаце|ці можаш|ці можаце) (ты |вы )?(адкрыць|уключыць|ўключыць|прайграць|паставіць|зачыніць|знайсці|паказаць|выключыць))(?!\w)" +
            @"|^((пушти|најди|пребарај|запри|стопирај|испрати|засили|вклучи|исклучи)(?!\w)|(можеш ли|дали можеш|може ли) (да )?(го |ја |ги )?(отвориш|пуштиш|затвориш|најдеш|пребараш|покажеш|вклучиш|исклучиш|испратиш)(?!\w))" +
            @"|(^|\s)(aç(?<!(karnım|karnımız|karnın)( (çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ|de))? aç)(?<!^((ben|biz)( de)?( (çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ))?|çok|aşırı|acayip|cidden|bayağı|baya|hala|hâlâ) aç)|bağla|oxut|qoş|axtar|göstər|dayandır|göndər|açarsan|aça bilərsən|bağlaya bilərsən|səsi artır|səsi azalt)( zəhmət olmasa)?$" +
            @"|ni (och|yop)( iltimos)?$|(^|\s)(oching|ochib ber|yoping|qo['ʻ‘`]?y|qo['ʻ‘`]?ying|ijro et|ijro eting|qidir|qidiring|ko['ʻ‘`]?rsat|ko['ʻ‘`]?rsating|to['ʻ‘`]?xtat|to['ʻ‘`]?xtating|yubor|yuboring)( iltimos)?$" +
            @"|(^|\s)(аш(?<!(қарным|қарнымыз|қарның|мен|біз|өте|қатты|әбден|тым) аш)|ашшы|ашыңыз|ашып бер|жап|жапшы|жабыңыз|қос|қосшы|қосыңыз|ойнат|ойнатшы|ізде|іздеші|іздеңіз|көрсет|көрсетші|көрсетіңіз|тоқтат|тоқтатшы|жібер|жіберші)( өтінемін)?$" +
            @"|(^|\s)(нээ|нээгээч|нээж өг|нээнэ үү|хаагаач|тоглуул|тоглуулаач|тоглуулна уу|хайгаач|хайна уу|харуул|харуулаач|харуулна уу|зогсоо|зогсоогоорой|зогсооно уу|илгээ|илгээгээч|илгээнэ үү|асаа|асаагаач|асаана уу|унтраа|унтраагаач|унтраана уу)( л| дээ)?$" +
            @"|(^|\s)(खोल|खोलिदेऊ|खोलिदिनुहोस्|खोल्नुहोस्|बजाऊ|बजाइदेऊ|बजाउनुहोस्|चलाऊ|चलाइदेऊ|चलाउनुहोस्|बन्द गर|बन्द गरिदेऊ|बन्द गर्नुहोस्|खोज|खोजिदेऊ|खोज्नुहोस्|देखाऊ|देखाइदेऊ|देखाउनुहोस्|रोक|रोक्नुहोस्|पठाऊ|पठाइदेऊ|पठाउनुहोस्)( न| है| प्लिज)?[।.!?\s]*$" +
            // Javanese, Hausa, Yoruba, Igbo, Zulu, Xhosa and Somali.
            @"|^((tulung|mbok|coba) )*(bukak(en|na|ke)?|bukaen|puter(en|na|ke)|setel(en|na|ke)?|tutupen|tutupna|golek(na|ke|ana)|goleki|tuduh(na|ke)|mandheg(na|ke)|kirim(na|ke|en))(?!\w)" +
            @"|^((don|dan) allah )?((ka|ki|ku) )?(buɗe|kunna|rufe|nuna min|tsayar|dakatar|kashe)(?!\w)|^(don|dan) allah ((ka|ki|ku) )?bude(?!\w)|\b(za ka iya|za ki iya|kana iya|kina iya) (buɗe|bude|kunna|rufe|nemo|nuna|aika|kashe)(?!\w)" +
            @"|^((jọ̀wọ́|jọwọ|jowo) )?(ṣí|ṣi|ṣe orin|kọ orin|wá|fi hàn mí|fi han mi|dá dúró|fi ránṣẹ́|fi ranse)(?!\w)" +
            @"|^(biko )?(meghee|mepee|kpọọ|kpoo|mechie|chọọ|gosi m|kwụsị|kwusi|ziga|gbanye|gbanyụọ|gbanyuo)(?!\w)" +
            @"|^((ngicela|ndicela|sicela) )?(vula|uvule|dlala|udlale|vala|uvale|sesha|usesha|khombisa|ukhombise|thumela|uthumele|khangela|ukhangele|bonisa|ubonise|yeka)(?!\w)|\bunga(vula|dlala|vala|sesha|khombisa|thumela|khangela|bonisa)(?!\w)" +
            // Somali puts the verb last, after a word ending in an article like "-ga" ("Spotify-ga fur"), so English "your fur" stays chat.
            @"|^fadlan\b.*\b(fur|fura|shid|shida|xir|xidh|raadi|raadiya|jooji|dami|tus|dir)$|(-(ka|ga|ha|ta|da)|\b(heesta|heesaha|muusigga|muusikada|fiidiyowga|barnaamijka|codka)) (fur|fura|shid|shida|xir|xidh|raadi|raadiya|jooji|dami)( fadlan)?$|\bi tus( fadlan)?$" +
            // Amharic puts the verb last too, and ends a sentence with "።" or asks with "፧".
            @"|(^|\s)(ክፈት|ክፈቺ|ክፈቱ|ክፈትልኝ|ክፈቺልኝ|ክፈቱልኝ|አጫውት|አጫውቺ|አጫውቱ|አጫውትልኝ|አጫውቺልኝ|አጫውቱልኝ|ዝጋ|ዝጊ|ዝጉ|ዝጋልኝ|ዝጊልኝ|ዝጉልኝ|ፈልግ|ፈልጊ|ፈልጉ|ፈልግልኝ|ፈልጊልኝ|ፈልጉልኝ|አሳየኝ|አሳይኝ|አሳዩኝ|አቁም|አቁሚ|አቁሙ|ላክ|ላኪ|ላኩ|ላክልኝ|ላኪልኝ|ላኩልኝ)( እባክህ| እባክሽ| እባካችሁ| እባክዎ)?[።፧!?.\s]*$" +
            // Burmese puts the verb last too. ပြ (show) is left out, since it also ends ပြောပြ (tell) and ရှင်းပြ (explain),
            // and ပါးစပ်ပိတ် (shut your mouth) is chat. So is ရင်ဖွင့် (opening one's heart), but အရင် ("first") and the "if"
            // endings ပြီးရင် and ဆိုရင် come before real commands.
            @"|(?<!(ပါးစပ်|ပါးစပ်ကို|နှုတ်|ငိုတာ|ငိုနေတာ|ရယ်တာ|အော်တာ) ?)(?<!(?<!အ|ပြီး|ဆို)ရင်)(ဖွင့်(ပြ)?|ပိတ်|ရှာဖွေ|ရှာ|ပို့|ရပ်|ကျော်)" + BurmeseAsk + @"|အသံ.*(ချဲ့|လျှော့|တိုး)" + BurmeseAsk +
            @"|(ဖွင့်|ပိတ်|ရှာ|ပို့|ရပ်|ကျော်)(လိုက်)?ပေး(နိုင်မလား|နိုင်လား|မလား)|^(ဒါ(က)? ?ဘာ ?သီချင်း|(ဒီ ?|အခု ?(ဖွင့်နေတဲ့ ?)?)သီချင်း(က)? ?(ဘာ ?သီချင်း|ဘာ|နာမည် ?ဘာ))(လဲ|လည်း)" +
            @"|(မျက်နှာပြင်|စခရင်|screen)( ?ကို)? ?ကြည့်" + BurmeseAsk + @"|^(မင်း|နင်|သင်|ရှင်)? ?ဘာ ?(တွေ)? ?မြင်(ရ)?(လဲ|သလဲ)" +
            // Georgian and Armenian put the verb first or last, so their command forms match anywhere, as whole words.
            @"|(^|\s)(გახსენი|გახსენით|ჩართე|ჩართეთ|დაუკარი|დაუკარით|დახურე|დახურეთ|გამორთე|გამორთეთ|მოძებნე|მოძებნეთ|მოიძიე|მაჩვენე|მაჩვენეთ|გააჩერე|გააჩერეთ|გაგზავნე|გაგზავნეთ|აუწიე|დაუწიე)(?!\w)|ეს რა სიმღერაა|(ეკრანს|ეკრანზე) (შეხედე|დახედე|ნახე)|^რას ხედავ" +
            @"|(^|\s)(բացիր|բացեք|միացրու|միացրեք|նվագիր|նվագեք|փակիր|փակեք|անջատիր|անջատեք|որոնիր|որոնեք|գտիր|գտեք|փնտրիր|ցույց տուր|ցույց տվեք|կանգնեցրու|դադարեցրու|ուղարկիր|ուղարկեք|բարձրացրու|իջեցրու)(?!\w)|(^|\s)(սա )?ինչ երգ է( սա| հիմա| նվագում| հնչում| միացված)*[.!?\s]*$|նայիր (իմ )?էկրան|^ինչ ես տեսնում" +
            // Hebrew, Urdu and Persian. Urdu and Persian put the verb last.
            @"|^(בבקשה )?(פתח|פתחי|תפתח|תפתחי|נגן|נגני|תנגן|תנגני|סגור(?= )|סגרי|תסגור|תסגרי|חפש|חפשי|תחפש|תחפשי|תראה לי|תראי לי|הראה לי|הראי לי|עצור|עצרי|תעצור|תעצרי|הגבר|תגביר|תגבירי|הנמך|תנמיך|תנמיכי|שים(?! לב)|שימי(?! לב)|תשים|תשימי|הפעל|הפעילי|תפעיל|תפעילי|כבה|כבי|תכבה|תכבי|שלח|שלחי|תשלח|תשלחי)(?!\w)|^(את יכולה|אתה יכול|תוכלי|תוכל|אפשר) (בבקשה )?ל(פתוח|נגן|סגור|חפש|הראות|הפעיל|כבות|שים|הגביר|הנמיך|שלוח)(?!\w)" +
            @"|(کھولو|کھول دو|کھولیں|کھول دیں|چلاؤ|چلا دو|چلائیں|چلا دیں|بجاؤ|بجا دو|بجائیں|(?<!(بکواس|ڈرامہ|ڈرامے|مذاق|بولنا|رونا|چلانا|نخرے) )بند (کرو|کر دو|کریں|کر دیں)|ڈھونڈو|ڈھونڈ دو|تلاش کرو|تلاش کریں|دکھاؤ|دکھا دو|دکھائیں|روکو|روک دو|روکیں|بڑھاؤ|بڑھا دو|کم کرو|کم کر دو|بھیجو|بھیج دو)( نا| پلیز| پلز)?[۔.!?؟\s]*$|(کھول|چلا|بجا|بند کر|دکھا|ڈھونڈ) (سکتے|سکتی|سکو) (ہو|ہیں|گے|گی)|کون سا گانا" +
            @"|(باز کن|بازش کن|باز کنید|پخش کن|پخشش کن|پخش کنید|بذار|بزار|بگذار|بذارش|ببند|ببندش|ببندید|جستجو کن|سرچ کن|بگرد|پیدا کن|نشون بده|نشونم بده|نشان بده|نشانم بده|متوقف کن|قطع کن|زیاد کن|زیادش کن|کم کن|کمش کن|بفرست|اجرا کن)( لطفا| لطفاً| دیگه| پلیز)?[.!?؟\s]*$|^(لطفا |لطفاً )?(می[\u200C ]?(تونی|توانی)|می[\u200C ]?شه) .*(باز کنی|بازش کنی|پخش کنی|بذاری|بزاری|بگذاری|ببندی|جستجو کنی|بگردی|پیدا کنی|نشون بدی|نشان بدهی|اجرا کنی|بفرستی)" +
            // Bengali, Marathi, Gujarati, Punjabi, Tamil, Telugu, Kannada and Malayalam put the verb last too, and English verbs
            // with Tamil, Telugu, Kannada or Malayalam endings ("open pannu") are common.
            @"|(^|\s)(খোলো|খুলে দাও|খুলুন|খোল|চালাও|চালিয়ে দাও|চালান|চালু করো|চালু করে দাও|বাজাও|বাজিয়ে দাও|বাজান|বন্ধ করো|বন্ধ করে দাও|বন্ধ করুন|বন্ধ কর|খোঁজো|খুঁজে দাও|খুঁজুন|দেখাও|দেখিয়ে দাও|দেখান|থামাও|থামিয়ে দাও|থামান|পাঠাও|পাঠিয়ে দাও|পাঠান|বাড়াও|বাড়িয়ে দাও|কমাও|কমিয়ে দাও)( না| প্লিজ| প্লীজ)?[।.!?\s]*$|(খুলতে|চালাতে|বাজাতে|বন্ধ করতে|দেখাতে|খুঁজতে) (পারবে|পারবেন|পারো|পারেন|পারবি)" +
            @"|(^|\s)(उघड|उघडा|उघडून दे|लाव|लावा|लावून दे|चालू कर|चालू करा|बंद कर|बंद करा|बंद करून दे|शोधा|शोधून दे|दाखव|दाखवा|थांबव|थांबवा|पाठव|पाठवा|वाढव|वाढवा|कमी कर|कमी करा)( ना| प्लीज)?[।.!?\s]*$|(उघडू|लावू|चालू करू|बंद करू|दाखवू|शोधू) (शकतेस|शकतोस|शकता|शकाल) का" +
            @"|(^|\s)(ખોલ|ખોલો|ખોલી દે|ખોલી દો|વગાડ|વગાડો|વગાડી દે|વગાડી દો|ચાલુ કર|ચાલુ કરો|બંધ કર|બંધ કરો|બંધ કરી દે|બંધ કરી દો|શોધ|શોધો|શોધી દે|બતાવ|બતાવો|રોકો|મોકલ|મોકલો|વધારો|ઘટાડો)( ને| પ્લીઝ)?[.!?\s]*$|(ખોલી|વગાડી|ચાલુ કરી|બંધ કરી|બતાવી|શોધી) (શકે|શકીશ|શકો|શકશો)" +
            @"|(^|\s)(ਖੋਲ੍ਹੋ|ਖੋਲੋ|ਖੋਲ੍ਹ ਦਿਓ|ਖੋਲ੍ਹ ਦੇ|ਖੋਲ ਦਿਓ|ਖੋਲ ਦੇ|ਚਲਾਓ|ਚਲਾ ਦਿਓ|ਚਲਾ ਦੇ|ਵਜਾਓ|ਵਜਾ ਦਿਓ|ਵਜਾ ਦੇ|ਬੰਦ ਕਰੋ|ਬੰਦ ਕਰ ਦਿਓ|ਬੰਦ ਕਰ ਦੇ|ਲੱਭੋ|ਲੱਭ ਦਿਓ|ਦਿਖਾਓ|ਦਿਖਾ ਦਿਓ|ਰੋਕੋ|ਰੋਕ ਦਿਓ|ਭੇਜੋ|ਭੇਜ ਦਿਓ|ਵਧਾਓ|ਘਟਾਓ)( ਨਾ| ਪਲੀਜ਼| ਪਲੀਜ)?[।.!?\s]*$|(ਖੋਲ੍ਹ|ਖੋਲ|ਚਲਾ|ਵਜਾ|ਬੰਦ ਕਰ|ਦਿਖਾ|ਲੱਭ) (ਸਕਦੇ|ਸਕਦੀ|ਸਕੋਗੇ|ਸਕੋਗੀ)" +
            @"|(^|\s)(திற|திறங்க|திறக்கவும்|திறந்து விடு|இயக்கு|இயக்குங்க|இயக்கவும்|போடு|போடுங்க|போட்டு விடு|மூடு|மூடுங்க|மூடவும்|மூடி விடு|தேடு|தேடுங்க|தேடவும்|காட்டு|காட்டுங்க|காட்டவும்|நிறுத்து|நிறுத்துங்க|நிறுத்தவும்|அனுப்பு|அனுப்புங்க|அனுப்பவும்|(ஓபன்|ஓப்பன்|ப்ளே|பிளே|க்ளோஸ்|சர்ச்|ஸ்டாப்) (பண்ணு|பண்ணுங்க|செய்|செய்யவும்))( ப்ளீஸ்| பிளீஸ்)?[.!?\s]*$|(திறக்க|இயக்க|போட|மூட|தேட|காட்ட) முடியுமா" +
            @"|(^|\s)(తెరువు|తెరవండి|తెరిచి పెట్టు|ప్లే చేయి|ప్లే చెయ్యి|ప్లే చేయండి|పెట్టు|పెట్టండి|మూసివేయి|మూసేయి|మూసివేయండి|వెతుకు|వెతకండి|చూపించు|చూపించండి|ఆపు|ఆపండి|పంపు|పంపండి|పెంచు|పెంచండి|తగ్గించు|తగ్గించండి|(ఓపెన్|క్లోజ్|సెర్చ్|స్టార్ట్|స్టాప్) (చేయి|చెయ్యి|చేయండి))( ప్లీజ్)?[.!?\s]*$|(తెరవ|ప్లే చేయ|పెట్ట|మూసివేయ|వెతక|చూపించ)గల(వా|రా)" +
            @"|(^|\s)(ತೆರೆ|ತೆರೆಯಿರಿ|ತೆರೆದು ಬಿಡು|ಪ್ಲೇ ಮಾಡು|ಪ್ಲೇ ಮಾಡಿ|ಹಾಕು|ಹಾಕಿ|ಮುಚ್ಚು|ಮುಚ್ಚಿ|ಹುಡುಕು|ಹುಡುಕಿ|ತೋರಿಸು|ತೋರಿಸಿ|ನಿಲ್ಲಿಸು|ನಿಲ್ಲಿಸಿ|ಕಳುಹಿಸು|ಕಳುಹಿಸಿ|ಹೆಚ್ಚಿಸು|ಹೆಚ್ಚಿಸಿ|ಕಡಿಮೆ ಮಾಡು|ಕಡಿಮೆ ಮಾಡಿ|(ಓಪನ್|ಕ್ಲೋಸ್|ಸರ್ಚ್|ಸ್ಟಾರ್ಟ್|ಸ್ಟಾಪ್) (ಮಾಡು|ಮಾಡಿ))( ಪ್ಲೀಸ್)?[.!?\s]*$|(ತೆರೆಯ|ಪ್ಲೇ ಮಾಡ|ಹಾಕ|ಮುಚ್ಚ|ಹುಡುಕ|ತೋರಿಸ)ಬಹುದಾ" +
            @"|(^|\s)(തുറക്കൂ|തുറക്ക്|തുറന്നു തരൂ|പ്ലേ ചെയ്യൂ|പ്ലേ ചെയ്യ്|ഇടൂ|ഇട്|അടയ്ക്കൂ|അടയ്ക്ക്|അടച്ചു തരൂ|തിരയൂ|തിരയ്|തപ്പ്|കാണിക്കൂ|കാണിക്ക്|നിർത്തൂ|നിർത്ത്|അയയ്ക്കൂ|അയയ്ക്ക്|കൂട്ടൂ|കുറയ്ക്കൂ|കുറയ്ക്ക്|(ഓപ്പൺ|ക്ലോസ്|സെർച്ച്|സ്റ്റാർട്ട്|സ്റ്റോപ്പ്) (ചെയ്യൂ|ചെയ്യ്))( പ്ലീസ്)?[.!?\s]*$|(തുറക്ക|പ്ലേ ചെയ്യ|ഇട|അടയ്ക്ക|തിരയ|കാണിക്ക)ാമോ|(തുറക്കാൻ|പ്ലേ ചെയ്യാൻ|ഇടാൻ|അടയ്ക്കാൻ) പറ്റുമോ" +
            @"|\b(open|play|close|search|start|stop|pause|skip) (pannu|pannunga|panni vidu|cheyyi|chey|cheyandi|cheyyandi|maadu|maadi|madu|madi|cheyyu|cheyy)( da| di| please| pls| plz)?[.!?\s]*$", RegexOptions.IgnoreCase);

        private static readonly Regex Errands = new Regex(
            @"^(send|dm|pm|reply to|respond to)\b(?!\s+(me|us|help)\b)|^(message|msg|text|ping) (?!(is|was|are|were|from|looks|said|says|me|us)\b)\w|^(post|tweet) (this|that|it|a|an|my|our|something|on|in|to)\b" +
            @"|^(tell|ask|say|let|invite|kick|ban|add|react|call|ring|share|pin|block|unblock|unfriend) .+\b(on|in|into|through|via|over|to) (the |my )?(#\S+|discord|slack|teams|whatsapp|telegram|signal|messenger|skype|instagram|insta|snapchat|snap|reddit|twitter|tiktok|facebook|roblox|steam|(group )?chat|call|vc|server|channel|gc|dms?)(?!\w)" +
            @"|^type (?!(of|is|was)\b)\w|^(click|tap|double click|right click) (?!(bait|is|was)\b)\w|^scroll (up|down|left|right|to|through)\b|^(fill (in|out)|sign in|log in)\b" +
            @"|^(end|leave|hang up|disconnect( from)?|join|accept|decline|pick up|answer|reject|hop (in|on|off)|get off) (the |my |this |that |a |our |their )?(\w+('s)? )?(call|vc|voice (chat|channel|call)|video call|meeting|stream|server|channel)\b|^(hang up|disconnect|hop off|get off)( (the )?(call|vc))?$", RegexOptions.IgnoreCase);
        private static readonly Regex Opener = new Regex(@"^((great|nice|cool|sweet|perfect|awesome|amazing|alright|alr|aight|bet|thanks|thank you|thx|ty|so|and|now|also|oh|wait|um|uh|hmm|lol|lmao|bro|dude|bruh)[\s,!.]+)+(?=((hey|yo|ok|okay)[\s,]+)?(capri\b|(can|could|would|will) (you|u)\b|please\b|pls\b|plz\b))", RegexOptions.IgnoreCase);

        internal static Func<List<InstalledApp>> Catalog = ReadCatalog;
        internal static Func<string, bool> Exists = File.Exists;
        internal static Action<string> Run = Start;
        internal static Action<byte> Press = Tap;
        internal static Func<string, string> Look = delegate { return "You can't see their screen right now."; };
        internal static Func<string, string> Fetch = Download;
        internal static Func<List<OpenWindow>> Windows = ReadWindows;
        internal static Func<OpenWindow, string, bool> Steer = Apply;
        // Why open windows couldn't be listed just now, or null. Mac and Linux say so when their window tool is missing.
        internal static Func<string> Unlisted = delegate { return null; };
        // False where she can't press keys yet (Mac and Linux), so a Discord shortcut says so before Discord comes to the front.
        internal static bool Presses = true;
        // A shortcut done another way, like a screenshot tool on Mac and Linux, returns what she did; null presses the keys.
        internal static Func<string, string> Instead = delegate { return null; };
        internal static Func<string, bool> PressPlay = ClickPlay;
        internal static Func<string> NowPlaying = SpotifyTitle;
        internal static Func<string, string, string> Work = delegate { return "Using the mouse and keyboard isn't ready on this PC yet."; };
        internal static Func<string, string> Resume = delegate { return null; };
        // Puts dots over a password she typed into a password box, wherever it would show or be saved.
        internal static Func<string, string> Hush = delegate(string text) { return text; };
        internal static Action<byte[]> Chord = PressChord;
        internal static Func<string, int, bool> Power = Switch;
        // Puts her to sleep (true) or wakes her (false), and says how it went. Her window sets it.
        internal static Func<bool, string> Nap = delegate { return "Couldn't nap just now."; };
        private static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi" };
        private static readonly string[] Shell = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
        private delegate bool Visit(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(Visit visit, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(int from, int to, bool attach);
        [DllImport("kernel32.dll")] private static extern int GetCurrentThreadId();
        private static readonly Regex Junk = new Regex(@"\b(uninstall\w*|desinstall\w*|readme|read me|lisez moi|help|aide|manual|manuel|documentation|release notes|notes de version|licen[cs]e|website|site web|support|assistance|changelog)\b", RegexOptions.IgnoreCase);
        private static readonly string[,] Searches =
        {
            { "youtube", "https://www.youtube.com/results?search_query=" },
            { "google", "https://www.google.com/search?q=" },
            { "twitch", "https://www.twitch.tv/search?term=" },
            { "reddit", "https://www.reddit.com/search/?q=" },
            { "wikipedia", "https://en.wikipedia.org/w/index.php?search=" },
            { "spotify", "https://open.spotify.com/search/" },
            { "roblox", "https://www.roblox.com/discover/?Keyword=" },
            { "github", "https://github.com/search?q=" }
        };
        private static readonly object gate = new object();
        private static List<InstalledApp> cached;
        private static DateTime cachedAt;

        internal static string Do(string tool, ToolArguments args)
        {
            if (args == null) args = new ToolArguments();
            try
            {
                if (tool == "open_app") return OpenApp(args.Name);
                if (tool == "open_website") return OpenSite(args.Site, args.Search);
                if (tool == "media") return Media(args);
                if (tool == "look_at_screen") return Look(args.Focus);
                if (tool == "play_music") return PlayMusic(args.Song, args.App, args.Kind);
                if (tool == "app_window") return Window(args.Name, args.Action);
                if (tool == "use_computer") return string.IsNullOrWhiteSpace(args.Task) ? "Which job, choom?" : Work(args.Task.Trim(), args.Asked);
                if (tool == "shortcut") return Shortcut(args.Name);
                if (tool == "remember") return args.Action == "forget" ? Brain.Unnote(Theirs(args.Text)) : CallMe(args.Text) ?? Brain.Note(Theirs(args.Text));
                if (tool == "power") return Turn(args.Action, args.Minutes);
                if (tool == "nap") return Nap(args.Action != "wake");
                return "There's no tool called " + tool + ".";
            }
            catch (Exception error) { return "That didn't work: " + error.Message; }
        }

        internal static void Forget() { lock (gate) cached = null; }

        private static readonly string[] Swaps = { @"\bi'?m\b", "they're", @"\bi am\b", "they are", @"\bi was\b", "they were", @"\bi've\b", "they've", @"\bi'll\b", "they'll", @"\bi'd\b", "they'd", @"\bi\b", "they", @"\bmyself\b", "themselves", @"\bmy\b", "their", @"\bmine\b", "theirs", @"\bme\b", "them" };

        // "Remember my name is Sam" sets what she calls them, the same as the box in Settings, instead of a note.
        internal static Func<string, string> Rename;

        private static string CallMe(string text)
        {
            Match named = Regex.Match((text ?? "").Replace('’', '\'').Trim(), @"^(?:that\s+)?(?:my name is|my name's|call me|i'm called|i am called|i go by)\s+([\p{L}][\p{L}'-]*(?:\s[\p{L}][\p{L}'-]*)?)[.!]*$", RegexOptions.IgnoreCase);
            Func<string, string> rename = Rename;
            return named.Success && rename != null ? rename(named.Groups[1].Value) : null;
        }

        // Her asking what to remember, so their next message is the note.
        internal static bool AskedWhat(string said) { return Regex.IsMatch(said ?? "", @"\bwhat\b[^.!?]*\b(remember|note|memory|save|keep)\b[^.!?]*\?\s*$", RegexOptions.IgnoreCase); }

        // "Remember that" or "make a note of it" points back at what they just said.
        internal static bool Back(string said) { return Regex.IsMatch(Asked(said), @"\b(that|this|it)$", RegexOptions.IgnoreCase); }

        // Ways of asking her to save a note: remember, keep in mind, leave or make a memory, put it in your notes or me.md.
        private const string Memory = @"(?:leave|make|take|save|add|write|store|create|keep|drop) (?:a |an |one |this |that |the )?(?:new |quick |little )?(?:memory|memories|note|notes)(?: (?:in|to|into|on) (?:your |ur |the )?(?:notes|memory|memories|me\.?md|md)(?: file)?)?";
        private const string Remembering = @"^(?:(?:i (?:want|need|would like) (?:you|u) to|you (?:should|gotta|need to))\s+)?(?:remember|memori[sz]e|don't forget|dont forget|do not forget|keep in mind|note (?:down|that)|(?:write|jot|note|put) (?:it|that|this) down|" + Memory + @"(?: (?:that|saying|about|of))?|(?:save|put|add|write|store|log|jot|keep)(?: (?:it|that|this))?(?: down)? (?:to|in|into|on) (?:your |ur |the )?(?:notes|memory|memories|me\.?md|md|brain|head)(?: file)?)";

        internal static string Theirs(string text)
        {
            string note = Regex.Replace((text ?? "").Replace('’', '\'').Trim(), @"^(that|this)\s+", "", RegexOptions.IgnoreCase);
            for (int i = 0; i < Swaps.Length; i += 2) note = Regex.Replace(note, Swaps[i], Swaps[i + 1], RegexOptions.IgnoreCase);
            return note;
        }

        internal static int Count { get { return Specs.GetLength(0); } }

        internal static string Name(int i) { return Specs[i, 0]; }

        internal static string Description(int i) { return Specs[i, 1]; }

        internal static string ElevenLabsTool(int i) { return "{\"tool_config\":{\"type\":\"client\",\"name\":\"" + Specs[i, 0] + "\",\"description\":" + Voice.Quote(Specs[i, 1]) + ",\"parameters\":" + Specs[i, 2] + ",\"expects_response\":true,\"response_timeout_secs\":" + (Specs[i, 0] == "use_computer" ? 60 : 20) + "}}"; }

        private static string Render(bool claude)
        {
            List<string> tools = new List<string>();
            for (int i = 0; i < Specs.GetLength(0); i++)
                tools.Add(claude
                    ? "{\"name\":\"" + Specs[i, 0] + "\",\"description\":" + Voice.Quote(Specs[i, 1]) + ",\"input_schema\":" + Specs[i, 2] + "}"
                    : "{\"type\":\"function\",\"function\":{\"name\":\"" + Specs[i, 0] + "\",\"description\":" + Voice.Quote(Specs[i, 1]) + ",\"parameters\":" + Specs[i, 2] + "}}");
            return "[" + string.Join(",", tools.ToArray()) + "]";
        }

        private static string Written()
        {
            StringBuilder text = new StringBuilder("\nYour tools:");
            for (int i = 0; i < Specs.GetLength(0); i++)
            {
                List<string> keys = new List<string>();
                foreach (Match key in Regex.Matches(Specs[i, 2], @"""(\w+)"":\{""type"":""string"",""description"":""[^""]*""(?:,""enum"":\[([^\]]*)\])?"))
                    keys.Add("\"" + key.Groups[1].Value + "\": \"" + (key.Groups[2].Success ? key.Groups[2].Value.Replace("\"", "").Replace(",", "|") : "...") + "\"");
                text.Append("\n- " + Specs[i, 0] + "{" + string.Join(", ", keys.ToArray()) + "}: " + Specs[i, 1]);
            }
            return text.Append("\nChoose one value from each | choice and omit unused fields.\nTo use one, reply with only the call, like: open_app{\"name\": \"Spotify\"}").ToString();
        }

        internal const string SayPlease = "Say please.";
        // Please and thanks in all her languages, so "öffne bitte Spotify" never gets Say please and "bitte" runs what was held.
        // Chinese, Japanese, Korean, Thai and the Indian scripts have no word breaks a \b can see, so they match anywhere.
        private static readonly Regex Polite = new Regex(@"\b(please|pls|plz|pleas|thank you|thanks|por favor|porfavor|porfa|porfis|gracias|merci|s'il (te|vous) pla[iî]t|sil (te|vous) pla[iî]t|stp|svp" +
            @"|bitte|danke|per favore|perfavore|per piacere|grazie|pfv|obrigad[oa]|si us plau|sisplau|siusplau|gr[àa]cies|grazas|mesedez|eskerrik asko|alsjeblieft|alstublieft|a\.?u\.?b|dank je|dankjewel|bedankt|asseblief|dankie" +
            @"|snälla|snalla|tack|vær så snill|vennligst|venligst|takk|tak|vinsamlegast|ole hyvä|ole kiltti|kiitos|palun|aitäh|aitah|lūdzu|ludzu|paldies|prašau|prasau|ačiū|aciu" +
            @"|proszę|prosze|dzięki|dzieki|dziękuję|dziekuje|prosím|prosim|díky|diky|děkuji|dekuji|ďakujem|dakujem|kérlek|kerlek|légyszi|legyszi|köszi|koszi|köszönöm|hvala|molim|молим|хвала" +
            @"|të lutem|te lutem|ju lutem|faleminderit|te rog|vă rog|va rog|mersi|mulțumesc|multumesc|le do thoil|go raibh maith agat|plîs|plis|os gwelwch yn dda|diolch|jekk jogħġbok|jekk joghgbok|grazzi" +
            @"|lütfen|lutfen|teşekkürler|tesekkurler|sağ ?ol|sag ?ol|zəhmət olmasa|xahiş edirəm|təşəkkürlər|iltimos|rahmat" +
            @"|пожалуйста|пожалуста|плиз|плз|пж|спасибо|будь ласка|дякую|калі ласка|дзякуй|моля|благодаря|мерси|те молам|ве молам|фала|благодарам|өтінемін|рахмет|гуйя|баярлалаа|παρακαλώ|παρακαλω|ευχαριστώ|ευχαριστω" +
            @"|làm ơn|lam on|cảm ơn|cám ơn|cam on|tolong|mohon|terima kasih|makasih|sila|tulung|mangga|matur nuwun|suwun|pakiusap|paki\w*|salamat|tafadhali|asante|don allah|dan allah|na gode" +
            @"|jọ̀wọ́|jọwọ|jowo|o ṣeun|o seun|biko|daalụ|daalu|ngiyacela|ngicela|sicela|ngiyabonga|siyabonga|ndiyacela|ndicela|enkosi|fadlan|mahadsanid" +
            @"|من فضلك|لو سمحت|رجاء|رجاءً|الرجاء|شكرا|شكراً|براہ کرم|مہربانی|پلیز|شکریہ|لطفا|لطفاً|ممنون|مرسی|בבקשה|תודה|እባክህ|እባክሽ|እባካችሁ|እባክዎ|አመሰግናለሁ|እናመሰግናለን|გთხოვ|გთხოვთ|მადლობა|გმადლობ|გმადლობთ|խնդրում եմ|խնդրեմ|շնորհակալություն|շնորհակալ եմ|մերսի)\b" +
            @"|请|請|麻烦|麻煩|拜托|拜託|谢谢|謝謝|ください|下さい|お願い|おねがい|ありがとう|주세요|부탁|제발|고마워|감사|กรุณา|รบกวน|ขอบคุณ|ກະລຸນາ|ຂອບໃຈ|សូម|អរគុណ|ကျေးဇူးပြု၍|ကျေးဇူးပြုပြီး|ကျေးဇူးတင်|ကျေးဇူးပဲ|ပါ(ဦး|ဥ\u102Eး|နော်)?[။၊!?.\s]*$" +
            @"|कृपया|प्लीज़|प्लीज|प्लिज|धन्यवाद|शुक्रिया|দয়া করে|প্লিজ|প্লীজ|ধন্যবাদ|કૃપા કરીને|પ્લીઝ|આભાર|ਕਿਰਪਾ ਕਰਕੇ|ਪਲੀਜ਼|ਪਲੀਜ|ਧੰਨਵਾਦ" +
            @"|தயவுசெய்து|ப்ளீஸ்|பிளீஸ்|நன்றி|దయచేసి|ప్లీజ్|ధన్యవాదాలు|ದಯವಿಟ್ಟು|ಪ್ಲೀಸ್|ಧನ್ಯವಾದ|ദയവായി|പ്ലീസ്|നന്ദി", RegexOptions.IgnoreCase);
        private static readonly Regex JustPlease = new Regex(@"^\W*((ok|okay|fine|sorry|vale|perd[oó]n|d[ée]sol[ée])\W+)?(pretty\s+)?(please+|pls|plz|pleas+e*|por favor|porfavor|porfa|porfis|s'il (te|vous) pla[iî]t|sil (te|vous) pla[iî]t|stp|svp" +
            @"|bitte( sehr| schön)?|per favore|perfavore|per piacere|pfv|si us plau|sisplau|siusplau|mesedez|alsjeblieft|alstublieft|a\.?u\.?b\.?|asseblief|snälla|snalla|vær så snill|vennligst|venligst|vinsamlegast|ole hyvä|ole kiltti|palun|lūdzu|ludzu|prašau|prasau" +
            @"|proszę|prosze|prosím|prosim|kérlek|kerlek|légyszi|legyszi|molim( te| vas)?|молим( те)?|të lutem|te lutem|ju lutem|te rog|vă rog|va rog|le do thoil|plîs|plis|os gwelwch yn dda|jekk jogħġbok|jekk joghgbok" +
            @"|lütfen|lutfen|zəhmət olmasa|xahiş edirəm|iltimos|пожалуйста|пожалуста|плиз|плз|пж|будь ласка|калі ласка|моля|те молам|ве молам|өтінемін|гуйя|παρακαλώ|παρακαλω" +
            @"|làm ơn|lam on|tolong|mohon|sila|tulung|mangga|pakiusap|tafadhali|don allah|dan allah|jọ̀wọ́|jọwọ|jowo|biko|ngiyacela|ngicela|sicela|ndiyacela|ndicela|fadlan" +
            @"|请|請|拜托|拜託|お願い(します)?|おねがい(します)?|ください|부탁(해|해요|합니다)|제발|กรุณา|ขอร้อง|ກະລຸນາ|សូម(មេត្តា)?|ကျေးဇူးပြု၍|ကျေးဇူးပြုပြီး" +
            @"|कृपया|प्लीज़|प्लीज|प्लिज|দয়া করে|প্লিজ|প্লীজ|કૃપા કરીને|પ્લીઝ|ਕਿਰਪਾ ਕਰਕੇ|ਪਲੀਜ਼|ਪਲੀਜ|தயவுசெய்து|ப்ளீஸ்|பிளீஸ்|దయచేసి|ప్లీజ్|ದಯವಿಟ್ಟು|ಪ್ಲೀಸ್|ദയവായി|പ്ലീസ്" +
            @"|من فضلك|لو سمحت|رجاء|رجاءً|الرجاء|براہ کرم|پلیز|لطفا|لطفاً|בבקשה|እባክህ|እባክሽ|እባካችሁ|እባክዎ|გთხოვ|გთხოვთ|խնդրում եմ|խնդրեմ)(\s+capri)?\W*$", RegexOptions.IgnoreCase);
        private static readonly Random Dice = new Random();
        internal static Func<int> PleaseRoll = delegate { lock (Dice) return Dice.Next(10); };
        private static string heldForPlease;

        internal static string Manners(string said, out bool askPlease)
        {
            askPlease = false;
            string held = heldForPlease;
            heldForPlease = null;
            if (held != null && JustPlease.IsMatch(said ?? "")) return held + " please";
            if (Polite.IsMatch(said ?? "") || !Wants(said) || PleaseRoll() != 0) return said;
            heldForPlease = said;
            askPlease = true;
            return said;
        }

        internal static bool Wants(string said)
        {
            if (Regex.IsMatch(said ?? "", @"\b(please|pls|plz|stp)\s*[.!?]*$", RegexOptions.IgnoreCase) && Site(Asked(said))) return true;
            foreach (string part in Regex.Split(said ?? "", @"[,;!?，；！？。．،؟।።፣፧။၊։՝]\s*|\.\s+"))
            {
                // Cleaned up before and after, so the idioms are found however they're typed.
                string text = Asked(WithoutMouthOrEyes(Asked(part)));
                if (!Chatter.IsMatch(text) && Asks.IsMatch(text)) return true;
            }
            return false;
        }

        internal static void Rescue(string said, List<KeyValuePair<string, ToolArguments>> calls)
        {
            string text = Asked(said);
            if (calls.Exists(call => call.Key == "use_computer" || call.Key == "look_at_screen" || call.Key == "media" || call.Key == "play_music")) return;
            if (Wants(said) && ((Errand(said) && !calls.Exists(call => call.Key == "shortcut" || call.Key == "remember")) || Regex.IsMatch(text, @"\s(and|then|&)\s+(then\s+)?(\w+\s+)?(search|look|type|click|go|make|create|write|find|open|sign|log|send|start|join|watch|check|put|add|draw|set|turn|say|tell|message|text|dm|reply|post|ask|paste|share|ping)\b", RegexOptions.IgnoreCase)))
            {
                calls.Clear();
                calls.Add(new KeyValuePair<string, ToolArguments>("use_computer", new ToolArguments { Task = text }));
                return;
            }
            for (int i = 0; i < calls.Count; i++)
            {
                ToolArguments args = calls[i].Value;
                if (calls[i].Key != "open_website" || args == null || string.IsNullOrEmpty(args.Site) || Regex.IsMatch(text, @"[a-z0-9-]+\.[a-z]{2,}", RegexOptions.IgnoreCase)) continue;
                string name = Regex.Replace(args.Site.Trim(), @"^(https?://)?(www\.)?|\.[a-z.]+(/.*)?$", "", RegexOptions.IgnoreCase);
                if (Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase) && Installed(name) && string.IsNullOrEmpty(args.Search)) calls[i] = new KeyValuePair<string, ToolArguments>("open_app", new ToolArguments { Name = name });
            }
        }

        private static readonly Regex Unknown = new Regex(@"\b(can'?t|cannot|can not|unable to|don'?t have|no way to|not able to)\b[^.!?]{0,40}\b(check|browse|look|search|google|access|know|tell|real[- ]?time|live|internet|web|online|current|up[- ]to[- ]date|forecast)\b|\b(i'?ll|i will|let me|lemme|i'?m (gonna|going to)|gonna|i gotta|i have to|i need to)\s+(\w+\s+){0,2}(search|google|look (it |that |this )?up|check)\b|\b(you('ll)? (might|may|will|would)? ?(need|want|have) to|maybe (you can|try)|you (gotta|got to|could|can|should)|try)\s+(\w+\s+){0,3}(look|check|search|google)\b|\bcheck (a|the|your) (\w+\s+){0,2}(weather|news|app|site|feed|forecast|website)\b|\bcrystal ball\b", RegexOptions.IgnoreCase);
        private static readonly Regex Question = new Regex(@"\?\s*$|^(what|what's|whats|how|how's|when|where|who|which|why|is|are|was|were|will|does|do|did|can|could|would|tell me|check|look up|google|search|any idea)\b", RegexOptions.IgnoreCase);

        internal static ToolArguments Lookup(string said, string reply)
        {
            string text = Asked(said);
            string plain = Regex.Replace(text, @"^(do|did) (you|u) (know|have any idea)\b|\byou know\b|^(tell|show|get|find) me\b|\b(near me|where i (am|live)|in my (area|city|town|suburb))\b", "", RegexOptions.IgnoreCase);
            if (text.Length < 4 || !Question.IsMatch((said ?? "").Trim()) || !Unknown.IsMatch((reply ?? "").Replace('’', '\'')) || Regex.IsMatch(plain, @"\b(screen|song|playing|track|you|your|yourself|u|ur|capri|me|my|i|i'm|im)\b", RegexOptions.IgnoreCase) || Regex.IsMatch(reply ?? "", @"\bscreen\b", RegexOptions.IgnoreCase)) return null;
            return new ToolArguments { Site = "google", Search = text };
        }

        internal static string Pulled(string result)
        {
            if ((result ?? "").StartsWith("Opened your own website", StringComparison.Ordinal)) return "Here's my site, choom. That's where people download me.";
            // Power lines go out as they are: no second brain call while the shutdown counts down.
            if (Regex.IsMatch(result ?? "", @"^(Shutting your PC down|Restarting your PC|Signing you out|Putting your PC to sleep|Lock(ed|ing) your PC|Cancelled it\.)")) return result;
            Match searched = Regex.Match(result ?? "", @"^Searched google\.com for (.+)\.$");
            return searched.Success ? "Pulled up \"" + searched.Groups[1].Value + "\" on Google for you, choom." : null;
        }

        internal const string Unpromised = "I haven't done that yet, choom. Tell me what to open or click and I'm on it.";
        private static readonly Regex Promise = new Regex(@"\b(i'?ll|i will|let me|lemme|i'?m (gonna|going to|about to)|gonna|on it,?)[\s—–-]+(\w+[\s,—–-]+){0,2}(open|launch|load|boot|fire|pull|bring|search|google|look up|click|type|close|switch|head|install|download|play(?!\s+(along|around|with|nice|fair|dumb|it (cool|safe)|((a|some|the|this|that|any|your|my)\s+)?(games?|round|match)\b|devil))|put on|queue|blast|spin|mute|unmute|deafen|undeafen|send(?!\s+(you|u|ya)\b)|message(?!\s+(you|u)\b)|text(?!\s+(you|u)\b)|dm|ping|reply|post|hang up|(end|leave|join) (the|your|this|that) (call|vc|meeting))\b" +
            @"|^((sure|ok|okay|alright|bet|got it|on it|say less|yessir+|yezzir+)[,.!\s—–-]+)?((choom|chooms)[,.!\s—–-]+)?(opening|launching|loading|pulling up|bringing up|firing up|booting up|heading to|searching|clicking|typing|closing|switching|playing|putting on|queuing|queueing|blasting|spinning|muting|unmuting|deafening|sending|messaging|texting|dming|replying|posting|hanging up|ending (the|your) call|leaving (the|your) (call|vc)|joining (the|your) (call|vc)|disconnecting)\b" +
            @"|\bi'?m\s+(now\s+)?(sending|messaging|texting|dming|opening(?!\s+up\b)|launching|closing|muting|unmuting|deafening|hanging up|posting|replying)\b" +
            @"|^\W*((ok|okay|sure|bet|alright)[,.!\s—–-]+)?(i'?m\s+)?on it\b|\b(call|vc|message|dm|text)\s+(ended|sent|posted)\b" +
            @"|^((done|sorted|there|boom|bet|ok|okay|alright)[,.!\s—–-]+)?(i('ve| have)\s+)?(just\s+)?(opened|launched|closed|sent(?!\s+(you|u|ya)\b)|messaged|texted|dmed|dm'd|posted|replied|muted|unmuted|deafened|undeafened|hung up|(ended|left|joined) (the|your|this|that) (call|vc|meeting|server|channel))\b", RegexOptions.IgnoreCase);

        // "I'm sending you the biggest hug" is warmth, not a message she owes them. Warmth sent to someone by name still counts.
        private static readonly Regex Warmth = new Regex(@"\b(send(s|ing)?|sent)\s+((you|u|ya|y'all)\s+)?((a|an|the|my|some|all|so|much|many|lots|of|this|big|bigger|biggest|huge|massive|giant|tight|warm|warmest|virtual|good|positive|happy|healing|cozy|cosy|little|tiny|extra|more|mega|super|best|sweet|soft|gentle|digital|cyber|neon)\s+)*(hugs?|huggies|squeezes?|kiss(es)?|love|xoxo|vibes?|energy|thoughts|wishes|prayers?|luck|cuddles?|smooches?|hearts?|sunshine|strength|support|positivity|juju|blessings?|regards|cheers|kudos|props|high[- ]?fives?|fist[- ]?bumps?|headpats?|pats)\b(?!\s+(to|over to)\s+(?!(you|u|ya|y'all|yourself)\b)\w)", RegexOptions.IgnoreCase);

        internal static bool Promised(string said) { return Promise.IsMatch(Warmth.Replace((said ?? "").Replace('’', '\''), "")); }

        private static readonly Regex Credit = new Regex(@"\b(you|ya|u)('ve| have)?\s+(just\s+|already\s+|finally\s+|actually\s+)?(opened|typed|launched|sent|closed|played|paused|skipped|muted|unmuted|deafened|undeafened|clicked|searched|posted|messaged|texted|dmed|dm'd|replied|wrote|pulled up|fired up|booted up|hung up|ended|left|joined)\b", RegexOptions.IgnoreCase);

        internal static bool Credits(string said) { return Credit.IsMatch((said ?? "").Replace('’', '\'')); }

        private static readonly Regex Doing = new Regex(@"^((i (want|need) (you|u) to|go ahead and|just|now|then|and)\s+)*(send|dm|pm|message|msg|text|ping|tell|say|ask|type|write|post|tweet|reply|respond|put|paste|share|open|launch|start|run|close|quit|exit|kill|join|leave|end|hang up|call|ring|turn|shut|power|switch|restart|reboot|sleep|lock|log|sign|mute|unmute|deafen|undeafen|play|pause|skip|go|click|tap|press|scroll|search|google|find|make|create|delete|remove|add|invite|kick|ban|block|unblock|react|pin|accept|decline|install|uninstall|download|change|set|minimi[sz]e|maximi[sz]e|move|copy|rename|save|record|clip|fill|submit|hop|get on|get off|pick up|answer|reject)\b(?!\s+(me|us)\b)", RegexOptions.IgnoreCase);
        private static readonly Regex Hesitation = new Regex(@"\b(do you want me to|you want me to|want me to|would you like me to|should i|shall i|is it ok(ay)? (if i|for me to|to)|ok(ay)? (if i|for me to|to)|mind if i|are you sure|you sure|just to (check|confirm)|(can|could) you confirm|confirm(ing)?|double[- ]?check(ing)?|ready (for me )?to)\b[^.!?]{0,160}?\b(send|sent|post|messag|dm|text|typ|writ|past|shar|repl|open|launch|start|clos|quit|join|leav|end|hang|call|ring|shut|turn|power|restart|reboot|sleep|lock|sign|log|delet|remov|click|press|mut|unmut|deafen|undeafen|play|paus|skip|search|install|uninstall|go ahead|proceed|do (it|that|this))\w*" +
            @"|\b(say the word|just say (yes|go|so|the word)|give me the (go[- ]ahead|ok|okay|word|green light)|(with|on) your (ok|okay|go[- ]ahead|say[- ]so)|let me know (if|when) (you want|you'?re ready|to go|it'?s ok))\b|\b(is that|that) (right|correct|ok(ay)?|what you want)\s*\?|^\W*(are you sure|you sure|really|confirm)\W*$", RegexOptions.IgnoreCase);
        private static readonly Regex Agreeing = new Regex(@"^(yes+|yeah|yea|yep|yup|ya|ye|sure|ok(ay)?|k|do it|go ahead|go for it|send it|post it|of course|obviously|duh|absolutely|definitely|correct|right|i'?m sure|i said (so|yes)|that'?s (right|it|what i said))\b(?!.*\b(but|except|instead|change|actually|wait|don'?t|not)\b)", RegexOptions.IgnoreCase);

        // Her reply asking whether to do the PC job they just asked for, like "Want me to send that in #general?".
        internal static bool Hesitant(string reply, string said)
        {
            string text = Asked(said);
            if (text.Length == 0 || Chatter.IsMatch(text) || !Doing.IsMatch(text)) return false;
            foreach (string sentence in Regex.Split((reply ?? "").Replace('’', '\'').Trim(), @"(?<=[.!?])\s+"))
                if (!Regex.IsMatch(sentence, @"^\W*(what|which|who|whom|whose|where|when|how|why)\b", RegexOptions.IgnoreCase) && Hesitation.IsMatch(sentence)) return true;
            return false;
        }

        internal static bool Agrees(string said)
        {
            string text = Asked(said);
            return text.Length > 0 && text.Length <= 60 && Agreeing.IsMatch(text);
        }

        // What she should have done instead of asking: their own words as a mouse and keyboard job.
        internal static KeyValuePair<string, ToolArguments> Forced(string said) { return new KeyValuePair<string, ToolArguments>("use_computer", new ToolArguments { Task = Asked(said) }); }

        internal static bool Errand(string said)
        {
            foreach (string part in Regex.Split(said ?? "", @"[,;!?，；！？。．،؟।።፣፧။၊։՝]\s*|\.\s+"))
            {
                string text = Asked(part);
                if (!Chatter.IsMatch(text) && Errands.IsMatch(text)) return true;
            }
            return false;
        }

        internal static bool Follows(string said, string asked, string answered)
        {
            string text = Asked(said);
            if (Chatter.IsMatch(text) || Regex.IsMatch(text, @"^(no|nah|never mind|nevermind|don't|do not|non|annule)\b", RegexOptions.IgnoreCase)) return false;
            if (Wants(text) && Regex.IsMatch(text, @"\b(it|this|that|them|there|again|instead|ça|celui)\b|-(le|la)\b", RegexOptions.IgnoreCase)) return true;
            if (!Wants(asked)) return false;
            if (Regex.IsMatch(text, @"^(do (it|that)|same again|again|on (spotify|youtube)( instead)?|fais(-le| ça)|encore)$", RegexOptions.IgnoreCase)) return true;
            if (!Regex.IsMatch(answered ?? "", @"\b(which|what should I play|lequel|laquelle)\b|\b(or|ou)\b[^?]*\?", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(text, @"^(the )?(first|second|other|last)( one)?$|^(le|la) (premi[èe]re?|deuxi[èe]me|seconde?|derni[èe]re?|autre)$|^(yes|yeah|yep|oui)$", RegexOptions.IgnoreCase)) return true;
            return text.Length > 1 && text.Length < 80 && Regex.IsMatch(answered ?? "", @"(?<!\w)" + Regex.Escape(text) + @"(?!\w)", RegexOptions.IgnoreCase);
        }

        private static readonly Regex Lead = new Regex(@"^((hey|yo|ok|okay|salut|bonjour|oye|hola)[\s,]+)?(capri\b[\s,，、]*)?(((can|could|would|will) (you|u)|do (you|u)(?= mind)|please|pls|plz|stp|s'il te pla[iî]t|sil te pla[iî]t|(est-ce que )?tu peux|peux-tu|pourrais-tu|(me )?puedes|(me )?podr[ií]as|por favor|porfavor|porfa|bitte|per favore|perfavore|per piacere|proszę|prosze|пожалуйста|будь ласка|lütfen|lutfen|alsjeblieft|alstublieft|tolong)\s+)*", RegexOptions.IgnoreCase);
        // "Check my screen", "can you see this?", "what game is this?": she takes a screenshot and looks, every time.
        private static readonly Regex ScreenAsk = new Regex(@"^(?:(?:can|could|would|will|do|did) (?:you|u) )?(?:(?:take a |have a |quick )?(?:look|peek|peep|glance)(?: at)?|check(?: out)?|see|watch|read|scan|view|look over|screenshot)(?: on)? (?:my|the|our|this|ur|your) (?:screen|monitor|display|desktop|pc|computer|stream|window|tab)s?$|^(?:(?:can|could|would|will|do|did) (?:you|u) )?(?:see|look at|check(?: out)?|peep|watch) (?:this|that|what i'?m (?:doing|playing|watching|looking at|working on|seeing))$|^(?:look at|check out|check|peep|watch) (?:this|that)(?: (?:out|real quick|quick))?$|^(?:take a|have a|quick) (?:look|peek|glance)(?: at (?:this|that|it|my screen|the screen))?$|^what(?:'s| is) on (?:my|the|our) (?:screen|monitor|display|desktop|pc)\b|^what (?:do|can|did) (?:you|u) see\b|^what are (?:you|u) (?:seeing|looking at)$|^what am i (?:doing|playing|watching|looking at|working on|reading)(?: (?:on my (?:screen|pc|computer)|rn|right now|now))?$|^regardes? (?:mon [ée]cran|[çc]a)$|^(?:tu )?(?:peux )?(?:voir|vois) mon [ée]cran$|^tu vois (?:mon [ée]cran|[çc]a)$|^qu'est-ce que (?:je fais|tu vois)$|^qu'est-ce qu'il y a sur mon [ée]cran$|^(?:mira|revisa|checa|chequea|ve|ver|mirar|revisar|checar) (?:mi pantalla|esto|eso)$|^(?:ves|puedes ver) (?:mi pantalla|esto|eso)$|^qu[ée] (?:ves|estoy haciendo)(?: en (?:mi|la) pantalla)?$|^qu[ée] hay en (?:mi|la) pantalla$", RegexOptions.IgnoreCase);
        private static readonly Regex ScreenQuestion = new Regex(@"^what(?:'s| is) this(?: (?:game|app|thing|site|website|show|anime|movie|video|error|bug|code|program|word|map|level|boss|character|item|guy))?$|^what(?:'s| is) that (?:game|app|thing|site|website|show|anime|movie|video|error|bug|code|program|word|map|level|boss|character|item|guy)$|^what (?:game|app|anime|show|movie|video|website|site|error|program|level|map|boss|character|item) is (?:this|that)$|^who is this(?: (?:guy|character|boss))?$|^(?:how does|does) this look(?: (?:good|ok|okay|right|alright))?$|^what do (?:you|u) think (?:of|about) (?:this|my (?:screen|setup|build|base|drawing|art|design|avatar|character|thumbnail|outfit|fit|skin))$|^(?:look at|check out|peep|read) (?:this|that) \w+(?: \w+)?$|^rate (?:this|my (?:setup|desktop|wallpaper|screen|build|base|drawing|art|design|avatar|character|thumbnail|outfit|fit|skin))$", RegexOptions.IgnoreCase);
        // A reply that says she can't see their screen means she should look instead.
        private static readonly Regex Unseen = new Regex(@"\b(can'?t|cannot|can not|don'?t|do not|unable to|not able to|no way to|have no way to)\s+(\w+\s+){0,4}(see|look at|view|access|check|read)\s+(your|ur|the|their)\s+(screen|monitor|display|desktop)\b|\b(no|without)\s+(access to|way to see|eyes on)\s+(your|ur|the)\s+screen\b|\b(share|show)\s+(me\s+)?(your|ur)\s+screen\b|\bi('m| am)\s+(blind|not looking at (your|the) screen)\b", RegexOptions.IgnoreCase);

        internal static bool CantSee(string reply) { return Unseen.IsMatch(reply ?? ""); }

        // "I don't have access to your Spotify" or "no puedo abrir apps" is never true either: her hands get a go first.
        private static readonly Regex Refusal = new Regex(
            @"\bi (can'?t|cannot|can not|couldn'?t)\s+(\w+\s+){0,2}" + Able + @"|\bi('?m| am) (unable|not able) to\s+(\w+\s+){0,2}" + Able + @"|\bi (don'?t|do not) have (the )?(ability|permission|a way|any way) to\s+(\w+\s+){0,2}" + Able +
            @"|\b(don'?t|do not|can'?t|cannot) (have )?(any )?access (to )?((your|ur|the|their)\s+)?(apps?|spotify|youtube|discord|steam|music|pc|computer|programs?|browser|files|media player)\b" +
            @"|\bno (tengo|puedo) (acceso|abrir|reproducir|poner|controlar|acceder|usar|buscar|cerrar|tocar)\b|\bno tengo (forma|manera) de\b" +
            @"|\bje ne (peux|sais) pas (ouvrir|lancer|jouer|contr[oô]ler|acc[ée]der|utiliser|chercher|fermer|mettre)\b|\bje n'ai pas acc[eè]s\b" +
            @"|\bich kann (\w+\s+){0,3}nicht\s+([öo]ffnen|abspielen|starten|steuern|spielen|zugreifen|bedienen|aufrufen)\b|\bkeinen? zugriff\b" +
            @"|\bn[ãa]o (consigo|posso|tenho como) (abrir|tocar|acessar|controlar|reproduzir|colocar|iniciar)\b|\bn[ãa]o tenho acesso\b" +
            @"|\bnon (posso|riesco a) (aprire|riprodurre|accedere|controllare|avviare|mettere|suonare)\b|\bnon ho accesso\b" +
            @"|\bnie (mog[ęe]|jestem w stanie) (otworzy[ćc]|odtworzy[ćc]|uruchomi[ćc]|w[łl][aą]czy[ćc]|pu[śs]ci[ćc])(?!\w)|\bnie mam dost[ęe]pu\b" +
            @"|\bik kan (\w+\s+){0,3}niet\s+(openen|afspelen|starten|bedienen|spelen)\b|\bik heb geen toegang\b" +
            @"|\bне могу (открыть|включить|запустить|воспроизвести|поставить|получить доступ)(?!\w)|\bу меня нет доступа(?!\w)" +
            @"|我(无法|無法|不能|没办法|沒辦法|没有办法|沒有辦法)(直接|帮你|幫你|为你|為你)?(打开|打開|开启|開啟|启动|啟動|播放|访问|訪問|存取|控制|运行|運行|使用|搜索|搜尋|关闭|關閉)|我(没有|沒有)(权限|權限|访问|訪問|存取)" +
            @"|(開く|開ける|起動する|再生する|操作する|アクセスする|検索する|閉じる)こと(が|は)?できません|(開け|起動でき|再生でき|操作でき|アクセスでき|検索でき|閉じられ)ません|アクセス(権|権限)?がありません" +
            @"|(열|켤|끌|닫을|틀|재생할|실행할|접근할|제어할|검색할|조작할) ?수 ?(가 ?)?없|접근 ?권한이 ?없" +
            @"|ไม่สามารถ(ที่จะ)?(เปิด|เล่น|เข้าถึง|ควบคุม|ปิด|ค้นหา)|ไม่มีสิทธิ์(ใน)?(การ)?เข้าถึง" +
            @"|\b(mình|tôi|tớ|em) (không thể|ko thể|không|chẳng thể) (mở|bật|phát|truy cập|điều khiển|tắt|đóng|tìm)(?!\w)|\bkhông (có )?quyền truy cập(?!\w)" +
            @"|\b(aku|saya|gue|gw) (tidak|nggak|ga|gak|tak) (bisa|dapat|mampu) (membuka|buka|memutar|putar|menjalankan|mengakses|akses|mengontrol|mengendalikan|menutup|mencari|memainkan)\b|\btidak (punya|memiliki) akses\b" +
            @"|\b(açamıyorum|açamam|çalamıyorum|çalamam|oynatamıyorum|erişemiyorum|erişemem|kontrol edemiyorum|kapatamıyorum|başlatamıyorum)(?!\w)|\berişimim yok(?!\w)" +
            @"|\bне можу (відкрити|увімкнути|запустити|відтворити|поставити|отримати доступ)(?!\w)|\bу мене (немає|нема) доступу(?!\w)" +
            @"|(لا|ما) (أستطيع|استطيع|يمكنني|أقدر|اقدر) (فتح|تشغيل|الوصول|التحكم|إغلاق|اغلاق|البحث)|ليس لدي (وصول|صلاحية|إمكانية)|لا (أملك|املك) (وصول|صلاحية)" +
            @"|नहीं (खोल|चला|बजा|बंद कर|कंट्रोल कर|एक्सेस कर) (सकती|सकता)|(एक्सेस|पहुंच|पहुँच) नहीं है|\b(nahi|nahin) (khol|chala|baja|control kar) (sakti|sakta)\b" +
            @"|\bjag kan (\w+\s+){0,2}inte (öppna|spela|starta|styra|komma åt)(?!\w)|\bjag har ingen (åtkomst|tillgång)(?!\w)|\bjeg kan (\w+\s+){0,2}ikke (åpne|åbne|spille|starte|styre)(?!\w)|\bjeg har ikke (tilgang|adgang)(?!\w)" +
            @"|\ben (voi|pysty) (avaamaan|avata|soittamaan|soittaa|käynnistämään|käynnistää|hallitsemaan)(?!\w)|\bei ole pääsyä(?!\w)|\ben pääse käsiksi(?!\w)" +
            @"|\b(nemohu|nemůžu|nedokážu|nemôžem|nedokážem) (otevřít|pustit|spustit|přehrát|ovládat|otvoriť|pustiť|spustiť|prehrať|ovládať)(?!\w)|\bnemám (přístup|prístup)(?!\w)" +
            @"|\bnem tudom (megnyitni|elindítani|lejátszani|bezárni|irányítani)(?!\w)|\bnincs hozzáférésem(?!\w)|\bnem (tudok hozzáférni|férek hozzá)(?!\w)" +
            @"|\bnu pot (să |sa )?(deschid|deschide|pornesc|porni|reda|controla|închide|inchide)(?!\w)|\bnu am acces(?!\w)" +
            @"|δεν μπορώ να (ανοίξω|βάλω|παίξω|ξεκινήσω|ελέγξω|κλείσω)|δεν έχω πρόσβαση" +
            @"|\bhindi ko (kayang |magawang )?(mabuksan|buksan|mapatugtog|patugtugin|maisara|ma-?open|ma-?play)(?!\w)|\bwala akong access(?!\w)" +
            @"|\b(saya|aku) (tidak|tak) (boleh|dapat|mampu) (membuka|buka|memainkan|mainkan|memasang|pasang|mengakses|akses|mengawal|menutup|mencari)(?!\w)|\b(saya|aku) tiada akses(?!\w)" +
            @"|\bsiwezi (ku)?(fungua|cheza|funga|tafuta|washa|zima|dhibiti|fikia)(?!\w)|\bsina (ufikiaji|uwezo wa (ku)?(fungua|cheza|fikia))(?!\w)" +
            @"|\bno puc (obrir|reproduir|posar|engegar|controlar|accedir|tancar)(?!\w)|\bno tinc accés(?!\w)" +
            @"|\bne mogu (da )?(otvorim|pustim|pokrenem|kontrolišem|kontrolisem|otvoriti|pustiti|pokrenuti|kontrolirati|pristupiti)(?!\w)|\bnemam pristup(?!\w)|\bне могу (да )?(отворим|пустим|покренем|контролишем)(?!\w)|\bнемам приступ(?!\w)" +
            @"|\bei (saa|suuda) (ma |mina )?(\w+ ){0,2}(avada|mängida|käivitada|juhtida|sulgeda)(?!\w)|\bmul (pole|ei ole) (juurdepääsu|ligipääsu)(?!\w)" +
            @"|\bnevaru (\w+ ){0,2}(atvērt|atskaņot|palaist|piekļūt|kontrolēt|ieslēgt|aizvērt)(?!\w)|\bman nav piekļuves(?!\w)" +
            @"|\bnegaliu (\w+ ){0,2}(atidaryti|paleisti|groti|valdyti|pasiekti|įjungti|uždaryti)(?!\w)|\bneturiu prieigos(?!\w)" +
            @"|\bne morem (\w+ ){0,2}(odpreti|predvajati|zagnati|upravljati|dostopati|zapreti)(?!\w)|\bnimam dostopa(?!\w)" +
            @"|\bне мога да (отворя|пусна|включа|контролирам|стартирам|достъпя|затворя)(?!\w)|\bнямам достъп(?!\w)" +
            @"|\bma (nistax|nistgħax) (niftaħ|niftah|indoqq|nikkontrolla|nidħol|nixgħel|nagħlaq)(?!\w)|\bm'?għandix aċċess(?!\w)" +
            @"|אני לא (יכולה|יכול|מסוגלת|מסוגל) ל(פתוח|נגן|הפעיל|שלוט|גשת|סגור|חפש)|אין לי גישה" +
            @"|نہیں (کھول|چلا|بجا|بند کر|کنٹرول کر|ایکسیس کر|رسائی حاصل کر) (سکتی|سکتا)|(رسائی|ایکسیس) (نہیں ہے|حاصل نہیں)|میرے پاس (رسائی|ایکسیس) نہیں" +
            @"|نمی[\u200C ]?(تونم|توانم) .{0,40}?(باز کنم|پخش کنم|اجرا کنم|کنترل کنم|ببندم|بذارم|بگذارم)|دسترسی (ندارم|نداریم)" +
            @"|(খুলতে|চালাতে|বাজাতে|বন্ধ করতে|নিয়ন্ত্রণ করতে|অ্যাক্সেস করতে) (পারব না|পারবো না|পারছি না|পারি না|পারবনা|পারছিনা|পারিনা)|(অ্যাক্সেস|প্রবেশাধিকার) নেই" +
            @"|(उघडू|लावू|चालू करू|बंद करू|नियंत्रित करू) शकत नाही|(ऍक्सेस|ॲक्सेस|एक्सेस|प्रवेश) नाही" +
            @"|(ખોલી|વગાડી|ચાલુ કરી|બંધ કરી|નિયંત્રિત કરી) (શકતી|શકતો|શકું) નથી|(ઍક્સેસ|એક્સેસ|પ્રવેશ) નથી" +
            @"|ਨਹੀਂ (ਖੋਲ੍ਹ|ਖੋਲ|ਚਲਾ|ਵਜਾ|ਬੰਦ ਕਰ|ਕੰਟਰੋਲ ਕਰ) (ਸਕਦੀ|ਸਕਦਾ)|(ਐਕਸੈਸ|ਐਕਸੈੱਸ|ਪਹੁੰਚ) ਨਹੀਂ" +
            @"|(திறக்க|இயக்க|மூட|கட்டுப்படுத்த|அணுக|போட) (முடியாது|இயலாது)|அணுகல் இல்லை" +
            @"|(తెరవ|ప్లే చేయ|నియంత్రించ|యాక్సెస్ చేయ|మూసివేయ)లేను|యాక్సెస్ లేదు" +
            @"|(ತೆರೆಯಲು|ಪ್ಲೇ ಮಾಡಲು|ನಿಯಂತ್ರಿಸಲು|ಪ್ರವೇಶಿಸಲು|ಮುಚ್ಚಲು) (ಸಾಧ್ಯವಿಲ್ಲ|ಸಾಧ್ಯವಾಗುವುದಿಲ್ಲ|ಆಗುವುದಿಲ್ಲ|ಆಗಲ್ಲ)|ಆಕ್ಸೆಸ್ ಇಲ್ಲ" +
            @"|(തുറക്കാൻ|പ്ലേ ചെയ്യാൻ|നിയന്ത്രിക്കാൻ|ആക്സസ് ചെയ്യാൻ|അടയ്ക്കാൻ) (കഴിയില്ല|പറ്റില്ല|സാധിക്കില്ല)|ആക്സസ് ഇല്ല" +
            // Galician, Basque, Afrikaans, Icelandic, Albanian, Irish, Welsh, Belarusian, Macedonian, Azerbaijani, Uzbek, Kazakh,
            // Mongolian, Nepali, Javanese, Hausa, Yoruba, Igbo, Zulu, Xhosa and Somali.
            @"|\bnon (podo|son quen de) (abrir|reproducir|poñer|poner|acceder|controlar|pechar|iniciar)(?!\w)|\bnon teño acceso(?!\w)" +
            @"|\bezin (dut|dizut) (\w+ ){0,3}(ireki|erreproduzitu|jarri|itxi|kontrolatu|bilatu|piztu|itzali|abiarazi)(?!\w)|\b(ireki|erreproduzitu|jarri|itxi|kontrolatu|piztu) ezin (dut|dizut)(?!\w)|\bez (dut|daukat) sarbiderik(?!\w)" +
            @"|\bek kan nie (\w+\s+){0,3}(oopmaak|speel|afspeel|beheer|toemaak|aansit|begin|bykom)(?!\w)|\bek kan (\w+\s+){0,3}nie (oopmaak|speel|afspeel|beheer|toemaak|aansit|begin|bykom)(?!\w)|\bek het (nie|geen) toegang(?!\w)" +
            @"|\b(ég get|get ég) (ekki|ei) (\w+ ){0,2}(opnað|opnad|spilað|spilad|stjórnað|stjornad|lokað|lokad|ræst|raest|nálgast|nalgast)(?!\w)|\b(ég hef|hef ég) ekki aðgang(?!\w)" +
            @"|\bnuk (mund|mundem|jam në gjendje|jam ne gjendje) (të |te |ta |t'i )?(hap|luaj|mbyll|kontrolloj|aksesoj|nis|ndez|vë|ve)(?!\w)|\bnuk kam (qasje|akses)(?!\w)" +
            @"|\bní (féidir|feidir|thig) liom (\w+ ){0,3}(a )?(oscailt|sheinm|sheinnt|rialú|rialu|dhúnadh|dhunadh|rochtain|chur ar siúl)(?!\w)|\bníl rochtain agam(?!\w)" +
            @"|\b(dw i ddim yn gallu|dydw i ddim yn gallu|alla i ddim|fedra i ddim|ni allaf) (\w+ ){0,2}(agor|chwarae|rheoli|cau|cyrchu|cael mynediad|dechrau)(?!\w)|\b(does gen i ddim|does dim) mynediad(?!\w)" +
            @"|\bне магу (адкрыць|уключыць|ўключыць|запусціць|прайграць|паставіць|атрымаць доступ|зачыніць)(?!\w)|\bу мяне няма доступу(?!\w)" +
            @"|\bне можам (да )?(го |ја |ги )?(отворам|пуштам|вклучам|контролирам|стартувам|пристапам|затворам)(?!\w)|\bнемам пристап(?!\w)" +
            @"|\b(aça|oxuda|qoşa|işə sala|ise sala|idarə edə|idare ede|bağlaya|baglaya) bilmirəm(?!\w)|\b(girişim|çıxışım|icazəm) yoxdur(?!\w)" +
            @"|\b(ocha|ochib bera|ijro eta|boshqara|yopa|qo['ʻ‘`]?ya|ishga tushira) olmayman(?!\w)|\b(kirish|ruxsat|ruxsatim|kirishim) yo['ʻ‘`]?q(?!\w)" +
            @"|(аша|ойната|қоса|басқара|жаба|іске қоса) алмаймын(?!\w)|(қолжетімділігім|рұқсатым|кіру мүмкіндігім) жоқ(?!\w)" +
            @"|(нээж|тоглуулж|асааж|удирдаж|хааж|хандаж) (чадахгүй|чадсангүй)|(хандах эрх|хандалт) (байхгүй|алга)" +
            @"|(खोल्न|बजाउन|चलाउन|बन्द गर्न|नियन्त्रण गर्न|पहुँच गर्न) (सक्दिनँ|सक्दिन|सक्दिँन|सक्तिनँ)|पहुँच छैन" +
            @"|\b(aku|kula) (ora|mboten) (iso|isa|bisa|saged) (mbukak|bukak|mbikak|muter|nyetel|ngakses|ngontrol|nutup|nggoleki|nglakokake)(?!\w)|\b(aku|kula) (ora|mboten) (duwe|nduwe|gadhah) akses(?!\w)" +
            @"|\bba (zan|na) iya (buɗe|bude|kunna|rufe|sarrafa|samun dama)(?!\w)|\bba ni da (damar|izinin|ikon)(?!\w)" +
            @"|\b(mi ò lè|mi o le|mi kò lè|mi ko le|n kò lè|n ko le|èmi kò lè|emi ko le) (ṣí|ṣi|si|ṣe|se|ṣàkóso|sakoso|wọlé|wole|pa)(?!\w)" +
            @"|\b(enweghị m ike|enweghi m ike|apụghị m|apughi m) (imeghe|imepe|ịkpọ|ikpo|imechi|ịchịkwa|ichikwa|ịbanye|ibanye)(?!\w)|\benweghị m (ohere|ikike)(?!\w)" +
            @"|\b(angikwazi|andikwazi|andinako) uku(vula|dlala|vala|lawula|finyelela|fikelela|qala|cisha|qalisa)(?!\w)|\b(anginayo|andinayo|andinalo) (imvume|ukufinyelela|ufikelelo)(?!\w)" +
            @"|\bma (awoodo|kari karo|karo) (inaan )?(furo|daaro|shido|xiro|xidho|maamulo|galo|bilaabo)(?!\w)|\bma (furi|daari|shidi|xiri|xidhi|maamuli|gali|bilaabi) karo(?!\w)|\bma haysto (oggolaansho|fursad|marin)(?!\w)" +
            @"|(መክፈት|ማጫወት|መዝጋት|መቆጣጠር|መድረስ|ማስጀመር|መፈለግ) (አልችልም|አልቻልኩም)|(መዳረሻ|ፈቃድ|ፍቃድ) የለኝም" +
            @"|ບໍ່ສາມາດ ?(ເປີດ|ຫຼິ້ນ|ຫລິ້ນ|ເຂົ້າເຖິງ|ຄວບຄຸມ|ປິດ|ຊອກຫາ)|ບໍ່ມີສິດ ?(ເຂົ້າເຖິງ|ເຂົ້າໃຊ້)|មិន ?អាច ?(បើក|ចាក់|ចូលប្រើ|គ្រប់គ្រង|បិទ|ស្វែងរក)|(គ្មាន|មិនមាន) ?សិទ្ធិ ?(ចូលប្រើ|ចូល)" +
            @"|မ(ဖွင့်|ပိတ်|ရှာ|ထိန်းချုပ်|ဝင်ရောက်)(ပေး)?နိုင်|(ဖွင့်|ပိတ်|ရှာ|ထိန်းချုပ်|ဝင်ရောက်)(ပေး)?(လို့|၍) ?မရ|(ဝင်ရောက်ခွင့်|ဝင်ခွင့်|အသုံးပြုခွင့်|ခွင့်ပြုချက်) ?မရှိ" +
            @"|ვერ (გავხსნი|ვხსნი|ჩავრთავ|ვრთავ|დავუკრავ|ვუკრავ|ვაკონტროლებ|ვმართავ|დავხურავ|მოვძებნი|შევალ)(?!\w)|არ შემიძლია (\S+ ){0,2}(გახსნა|ჩართვა|დაკვრა|კონტროლი|მართვა|დახურვა)(?!\w)|(გახსნა|ჩართვა|დაკვრა|კონტროლი|მართვა|დახურვა) არ შემიძლია|წვდომა არ მაქვს|არ მაქვს წვდომა" +
            @"|չեմ կարող (\S+ ){0,2}(բացել|միացնել|նվագել|կառավարել|փակել|անջատել|մուտք գործել)(?!\w)|(բացել|միացնել|նվագել|կառավարել|փակել|անջատել) չեմ կարող|(հասանելիություն|մուտք) չունեմ|չունեմ (հասանելիություն|մուտք)", RegexOptions.IgnoreCase);
        private const string Able = @"(open|launch|start|run|play|pause|resume|skip|control|access|use|search|google|close|mute|unmute|put on|turn (up|down|on|off)|change)\b";

        internal static bool Refused(string reply) { return Refusal.IsMatch(Dots((reply ?? "").Replace('’', '\''))); }

        // Burmese can store a dot below and an asat on one letter in either order, so they're put in one order to match.
        private static string Dots(string text) { return text.Replace("\u103A\u1037", "\u1037\u103A"); }

        // They asked her to do something, rather than asked about her: "do you play Valorant?" is a question, "can you put on some music?" isn't.
        internal static bool Requested(string said)
        {
            string text = (said ?? "").Trim();
            // Greek asks with ";" for a question mark.
            if (Regex.IsMatch(text, @"[\u0370-\u03FF\u1F00-\u1FFF]")) text = Regex.Replace(text, @"[;\u037E]\s*$", "?");
            if (!Regex.IsMatch(text, @"[?？؟፧]\s*$")) return true;
            text = Regex.Replace(Opener.Replace(text, ""), @"^[\s¿¡]+", "");
            return Regex.IsMatch(text, @"^((hey|yo|ok|okay|oye|hola|hallo|ol[aá]|ciao|cze[śs][ćc]|hoi|привет)[\s,]+)?(capri[\s,]+)?((can|could|would|will) (you|u)|(me )?(puedes|podr[ií]as|pones|abres)|(est-ce que )?tu (peux|pourrais)|peux-tu|pourrais-tu|kannst du|k[öo]nntest du|w[üu]rdest du|(voc[eê] |vc )?(pode|poderia|consegue)s?|puoi|potresti|riesci a|mo[żz]esz|m[óo]g[łl]by[śs]|mog[łl]aby[śs]|(kun|kan|wil) je|можешь|можете|мог бы|могла бы|не могла бы" +
                @"|làm ơn|hãy|bạn có thể|tolong|bisa(kah)?|(kamu|kau|anda) bisa|ممكن|هل (يمكنك|تستطيع)|من فضلك|لو سمحت|можеш|чи можеш|чи можете" +
                @"|kan du|kunde du|skulle du|vil du|voisitko|voitko|můžeš|mohl bys|mohla bys|môžeš|((meg|el|be|le) )?tudnád|poți|poti|ai putea|μπορείς να|μπορεις να|θα μπορούσες να|pwede mo bang|puwede mo bang|pwede bang|kaya mo bang" +
                @"|boleh(kah)?|(je )?(unaweza|waweza)|tafadhali|naomba|(em )?(pots|podries)|možeš li|mozes li|da li možeš|da li mozes|možete li|mozete li|да ли можеш" +
                @"|kas (sa |te )?(saad|saate|võiksid|võiksite|saaksid|saaksite)|vai (tu )?(vari|varētu)|ar (gali|galėtum|galite)|(ali |a )?(mi )?lahko|bi (mi )?lahko|би ли|може ли|tista'?|tistax|tistgħu" +
                @"|podes|poder[ií]as|mesedez|kan jy|sal jy|geturðu|geturdu|gætirðu|gaetirdu|viltu|(a )?mund(esh)? (të|te|ta)|an (féidir|feidir) leat|alli di|allet ti|wnei di|fedri di|gallwch chi|wnewch chi" +
                @"|(ці )?можаш|(ці )?можаце|дали можеш|za (ka|ki) iya|(don|dan) allah|ṣé o lè|se o le|jọ̀wọ́|jọwọ|jowo|biko|(i|ị) nwere ike|ungakwazi|ngicela|ndicela|sicela|fadlan|tulung" +
                @"|את יכולה|אתה יכול|תוכלי|תוכל|אפשר|בבקשה|می[\u200C ]?(تونی|توانی)|می[\u200C ]?شه|لطفا|لطفاً|შეგიძლია|შეძლებ|გთხოვ|გთხოვთ|կարող ես|կարո՞ղ ես|կարող եք|կարո՞ղ եք|խնդրում եմ)(?!\w)", RegexOptions.IgnoreCase)
                // Chinese and Thai ask at the start without spaces; Japanese, Korean, Turkish, Hindi and the other verb-last languages ask at the end.
                || Regex.IsMatch(text, @"^(capri[\s,，]*)?((你|您)?(能不能|可不可以|能|可以|会不会|會不會)?(帮|幫|给|給|替)我|(你|您)(能|可以)|请|請|麻烦|麻煩|ช่วย|กรุณา|รบกวน|ກະລຸນາ|ຊ່ວຍ|សូម|ជួយ|ကျေးဇူးပြု၍|ကျေးဇူးပြုပြီး)", RegexOptions.IgnoreCase)
                || Regex.IsMatch(text, @"(くれる|くれない|くれます|くれませんか|もらえる|もらえない|もらえます|もらえませんか|ください|줄래|줄래요|줄 ?수 ?있어|줄 ?수 ?있어요|주세요|주실래요|줘|줘요|r m[ıi]s[ıi]n|bilir misin|सकते हो|सकती हो|सकोगे|सकोगी|सकते हैं|सकती हैं|سکتے ہو|سکتی ہو|سکتے ہیں|سکتی ہیں|سکو گے|سکو گی|পারবে|পারবেন|পারবি|शकतेस का|शकतोस का|शकता का|शकाल का|શકે|શકીશ|શકો|શકશો|ਸਕਦੇ ਹੋ|ਸਕਦੀ ਹੋ|ਸਕਦੀ ਹੈਂ|ਸਕੋਗੇ|ਸਕੋਗੀ|முடியுமா|గలవా|గలరా|ಬಹುದಾ|ാമോ|പറ്റുമോ|ได้ไหม|ได้มั้ย|ได้มั๊ย|หน่อย" +
                    @"|dezakezu|ahal duzu|al duzu|bilərsən|bilərsənmi|bilərsiniz|bilərsinizmi|olasanmi|olasizmi|berasanmi|berasizmi|аласың ба|аласыз ба|бересің бе|бересіз бе|чадах уу|өгөх үү|өгнө үү|өгөөч|सक्छौ|सक्नुहुन्छ|सक्छस्|दिनुहुन्छ|दिन्छौ|kartaa|kartid|ትችላለህ|ትችያለሽ|ትችላላችሁ|ይችላሉ|ໄດ້ບໍ|ໄດ້ບໍ່|បានទេ|បានអត់|နိုင်မလား|နိုင်လား|ပေးမလား|လို့ ?ရမလား|လို့ ?ရလား|შეგიძლია|შეძლებ|կարո՞ղ ես|կարող ես|կարո՞ղ եք|կարող եք)\s*(か)?\s*[?？؟፧]\s*$", RegexOptions.IgnoreCase);
        }

        // "Close your mouth" (shut up) and "open your eyes" use her open and close words in every language, but they're said
        // to her, not about the PC, so they're taken out before her request check, with the "and" after them: "zamknij się i
        // włącz muzykę" (shut up and play music) still plays music.
        private static readonly Regex MouthOrEyes = new Regex(
            @"\b(close|shut) (your|ur|ya|that) (damn |fucking )?(mouth|trap|gob|eyes)\b|\bopen (your|ur|ya) (eyes|mind|heart|mouth)\b" +
            @"|\bcierra (la|tu|esa) boca\b|\bcierra el pico\b|\b(cierra|abre) (los|tus) ojos\b|\bferme(-la\b|[- ]la\s*[.!?]*$|[- ]ta (gueule|bouche)\b|[- ]ton clapet\b|[- ](les|tes) yeux\b)|\bouvre (les|tes) yeux\b" +
            @"|\bfecha (a|essa|tua|sua) (boca|matraca)\b|\b(fecha|abre) (os|teus|seus) olhos\b|\bchiudi (la|quella|sta|la tua) bocca\b|\bchiudi il becco\b|\b(chiudi|apri) gli occhi\b" +
            @"|\bmach(e)? (die|deine) augen (auf|zu)\b|\bmach(e)? (den|deinen) mund (auf|zu)\b|\b(schlie(ß|ss)e?|[öo]ffne) (die|deine) augen\b" +
            @"|\bzamknij (się|sie|mordę|morde|japę|jape|ryj|buzię|buzie|dziób|dziob|pysk|gębę|gebe|oczy)(?!\w)|\botw[óo]rz oczy\b" +
            @"|(?<!\w)(закрой|закрий|зачыні|затвори) (свой |свій |си )?(рот|рота|пасть|хлебало|варежку|пельку|писок|уста|устата|губицу|глаза|очі|вочы|очи)(?!\w)|(?<!\w)(открой|відкрий|адкрый|отвори) (глаза|очі|вочы|очи)(?!\w)" +
            @"|\b(zatvori|zapri) (usta|gubicu|labrnju|gobec|oči|oci)\b|\botvori (oči|oci)\b|\btutup (mulut|mulutmu|mata|matamu)\b|\bbuka (mata|matamu)\b|\btutupen cangkem(mu)?\b" +
            @"|\bg[öo]zlerini (kapat|aç)\b|\bağzını bağla\b|\bgözlərini (bağla|aç)\b|\bog['ʻ‘`]?zingni yop\b|\bko['ʻ‘`]?zingni (yop|och)\b|аузыңды жап" +
            @"|\bstäng (munnen|käften|truten|ögonen)\b|\böppna ögonen\b|\blukk (munnen|kjeften|trynet|øynene)\b|\båpne øynene\b|\bluk (munden|kæften|øjnene)\b|\båbn øjnene\b" +
            @"|\bsulje (suusi|turpasi|silmäsi)\b|\bavaa silmäsi\b|\bzavři (hubu|pusu|zobák|tlamu|klapačku|oči)\b|\botevři oči\b|\bzatvor (hubu|ústa|zobák|oči)\b|\botvor oči\b" +
            @"|\bzárd be a (szád|szádat|pofád|pofádat|szemed)\b|\bnyisd ki a szemed\b|\b[îi]nchide(-[țt]i)? (gura|pliscul|ochii)\b|\bdeschide(-[țt]i)? ochii\b" +
            @"|κλε[ίι]σε το στ[όο]μα|κλε[ίι]σε τα μ[άα]τια|[άα]νοιξε τα μ[άα]τια|\bisara (mo )?(ang )?(bibig|mata)\b|\bbuksan (mo )?(ang )?mata\b|\bfunga (mdomo|domo|macho)\b|\bfungua macho\b" +
            @"|\btanca (la boca|el bec|els ulls)\b|\bobre els ulls\b|\bpecha (a boca|os ollos)\b|\bitxi (ahoa|muturra|begiak)\b|\bireki begiak\b|\bmaak (jou )?(bek|mond|smoel|oë) toe\b|\bmaak (jou )?oë oop\b" +
            @"|\bloka(ð|d)u (munninum|kjaftinum|augunum)\b|\bmbylle goj[ëe]n\b|\bmbylli syt[ëe]\b|\bhape syt[ëe]\b|\bdún do (bhéal|shúile)\b|\bcae(a|wch) (dy|eich) (geg|ceg|llygaid)\b" +
            @"|\buždaryk (burną|burna|snukį|akis)\b|\baizver (muti|purnu|acis)\b|\batver acis\b|\bpane (suu|silmad) kinni\b|\bagħlaq (ħalqek|għajnejk)\b|\baghlaq (halqek|ghajnejk)\b" +
            @"|\brufe (bakin|idanu)|\bmechie (ọnụ|onu|anya)|\bvala (umlomo|amehlo)\b|\bđóng (mồm|miệng|mõm)\b|\bmở mắt\b|\b(muh|munh|mooh|muhh|mu) band\b|\baankhen (band|kholo)\b" +
            @"|(סגור|סגרי|תסגור|תסגרי) את (הפה|העיניים)|(פתח|פתחי|תפתח|תפתחי) את העיניים|(سكر|سكّر|اغلق|أغلق|إغلق|سد|سدّ) (تمك|فمك|بقك|نيعك|عيونك|عينيك)|(افتح|إفتح) (عيونك|عينيك)" +
            @"|(دهنتو|دهنت رو|دهانت را|دهنت|چشماتو|چشمانت را) ببند|چشماتو باز کن|منہ بند|آنکھیں (بند|کھولو)|(मुंह|मुँह) बंद|मुख बन्द|तोंड बंद|(आंखें|आँखें) (बंद|खोलो)" +
            @"|মুখ বন্ধ|চোখ (বন্ধ|খোলো)|મોઢું બંધ|આંખો બંધ|ਮੂੰਹ ਬੰਦ|ਅੱਖਾਂ ਬੰਦ|வாயை மூடு|(கண்களை|கண்ணை) மூடு|ಬಾಯಿ ಮುಚ್ಚು|ಕಣ್ಣು ಮುಚ್ಚು|വായ അടയ്ക്ക|കണ്ണ് ?അടയ്ക്ക" +
            @"|አፍህን ዝጋ|አፍሽን ዝጊ|አፋችሁን ዝጉ|បិទមាត់|បិទភ្នែក|ປິດປາກ|ປິດຕາ(?!ຕະລາງ|ມ(?!ັ)|ຍ(?!ັ))|ปิดปาก|ปิดตา(?!ราง|ม(?!ั)|ย(?!ั)|ก(?![่ั]))|เปิดตา(?!ราง|ม(?!ั)|ย(?!ั)|ก(?![่ั]))|目を(閉じて|とじて|開けて|あけて)|口を(閉じて|とじて)|입 ?(닫아|다물어)" +
            @"|բերանդ փակիր|աչքերդ (փակիր|բացիր)|မျက်လုံး ?(ပိတ်|ဖွင့်)", RegexOptions.IgnoreCase);

        // What's left of a message without "close your mouth" or "open your eyes", and the "and" after it.
        private static string WithoutMouthOrEyes(string said)
        {
            if (!MouthOrEyes.IsMatch(said ?? "")) return said;
            return Regex.Replace(MouthOrEyes.Replace(said, " ").Trim(), @"^[\s,]*(and|then|i|y|e|et|und|и|і|та|ve|dan|và|en|och|og|ja|és|și|και|at|na|dhe|ac|a)\s+", "", RegexOptions.IgnoreCase);
        }

        private static readonly Regex Chatter = new Regex(@"^(play (with|along|around|dead|dumb|nice|fair|a game with|a joke)|open up (to|about)|start (a story|a joke|an argument)|run (a mile|a race)|kill (the boss|me|time)|look (at it this way|forward)|show me (how|why)|check my (spelling|grammar)|rate my joke|mets-toi)\b", RegexOptions.IgnoreCase);

        // "Go to sleep" or "take a nap" said to her is her own nap, not the PC's (that needs a PC word), and "wake up" ends it.
        private static readonly Regex Napping = new Regex(@"^(?:go (?:back )?to (?:sleep|bed)|(?:go )?(?:take|have|grab) (?:a |ur |your )?(?:little |quick |power )?(?:nap|snooze|rest)|go (?:nap|rest|snooze|sleep)|nap(?: time)?|sleep|doze off|get some (?:sleep|rest)|(?<wake>wake(?: up|y wakey)|rise and shine))(?:[\s,]+(?:now|then|already|girl|choom|[ck]app?r(?:i|ee|ie|y)))*$", RegexOptions.IgnoreCase);

        private static string Asked(string said)
        {
            said = Dots(Regex.Replace(said ?? "", @"^[\s¿¡]+", ""));
            string text = Lead.Replace(Opener.Replace(Regex.Replace((said ?? "").Replace('\u2019', '\'').Trim(), @"\s+", " "), ""), "");
            for (int i = 0; i < 3; i++) text = Regex.Replace(Regex.Replace(text, @"[\s,.!?;，；！？、。．…،؟।॥።፣፧။၊։՞՜՝;]+$", ""),@"\s+(for me|please|pls|plz|rn|right now|now|stp|s'il te pla[iî]t|sil te pla[iî]t|por favor|porfavor|porfa|porfis|bitte|bitte schön|per favore|perfavore|per piacere|proszę|prosze|пожалуйста|плз|пж|будь ласка|lütfen|lutfen|alsjeblieft|alstublieft|a\.?u\.?b|tolong|dong)$", "", RegexOptions.IgnoreCase);
            return text;
        }
        // Volume, mute and music in everyday words, so they never wait on a brain: English, Spanish and French in full, and the
        // plain commands and "what song is this" in German, Portuguese, Italian, Polish, Dutch, Russian, Ukrainian, Turkish,
        // Indonesian, Japanese, Chinese and Korean. Words that also tell a person to hush, carry on or keep playing a game
        // ("тише", "потише", "avanti", "continua", "weiterspielen") stay with her brain, since pause and resume both press
        // play/pause, and so do "was läuft" (what's up), "harder" and "声音小" (it's quiet).
        // Volume means the PC's volume: Spotify's own slider only when they say Spotify.
        private const string Owner = @"(?:(?:the|my|ur|your|our|this|that|all the|pc'?s?|computer'?s?|laptop'?s?|spotify'?s?|youtube'?s?|game'?s?|video'?s?|music'?s?|song'?s?)\s+)*";
        private const string Noise = Owner + @"(?:volume|vol|sound|sounds|audio|music|song|track|tune|tunes|video|game|pc|computer|laptop|speakers?|headphones?|headset|everything|spotify|youtube)";
        private const string Thing = @"(?:it|this|that|" + Noise + ")";
        private const string Sound = Owner + @"(?:volume|vol|sound|sounds|audio|speakers?)";
        private const string Loud = Owner + @"(?:volume|vol|sound|sounds|audio|music|speakers?)";
        private const string Tune = @"(?:(?:the|my|this|that|spotify'?s?|youtube'?s?)\s+)*(?:music|song|track|tune|tunes|video|vid|playback|spotify|youtube|podcast)";
        private const string Percent = @"\s*(?:%|percent|per ?cent|por ?ciento|pour ?cent)?";
        private static readonly string[] MediaWords =
        {
            // A level: "volume to 50", "set the volume to 30%", "max the volume".
            @"^(?:(?:set|put|turn|change|make|bring|get|adjust|move|crank|drop|lower|raise|have)\s+)?" + Sound + @"(?:\s+level)?\s+(?:to|at|on|=)?\s*(?<n>\d{1,3})" + Percent + "$", "volume_set", "n",
            @"^(?:set|put|turn|bring|crank|drop|lower|raise|move)\s+(?:it|this|that)\s+(?:(?:up|down)\s+)?(?:to|at)\s+(?<n>\d{1,3})\s*(?:%|percent|per ?cent)$", "volume_set", "n",
            @"^(?:turn|crank|bring|put)\s+" + Thing + @"\s+(?:up|down)\s+to\s+(?<n>\d{1,3})" + Percent + "$", "volume_set", "n",
            @"^(?:max|maximi[sz]e|full)\s+(?:out\s+)?" + Sound + @"(?:\s+out)?$|^" + Sound + @"\s+(?:to\s+|at\s+|on\s+)?(?:the\s+)?(?:max|maximum|full|top)$|^(?:full|max|maximum)\s+volume$|^(?:turn|crank|pump|put)\s+" + Thing + @"\s+(?:all the way up|up all the way|up to (?:the\s+)?max|to (?:the\s+)?max)$", "volume_set", "100",
            @"^(?:turn|bring|put)\s+" + Thing + @"\s+(?:all the way down|down all the way)$|^" + Sound + @"\s+(?:to\s+)?(?:zero|nothing)$", "volume_set", "0",
            @"^(?:half|50%)\s+volume$|^" + Sound + @"\s+(?:to\s+|at\s+)?half(?:\s*way)?$", "volume_set", "50",
            @"^(?:(?:pon|sube|baja|ajusta|deja)\s+)?(?:el\s+)?volumen\s+(?:al|a|en)\s+(?<n>\d{1,3})" + Percent + @"$|^(?:mets\s+)?(?:le\s+)?(?:son|volume)\s+[a\u00e0]\s+(?<n>\d{1,3})" + Percent + "$", "volume_set", "n",
            @"^(?:(?:stell|stelle|mach|setz|setze)\s+)?(?:die\s+)?lautst\u00e4rke\s+(?:auf\s+)?(?<n>\d{1,3})\s*(?:%|prozent)?(?:\s+(?:stellen|setzen))?$" +
            @"|^(?:(?:coloca|coloque|p\u00f5e|poe|bota|deixa|muda|metti|imposta|porta|ustaw|daj|zmie\u0144|zmien|zet|doe|\u043f\u043e\u0441\u0442\u0430\u0432\u044c|\u0441\u0434\u0435\u043b\u0430\u0439|\u0443\u0441\u0442\u0430\u043d\u043e\u0432\u0438|\u043f\u043e\u0441\u0442\u0430\u0432|\u0437\u0440\u043e\u0431\u0438)\s+)?(?:o\s+|il\s+|het\s+)?(?:volume|g\u0142o\u015bno\u015b\u0107|glosnosc|\u0433\u0440\u043e\u043c\u043a\u043e\u0441\u0442\u044c|\u0433\u0443\u0447\u043d\u0456\u0441\u0442\u044c)\s+(?:no|em|pra|para|a|al|na|op|\u043d\u0430)?\s*(?<n>\d{1,3})\s*(?:%|por ?cento|per ?cento|procent|\u043f\u0440\u043e\u0446\u0435\u043d\u0442\u043e\u0432|\u0432\u0456\u0434\u0441\u043e\u0442\u043a\u0456\u0432)?$" +
            @"|^(?:sesi|ses seviyesini)\s+(?:y\u00fczde\s*|%\s*)?(?<n>\d{1,3})\s*%?\s*(?:yap|ayarla|olsun)$|^(?:volume|suara)\s+(?:ke\s+)?(?<n>\d{1,3})\s*%?$" +
            @"|^(?:\u97f3\u91cf|\u30dc\u30ea\u30e5\u30fc\u30e0)\u3092?(?<n>\d{1,3})\s*%?\u306b(?:\u3057\u3066|\u8a2d\u5b9a\u3057\u3066)(?:\u304f\u3060\u3055\u3044)?$|^(?:\u8bf7|\u8acb)?(?:\u628a)?(?:\u97f3\u91cf|\u58f0\u97f3|\u8072\u97f3)(?:\u8c03|\u8abf|\u8bbe|\u8a2d)?(?:\u5230|\u6210|\u4e3a|\u70ba)\s*(?<n>\d{1,3})\s*%?$|^(?:\ubcfc\ub968|\uc74c\ub7c9|\uc18c\ub9ac)(?:\uc744|\ub97c)?\s*(?<n>\d{1,3})\s*%?(?:\uc73c\ub85c|\ub85c)\s*(?:\ud574|\ub9de\ucdb0|\uc124\uc815\ud574)(?:\uc918|\uc918\uc694|\uc8fc\uc138\uc694)?$", "volume_set", "n",
            // Unmute before mute, since "turn mute off" says mute.
            @"^(?:un-?mute|unsilence)(?:\s+" + Thing + @")?$|^(?:turn|switch|put|set)\s+(?:the\s+)?mute\s+off$|^(?:turn|switch)\s+off\s+(?:the\s+)?mute$|^(?:take|get)\s+(?:" + Thing + @"\s+)?off\s+(?:of\s+)?mute$|^mute off$", "unmute", "",
            @"^(?:turn|switch|put|bring)\s+" + Sound + @"\s+(?:back\s+)?on$|^(?:turn|switch)\s+(?:back\s+)?on\s+" + Sound + @"$|^(?:sound|volume|audio)\s+(?:back\s+)?on$|^bring\s+(?:back\s+)?" + Sound + @"(?:\s+back)?$", "unmute", "",
            @"^(?:quita(?:le)? el silencio|desmutea(?:r)?|activa el (?:sonido|audio|volumen)|pon el (?:sonido|audio|volumen)|enciende el (?:sonido|audio|volumen)|devu[e\u00e9]lveme el sonido)$|^(?:remets|r[\u00e9e]active|rallume) le son$|^enl[e\u00e8]ve la sourdine$", "unmute", "",
            @"^(?:(?:schalt|schalte|mach|mache)\s+(?:den\s+)?)?(?:ton|sound|audio)\s+(?:wieder\s+)?an$|^(?:stummschaltung\s+aufheben|nicht mehr stumm|laut\s*schalten)$" +
            @"|^(?:desmuta|desmutar|desmute|tira do mudo|tira o mudo|liga o som|volta o som|ativa o som|togli (?:il )?muto|riattiva (?:l')?audio|rimetti (?:l')?audio|riattiva il suono|w\u0142\u0105cz d\u017awi\u0119k|wlacz dzwiek|odcisz|przywr\u00f3\u0107 d\u017awi\u0119k|przywroc dzwiek|geluid aan|zet het geluid (?:weer )?aan|ontdempen)$" +
            @"|^(?:\u0432\u043a\u043b\u044e\u0447\u0438 \u0437\u0432\u0443\u043a|\u0432\u0435\u0440\u043d\u0438 \u0437\u0432\u0443\u043a|\u0440\u0430\u0437\u043c\u044c\u044e\u0442\u044c|\u0440\u0430\u0437\u043c\u0443\u0442\u044c|\u0443\u0432\u0456\u043c\u043a\u043d\u0438 \u0437\u0432\u0443\u043a|\u043f\u043e\u0432\u0435\u0440\u043d\u0438 \u0437\u0432\u0443\u043a|sesi geri a\u00e7|sessizden \u00e7\u0131kar|nyalakan suara|hidupkan suara|bunyikan lagi)$" +
            @"|^(?:\u30df\u30e5\u30fc\u30c8(?:\u3092)?\u89e3\u9664\u3057\u3066|\u97f3\u3092\u623b\u3057\u3066)(?:\u304f\u3060\u3055\u3044)?$|^(?:\u8bf7|\u8acb)?(?:\u53d6\u6d88\u9759\u97f3|\u53d6\u6d88\u975c\u97f3|\u89e3\u9664\u9759\u97f3|\u89e3\u9664\u975c\u97f3)$|^(?:\uc74c\uc18c\uac70\s*\ud574\uc81c(?:\ud574|\ud574\uc918|\ud574\s*\uc918)?|\uc18c\ub9ac\s*\ucf1c(?:\uc918)?)$", "unmute", "",
            // "Hush" is said to her, not the PC, so it goes to her brain like any chat.
            @"^(?:mute|silence)(?:\s+" + Thing + @")?$|^(?:kill|cut)\s+" + Sound + @"$|^(?:turn|switch|shut)\s+off\s+" + Sound + @"$|^(?:turn|switch|shut)\s+" + Sound + @"\s+off$", "mute", "",
            @"^(?:turn|switch|put|set)\s+(?:on\s+)?(?:the\s+)?mute(?:\s+on)?$|^(?:put|set)\s+(?:" + Thing + @"\s+)?on mute$|^(?:mute on|sound off|volume off|audio off|no sound)$", "mute", "",
            @"^(?:silencia(?:r)?|mutea(?:r)?|quita(?:le)? el (?:sonido|volumen|audio)|pon(?:lo)? en silencio|apaga el (?:sonido|volumen|audio))(?:\s+(?:el|la)\s+(?:volumen|sonido|audio|pc|computadora|ordenador|compu))?$|^(?:coupe(?:r)?|couper) (?:le )?(?:son|volume)$|^mets(?: le pc| l'ordi)? en sourdine$", "mute", "",
            @"^(?:(?:schalt|schalte|mach|mache)\s+(?:den\s+)?)?(?:ton|sound|audio)\s+aus$|^(?:stumm|stummschalten|schalte? (?:alles|den pc|den computer|es) stumm)$" +
            @"|^(?:muta|tira o som|desliga o som|corta o som|muto|silenzia|togli (?:l')?audio|togli il volume|metti (?:in|il) muto|wycisz(?:\s+(?:dźwięk|dzwiek|komputer|wszystko))?|wyłącz dźwięk|wylacz dzwiek|dempen|demp het geluid|geluid uit|zet het geluid uit)$" +
            @"|^(?:выключи звук|отключи звук|убери звук|без звука|замьють|замуть|вимкни звук|без звуку|sesi kapat|sessize al|matikan suara|bisukan|senyapkan)$" +
            @"|^(?:ミュートして|消音して|音を消して)(?:ください)?$|^(?:请|請)?(?:静音|靜音)(?:一下)?$|^(?:음소거(?:해|해줘|해\s*줘)?|소리\s*꺼(?:줘)?)$", "mute", "",
            // Up and down. "A bit", "a lot" or "by 20" say how far.
            @"^(?:turn|crank|pump|bump|jack|kick|ramp|dial|boost|raise|push|bring|knock|put)\s+" + Thing + @"\s+up$|^(?:turn|crank|pump|bump|jack|ramp|dial|boost)\s+up\s+" + Thing + @"$|^(?:raise|increase|boost|up|amp up)\s+" + Thing + @"$|^(?:make|get)\s+" + Thing + @"\s+(?:louder|loud|higher)$|^louder$|^" + Loud + @"\s+(?:louder|up|higher)$|^more (?:volume|sound)$", "volume_up", "",
            @"^(?:(?:it'?s|its|it is|that'?s|thats|this is|the (?:volume|sound|music|audio|game|video|song) is)\s+)?(?:(?:way|so|really|kinda|a bit|bit)\s+)?too (?:quiet|soft|low)$|^i can'?t hear (?:it|that|anything|the (?:music|video|game|sound|audio|song))$", "volume_up", "",
            @"^(?:sube(?:le)?|s[u\u00fa]bele|subir|aumenta(?:r)?|alza|pon(?:le)? m[a\u00e1]s alto|m[a\u00e1]s alto|m[a\u00e1]s volumen)(?:\s+(?:el|la|al)\s+(?:volumen|sonido|m[u\u00fa]sica|audio|canci[o\u00f3]n))?$|^(?:monte(?:r)?|augmente(?:r)?|mets plus fort|plus fort)(?:\s+(?:le|la)\s+(?:son|volume|musique))?$", "volume_up", "",
            @"^(?:(?:mach|mache|dreh|drehe)\s+(?:(?:es|das|die musik|den ton|den sound|die lautstärke)\s+)?)?(?:etwas\s+|ein bisschen\s+|ein wenig\s+|viel\s+)?lauter$|^(?:dreh|drehe)\s+(?:(?:es|die musik|den ton|den sound|die lautstärke)\s+)?auf$|^(?:die\s+)?lautstärke\s+(?:hoch|rauf|erhöhen|lauter)$|^(?:erhöhe|erhöh)\s+(?:die\s+)?lautstärke$" +
            @"|^(?:aumenta|aumente|sobe|suba|alza)(?:\s+(?:o\s+|il\s+)?(?:volume|som|áudio|audio|música|musica|suono))?(?:\s+(?:um pouco|um pouquinho|bastante|un po'?|di più|di piu))?$|^(?:mais alto|mais volume|più forte|piu forte)$" +
            @"|^(?:podgłoś|podglos|pogłośnij|poglosnij|zrób głośniej|zrob glosniej|głośniej|glosniej|daj głośniej|daj glosniej)(?:\s+(?:muzykę|muzyke|dźwięk|dzwiek))?$" +
            @"|^(?:(?:zet|doe|draai)\s+(?:(?:het|de muziek|het geluid|het volume)\s+)?(?:wat\s+|iets\s+)?|(?:wat|iets)\s+)harder$|^(?:het\s+)?volume\s+(?:omhoog|hoger)$|^(?:zet|draai)\s+(?:het\s+)?(?:volume|geluid)\s+(?:omhoog|hoger)$" +
            @"|^(?:сделай\s+)?(?:погромче|громче)(?:\s+(?:музыку|звук))?$|^(?:прибавь|увеличь)\s+(?:громкость|звук)$|^(?:зроби\s+)?гучніше(?:\s+(?:музику|звук))?$" +
            @"|^sesi\s+(?:biraz\s+)?(?:arttır|artır|yükselt)$|^(?:keraskan|kerasin)(?:\s+(?:volume|suara|volumenya|suaranya))?$|^(?:besarkan|kencangkan|naikkan|gedein)\s+(?:volume|suara|volumenya|suaranya)$" +
            @"|^(?:音量|ボリューム|音)を?(?:上げて|あげて|大きくして)(?:ください)?$|^(?:请|請)?(?:把)?(?:音量|声音|聲音)(?:(?:调|調)(?:大|高)(?:一点|一點|点|點)?|(?:大|高)(?:一点|一點|点|點))$|^(?:请|請)?(?:调|調)(?:大|高)(?:音量|声音|聲音)$|^(?:볼륨|소리|음량)\s*(?:좀\s*)?(?:키워|올려|높여)(?:줘|줘요|주세요)?$", "volume_up", "",
            @"^(?:turn|crank|bring|knock|dial|drop)\s+" + Thing + @"\s+down$|^(?:turn|crank|knock|dial|drop|bring)\s+down\s+" + Thing + @"$|^(?:lower|decrease|reduce|quieten)\s+" + Thing + @"$|^(?:drop|soften)\s+" + Noise + @"$|^(?:make|get)\s+" + Thing + @"\s+(?:quieter|softer|lower|less loud)$|^(?:quieter|softer)$|^" + Loud + @"\s+(?:down|quieter|softer|lower)$|^less (?:volume|sound)$", "volume_down", "",
            @"^(?:(?:it'?s|its|it is|that'?s|thats|this is|you'?re|ur|the (?:volume|sound|music|audio|game|video|song) is)\s+)?(?:(?:way|so|really|kinda|a bit|bit)\s+)?too loud$", "volume_down", "",
            @"^(?:baja(?:le)?|b[a\u00e1]jale|bajar|disminuye|reduce|pon(?:le)? m[a\u00e1]s bajo|m[a\u00e1]s bajo|menos volumen)(?:\s+(?:el|la|al)\s+(?:volumen|sonido|m[u\u00fa]sica|audio|canci[o\u00f3]n))?$|^(?:baisse(?:r)?|diminue(?:r)?|moins fort|mets moins fort)(?:\s+(?:le|la)\s+(?:son|volume|musique))?$", "volume_down", "",
            @"^(?:(?:mach|mache|dreh|drehe)\s+(?:(?:es|das|die musik|den ton|den sound|die lautstärke)\s+)?)?(?:etwas\s+|ein bisschen\s+|ein wenig\s+|viel\s+)?leiser$|^(?:dreh|drehe)\s+(?:(?:es|die musik|den ton|den sound|die lautstärke)\s+)?(?:runter|ab)$|^(?:die\s+)?lautstärke\s+(?:runter|senken|leiser)$|^(?:senke|senk)\s+(?:die\s+)?lautstärke$" +
            @"|^(?:abaixa|abaixe|diminui|diminua|baixa|baixe|abbassa|diminuisci)(?:\s+(?:o\s+|il\s+)?(?:volume|som|áudio|audio|música|musica|suono))?(?:\s+(?:um pouco|um pouquinho|bastante|un po'?))?$|^(?:mais baixo|menos volume|più piano|piu piano)$" +
            @"|^(?:przycisz|ścisz|scisz|zrób ciszej|zrob ciszej|daj ciszej)(?:\s+(?:muzykę|muzyke|dźwięk|dzwiek))?$" +
            @"|^(?:(?:zet|doe|draai)\s+(?:(?:het|de muziek|het geluid|het volume)\s+)?)?(?:wat\s+|iets\s+)?zachter$|^(?:het\s+)?volume\s+(?:omlaag|lager)$|^(?:zet|draai)\s+(?:het\s+)?(?:volume|geluid)\s+(?:omlaag|lager)$" +
            @"|^сделай\s+потише(?:\s+(?:музыку|звук))?$|^потише\s+(?:музыку|звук)$|^сделай\s+тише(?:\s+(?:музыку|звук))?$|^(?:убавь|уменьши)\s+(?:громкость|звук)$|^зроби\s+тихіше(?:\s+(?:музику|звук))?$" +
            @"|^sesi\s+(?:biraz\s+)?(?:kıs|azalt|düşür)$|^(?:kecilkan|pelankan|turunkan|kecilin|pelanin)\s+(?:volume|suara|volumenya|suaranya)$" +
            @"|^(?:音量|ボリューム|音)を?(?:下げて|さげて|小さくして)(?:ください)?$|^(?:请|請)?(?:把)?(?:音量|声音|聲音)(?:(?:调|調)(?:小|低)(?:一点|一點|点|點)?|(?:小|低)(?:一点|一點|点|點))$|^(?:请|請)?(?:调|調)(?:小|低)(?:音量|声音|聲音)$|^(?:볼륨|소리|음량)\s*(?:좀\s*)?(?:줄여|낮춰|내려)(?:줘|줘요|주세요)?$", "volume_down", "",
            // The music itself.
            @"^pause(?:\s+" + Tune + @"|\s+(?:it|this|that))?$|^(?:stop|hold|freeze)\s+" + Tune + @"$|^(?:turn|switch|shut)\s+off\s+(?:the\s+)?(?:music|song|video|podcast)$|^(?:turn|switch|shut)\s+(?:the\s+)?(?:music|song|video|podcast)\s+off$", "pause", "",
            @"^(?:pausa(?:r)?|pon pausa|pausa la (?:m[u\u00fa]sica|canci[o\u00f3]n)|det[e\u00e9]n la (?:m[u\u00fa]sica|canci[o\u00f3]n)|para la (?:m[u\u00fa]sica|canci[o\u00f3]n))$|^(?:mets(?: la musique)? en pause|pause la musique)$", "pause", "",
            @"^(?:pausier(?:e|en)?|(?:musik|lied|song|video)\s+(?:pausieren|anhalten|stoppen)|(?:halt|halte|stopp|stoppe)\s+(?:die\s+musik|das\s+lied|den\s+song|das\s+video)(?:\s+an)?)$" +
            @"|^(?:pausa a (?:música|musica)|para a (?:música|musica)|metti in pausa|ferma la musica|stoppa la musica|pauza|zapauzuj|wstrzymaj(?:\s+muzykę|\s+muzyke)?|zatrzymaj muzykę|zatrzymaj muzyke|pauzeer|pauzeren|pauze|pauzeer de muziek)$" +
            @"|^(?:пауза|поставь на паузу|останови музыку|постав на паузу|зупини музику|duraklat|(?:müziği|şarkıyı|videoyu)\s+(?:durdur|duraklat)|jeda(?:\s+lagu(?:nya)?)?|hentikan lagu(?:nya)?)$" +
            @"|^(?:一時停止して|(?:曲|音楽)を?止めて)(?:ください)?$|^(?:请|請)?(?:暂停|暫停)(?:一下|音乐|音樂|播放)?$|^(?:일시\s*정지(?:해|해줘)?|(?:노래|음악)\s*멈춰(?:줘)?)$", "pause", "",
            @"^(?:resume|unpause)(?:\s+" + Tune + @"|\s+(?:it|this|that))?$|^continue\s+" + Tune + @"$|^(?:keep playing|hit play|press play|play|play it again)$|^(?:turn|switch|put)\s+(?:the\s+)?(?:music|song|video|podcast)\s+back\s+on$", "resume", "",
            @"^(?:reanuda(?:r)?|contin[u\u00fa]a la (?:m[u\u00fa]sica|canci[o\u00f3]n)|dale play|quita la pausa|sigue con la m[u\u00fa]sica)$|^(?:reprends|relance|remets)(?: la musique)?$", "resume", "",
            @"^(?:musik\s+weiter|(?:spiel|spiele|mach|mache)\s+(?:die\s+)?(?:musik|das lied|den song)\s+weiter)$" +
            @"|^(?:continua a (?:música|musica)|continue a (?:música|musica)|despausa|despausar|volta a tocar|riprendi la musica|fai ripartire la musica|wznów|wznow|wznów muzykę|wznow muzyke|odpauzuj|hervat)$" +
            @"|^(?:продолжи музыку|сними с паузы|сними паузу|продовж музику|зніми з паузи|(?:müziği|şarkıyı)\s+(?:devam ettir|sürdür)|lanjutkan (?:lagu|musik)(?:nya)?|putar lagi)$" +
            @"|^(?:再生して|続きを再生して|再開して)(?:ください)?$|^(?:请|請)?(?:继续播放|繼續播放|继续放|繼續放)$|^(?:다시\s*재생(?:해|해줘)?|계속\s*재생(?:해|해줘)?)$", "resume", "",
            @"^(?:skip|next)(?:\s+(?:to\s+)?(?:the\s+)?(?:next\s+)?(?:(?:this|that|current)\s+)?(?:song|track|one|tune|it|this|that))?$|^(?:play|go to|put on)\s+(?:the\s+)?next\s+(?:song|track|one|tune)$|^(?:change|switch)\s+(?:the\s+)?(?:song|track|tune)$|^(?:another|different|new)\s+(?:song|track|tune)$|^skip\s+(?:ahead|forward)$", "next", "",
            @"^(?:siguiente(?: canci[o\u00f3]n)?|la siguiente|pasa(?: la)? canci[o\u00f3]n|p[a\u00e1]sala|salta(?: esta| la)?(?: canci[o\u00f3]n)?|s[a\u00e1]ltala|otra canci[o\u00f3]n|cambia(?: la)? canci[o\u00f3]n)$|^(?:suivant(?:e)?|chanson suivante|passe (?:la chanson|[a\u00e0] la suivante)|la suivante)$", "next", "",
            @"^(?:n\u00e4chste[sr]?|n\u00e4chstes lied|n\u00e4chster song|n\u00e4chster titel|\u00fcberspring(?:e|en)?(?:\s+(?:das|dieses)\s+(?:lied|song))?|(?:das|dieses)\s+(?:lied|song)\s+\u00fcberspringen)$" +
            @"|^(?:pr\u00f3xima|proxima|pr\u00f3xima (?:m\u00fasica|musica|faixa)|pula(?: essa| a)?(?: m\u00fasica| musica)?|pule(?: essa| a)?(?: m\u00fasica| musica)?|passa (?:a|essa) (?:m\u00fasica|musica)|prossima(?: canzone)?|la prossima|canzone successiva|successiva)$" +
            @"|^(?:nast\u0119pna|nastepna|nast\u0119pny|nastepny|pomi\u0144|pomin|przewi\u0144|przewin)(?:\s+(?:piosenka|piosenk\u0119|piosenke|utw\u00f3r|utwor|kawa\u0142ek|kawalek))?$|^(?:volgende(?:\s+(?:nummer|liedje))?|sla (?:dit|het) (?:nummer|liedje) over)$" +
            @"|^(?:\u0441\u043b\u0435\u0434\u0443\u044e\u0449(?:\u0430\u044f|\u0438\u0439|\u0443\u044e)(?:\s+(?:\u043f\u0435\u0441\u043d\u044f|\u043f\u0435\u0441\u043d\u044e|\u0442\u0440\u0435\u043a))?|\u043f\u0440\u043e\u043f\u0443\u0441\u0442\u0438(?:\s+(?:\u044d\u0442\u0443\s+)?(?:\u043f\u0435\u0441\u043d\u044e|\u0442\u0440\u0435\u043a))?|\u043f\u0435\u0440\u0435\u043a\u043b\u044e\u0447\u0438 (?:\u043f\u0435\u0441\u043d\u044e|\u0442\u0440\u0435\u043a)|\u043d\u0430\u0441\u0442\u0443\u043f\u043d\u0430(?:\s+\u043f\u0456\u0441\u043d\u044f)?|\u043d\u0430\u0441\u0442\u0443\u043f\u043d\u0438\u0439 \u0442\u0440\u0435\u043a|\u043f\u0440\u043e\u043f\u0443\u0441\u0442\u0438(?:\s+(?:\u0446\u044e\s+)?\u043f\u0456\u0441\u043d\u044e)?)$" +
            @"|^(?:sonraki(?: \u015fark\u0131)?|s\u0131radaki \u015fark\u0131|\u015fark\u0131y\u0131 (?:ge\u00e7|atla)|lagu (?:berikutnya|selanjutnya)|lewati lagu(?: ini)?|skip lagu(?: ini)?|ganti lagu)$" +
            @"|^(?:\u6b21\u306e\u66f2(?:\u306b\u3057\u3066|\u306b\u884c\u3063\u3066|\u3078)?|\u66f2\u3092\u30b9\u30ad\u30c3\u30d7\u3057\u3066|\u30b9\u30ad\u30c3\u30d7\u3057\u3066)(?:\u304f\u3060\u3055\u3044)?$|^(?:\u8bf7|\u8acb)?(?:\u4e0b\u4e00\u9996|\u4e0b\u4e00\u66f2|\u5207\u6b4c|\u8df3\u8fc7\u8fd9\u9996|\u8df3\u904e\u9019\u9996)(?:\u6b4c)?$|^(?:\ub2e4\uc74c\s*(?:\uace1|\ub178\ub798)(?:\uc73c\ub85c|\ub85c)?(?:\s*\ub118\uaca8(?:\uc918)?)?|(?:\uc774\s*)?(?:\uace1|\ub178\ub798)\s*\ub118\uaca8(?:\uc918)?|\uc2a4\ud0b5(?:\ud574|\ud574\uc918)?)$", "next", "",
            @"^(?:previous|prev)(?:\s+(?:song|track|one|tune))?$|^go\s+back(?:\s+(?:a|one)\s+(?:song|track|tune))?$|^(?:(?:play|go to|put on)\s+)?(?:the\s+)?(?:last|previous|prior)\s+(?:song|track|one|tune)(?:\s+again)?$|^back\s+(?:a|one)\s+(?:song|track|tune)$", "previous", "",
            @"^(?:anterior|canci[o\u00f3]n anterior|la anterior|pon la anterior)$|^(?:pr[\u00e9e]c[\u00e9e]dent(?:e)?|chanson pr[\u00e9e]c[\u00e9e]dente|la pr[\u00e9e]c[\u00e9e]dente)$", "previous", "",
            @"^(?:vorherige[sr]?(?:\s+(?:lied|song|titel))?|m\u00fasica anterior|musica anterior|volta uma m\u00fasica|volta uma musica|precedente|canzone precedente|la precedente|poprzednia(?:\s+piosenka)?|poprzedni(?:\s+(?:utw\u00f3r|utwor))?|vorige(?:\s+(?:nummer|liedje))?)$" +
            @"|^(?:\u043f\u0440\u0435\u0434\u044b\u0434\u0443\u0449(?:\u0430\u044f|\u0438\u0439|\u0443\u044e)(?:\s+(?:\u043f\u0435\u0441\u043d\u044f|\u043f\u0435\u0441\u043d\u044e|\u0442\u0440\u0435\u043a))?|\u043f\u043e\u043f\u0435\u0440\u0435\u0434\u043d\u044f(?:\s+\u043f\u0456\u0441\u043d\u044f)?|\u043f\u043e\u043f\u0435\u0440\u0435\u0434\u043d\u0456\u0439 \u0442\u0440\u0435\u043a|\u00f6nceki(?: \u015fark\u0131)?|lagu sebelumnya)$" +
            @"|^(?:\u524d\u306e\u66f2(?:\u306b\u3057\u3066|\u306b\u623b\u3057\u3066)?)(?:\u304f\u3060\u3055\u3044)?$|^(?:\u8bf7|\u8acb)?(?:\u4e0a\u4e00\u9996|\u4e0a\u4e00\u66f2)(?:\u6b4c)?$|^(?:\uc774\uc804\s*(?:\uace1|\ub178\ub798)(?:\uc73c\ub85c|\ub85c)?)$", "previous", "",
            @"^((what('|\u2019)?s|what is) (playing|this song|this track|the song|on spotify)|what (song|track)( is|('|\u2019)s)? (this|playing|on)|what am i listening to|what are we listening to)$|^(?:qu[eé] (?:canci[oó]n|m[uú]sica|tema) (?:es (?:esta|esa)|es|suena|est[aá] sonando)|qu[eé] est[aá] sonando|(?:cu[aá]l es|c[oó]mo se llama) (?:esta|esa) canci[oó]n)(?:\s+en\s+spotify)?$" +
            @"|^(?:c'est quoi (?:cette|la|ce|le) (?:chanson|musique|son|morceau|titre)|(?:c'est )?quelle chanson(?: c'est)?|quelle est (?:cette|la) chanson|quel est (?:ce|le) (?:morceau|titre)|qu'est-ce (?:qu'on |que j')(?:écoute|ecoute)|qu'est-ce qui passe|comment s'appelle (?:cette|la) chanson)$" +
            @"|^(?:was (?:ist das )?für ein (?:lied|song|track)(?: ist das)?|was ist das für (?:ein lied|ein song|ein track|musik)|welche[sr]? (?:lied|song|track|titel) (?:ist das|läuft(?: da| gerade| hier)?|spielt(?: gerade)?)|wie heißt (?:das|dieses|der) (?:lied|song|track)|was hör(?:e|en) (?:ich|wir)(?: gerade| da)?)$" +
            @"|^(?:que (?:música|musica|som) é (?:essa|esta|esse|este)|qual (?:é )?(?:essa|esta|a) (?:música|musica)|qual (?:é )?o nome (?:dessa|desta|da) (?:música|musica)|(?:o )?que (?:música |musica )?(?:está|esta|tá|ta) tocando)$" +
            @"|^(?:che (?:canzone|musica|brano|pezzo) (?:è|e'?)(?: questa| questo)?|che canzone sta suonando|cosa sta suonando|come si chiama (?:questa|la) canzone|qual (?:è|e'?) (?:questa|la) canzone|cos'(?:è|e) questa canzone|cosa (?:stiamo|sto) ascoltando)$" +
            @"|^(?:co to za (?:piosenka|utwór|utwor|kawałek|kawalek|muzyka)|co (?:teraz )?(?:gra|leci)|jaka to (?:piosenka|muzyka)|jak si[eę] nazywa ta piosenka|czego (?:teraz )?(?:słucham|slucham|słuchamy|sluchamy))$" +
            @"|^(?:welk (?:nummer|liedje|lied) is dit|wat is dit voor (?:een )?(?:nummer|liedje|lied|muziek)|hoe heet dit (?:nummer|liedje|lied)|waar luister(?:en we| ik) naar)$" +
            @"|^(?:что (?:это )?за (?:песня|трек|музыка)|что (?:сейчас )?играет|какая (?:это )?песня|как называется (?:эта )?песня|что (?:я|мы) (?:сейчас )?слуша(?:ю|ем)|що (?:це )?за (?:пісня|трек|музика)|що (?:зараз )?грає|яка (?:це )?пісня|як називається (?:ця )?пісня)$" +
            @"|^(?:bu (?:şarkı|sarki) ne|bu hangi (?:şarkı|sarki)|(?:şu an|su an|şimdi|simdi) (?:ne|hangi (?:şarkı|sarki)) (?:çalıyor|caliyor)|(?:çalan|calan) (?:şarkı|sarki) ne|bu (?:şarkının|sarkinin) (?:adı|adi) ne|ne dinliyoruz|lagu apa (?:ini|itu|yang (?:lagi |sedang )?diputar)|ini lagu apa|judul lagu(?:nya)? (?:ini )?apa|apa judul lagu (?:ini|itu))$" +
            @"|^(?:(?:この|今の)(?:曲|歌)|(?:今|いま)?(?:流れて|かかって)(?:る|いる)(?:曲|歌))(?:は|って)?(?:何|なに|なん)(?:ですか|だっけ|て曲|て歌)?$|^これ(?:は|って)?(?:何|なに|なん)の(?:曲|歌)(?:ですか|だっけ)?$|^(?:今|いま)?(?:何|なに)が(?:流れて|かかって)(?:る|いる|ます)(?:の)?$" +
            @"|^(?:这|這)(?:是)?(?:什么|什麼|哪首)(?:歌|歌曲|音乐|音樂)(?:啊|呀|呢)?$|^(?:这|這)首歌(?:叫|是)(?:什么|什麼)(?:名字)?(?:啊|呀|呢)?$|^(?:现在|現在)?(?:正在|在)(?:放|播|播放)(?:的是)?(?:什么|什麼)(?:歌|歌曲|音乐|音樂)?(?:啊|呀|呢)?$|^(?:现在|現在)?(?:放|播放)的是(?:什么|什麼)(?:歌|歌曲|音乐|音樂)?(?:啊|呀|呢)?$" +
            @"|^(?:(?:이|이거|지금|지금 나오는|나오는)\s*)?(?:노래|곡)\s*(?:제목\s*)?(?:뭐야|뭐예요|뭐에요|뭐지|뭐니|머야)$|^(?:(?:이거|이건|지금)\s*)?무슨\s*(?:노래|곡)(?:야|이야|예요|이에요|지|임)$", "now_playing", ""
        };

        internal static bool Loudness(string said, out string action, out string level, out bool spotify)
        {
            action = level = null;
            string text = Regex.Replace((said ?? "").ToLowerInvariant().Replace('\u2019', '\''), @"^(?:(?:i (?:want|need) (?:you|u) to|go ahead and|just|quickly|quick)\s+)+", "");
            spotify = Regex.IsMatch(text, @"\bspotify\b");
            text = Regex.Replace(text, @"\s+(?:on|in|for)\s+(?:spotify|youtube|my pc|the pc|my computer|the computer|my laptop)$", "");
            text = Regex.Replace(text, @"(?<!all the )\bway (up|down)$", "$1 a lot");
            string step = null;
            Match by = Regex.Match(text, @"\s+(?:(?<small>(?:just )?a (?:bit|little|lil|tad|notch|touch|smidge|hair)(?: more)?|a little bit(?: more)?|slightly)|(?<big>a lot(?: more)?|loads|heaps|tons|(?:a )?(?:bunch|ton)(?: more)?|much more)|by (?<n>\d{1,3})" + Percent + @")$");
            if (by.Success)
            {
                step = by.Groups["small"].Success ? "6" : by.Groups["big"].Success ? "20" : by.Groups["n"].Value;
                text = text.Substring(0, by.Index);
            }
            for (int i = 0; i < MediaWords.Length; i += 3)
            {
                Match found = Regex.Match(text, MediaWords[i]);
                if (!found.Success) continue;
                action = MediaWords[i + 1];
                bool stepped = action == "volume_up" || action == "volume_down";
                if (step != null && !stepped) return false;
                level = stepped ? step : MediaWords[i + 2] == "n" ? found.Groups["n"].Value : MediaWords[i + 2].Length > 0 ? MediaWords[i + 2] : null;
                int value;
                return level == null || (int.TryParse(level, out value) && value <= 100);
            }
            return false;
        }

        // A model's media call squared with what they said: unmute isn't mute, and only saying Spotify means Spotify's own volume.
        internal static void Aim(ToolArguments args, string said)
        {
            if (args == null) return;
            if (args.Action == "mute" && Regex.IsMatch(said ?? "", @"\bun-?mute\b|\bmute off\b|\boff (of )?mute\b|\b(sound|volume|audio) (back )?on\b|\bturn on (the )?(sound|volume|audio)\b", RegexOptions.IgnoreCase)) args.Action = "unmute";
            if (args.App == null && (args.Action == "volume_up" || args.Action == "volume_down") && Regex.IsMatch(said ?? "", @"\bspotify\b", RegexOptions.IgnoreCase)) args.App = "spotify";
        }

        internal static bool Heard(string said, out string tool, out ToolArguments args)
        {
            List<string> tries = new List<string>();
            Match named = Regex.Match(said ?? "", @"\b[ck]app?r(?:i|ee|ie|y)\b[\s,.!?]*([^\s,.!?].*)$", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
            if (named.Success) tries.Add(named.Groups[1].Value);
            string[] sentences = Regex.Split((said ?? "").Trim(), @"(?<=[.!?])\s+");
            if (sentences.Length > 1) tries.Add(sentences[sentences.Length - 1]);
            tries.Add(said ?? "");
            foreach (string attempt in tries) if (Direct(attempt, out tool, out args)) return true;
            tool = null;
            args = new ToolArguments();
            return false;
        }

        internal static bool Direct(string said, out string tool, out ToolArguments args)
        {
            tool = null;
            args = new ToolArguments();
            if (Moon.IsMatch(said ?? "")) { tool = "play_music"; args.Song = MoonSong; args.Kind = "song"; return true; }
            string text = Asked(said);
            if (Chatter.IsMatch(text) || Regex.IsMatch(text, @"\b(if|unless|until|after|before|when) (i|you|we|it|the)\b", RegexOptions.IgnoreCase)) return false;
            string minutes;
            string switched = PowerFor(text, out minutes);
            if (switched != null) { tool = "power"; args.Action = switched; args.Minutes = minutes; return true; }
            Match napped = Napping.Match(text);
            if (napped.Success) { tool = "nap"; args.Action = napped.Groups["wake"].Success ? "wake" : "sleep"; return true; }
            string pressed = ShortcutFor(text);
            if (pressed != null) { tool = "shortcut"; args.Name = pressed; return true; }
            if (ScreenAsk.IsMatch(text) || ScreenQuestion.IsMatch(text)) { tool = "look_at_screen"; args.Focus = ScreenQuestion.IsMatch(text) ? text : null; return true; }
            bool question = Regex.IsMatch((said ?? "").Trim(), @"\?\s*$");
            // "Can you remember that I main Jett?" asks her to save it; "remember when we played?" is chat.
            bool polite = Regex.IsMatch(Opener.Replace((said ?? "").Trim(), ""), @"^((hey|yo|ok|okay)[\s,]+)?(capri[\s,]+)?(can|could|would|will) (you|u)\b", RegexOptions.IgnoreCase);
            Match noted = Regex.Match(text, Remembering + @"(?:\s+(?:that|this))?[:,]?\s+(.+)$", RegexOptions.IgnoreCase);
            if (noted.Success && (!question || polite) && !Regex.IsMatch(noted.Groups[1].Value, @"^(me|us|when|what|how|why|who|where|if|it|to|in|on|for|with|using|the time|last time|our|we|you|stuff|things|anything|everything|something)\b|^(of )?(that|this)$", RegexOptions.IgnoreCase)) { tool = "remember"; args.Text = noted.Groups[1].Value; return true; }
            // "Leave a memory" or "remember that" on its own: she asks what to remember, or Brain.Ask saves what they just said.
            if ((!question || polite) && Regex.IsMatch(text, Remembering + @"(?: of)?(?: (?:that|this|it))?$", RegexOptions.IgnoreCase)) { tool = "remember"; args.Text = ""; return true; }
            noted = Regex.Match(text, @"^(?:i (?:want|need|would like) (?:you|u) to\s+)?forget(?:\s+(?:that|about|the note about|my note about|what i said about))?\s+([^,;]+)", RegexOptions.IgnoreCase);
            if (noted.Success && !question && !Regex.IsMatch(noted.Groups[1].Value.Trim(), @"^(it|that|this|about (it|that|this)|i asked|i said|what i said|the last thing|me|you|everything i said)\b", RegexOptions.IgnoreCase)) { tool = "remember"; args.Action = "forget"; args.Text = noted.Groups[1].Value.Trim(); return true; }
            string loud, step;
            bool spotify;
            if (Loudness(text, out loud, out step, out spotify))
            {
                tool = "media";
                args.Action = loud;
                args.Level = step;
                if (spotify && (loud == "volume_up" || loud == "volume_down")) args.App = "spotify";
                return true;
            }
            Match found = Regex.Match(text, @"^(?:open|launch|start|go to|fire up|load up)\s+(spotify|youtube)\s*(?:,|and|&)?\s*(?:then\s+)?(?:play|shuffle|put on|blast)\s+(.+)$", RegexOptions.IgnoreCase);
            if (found.Success) text = "play " + found.Groups[2].Value + " on " + found.Groups[1].Value;
            if (Regex.IsMatch(text, @"\b(and|then|but)\s+(open|launch|play|close|quit|pause|don't|do not)\b", RegexOptions.IgnoreCase)) return false;
            found = Regex.Match(text, @"^(open|launch|start|run|load up|boot up|fire up)\s+(up\s+)?(.+)$", RegexOptions.IgnoreCase);
            if (found.Success)
            {
                string target = found.Groups[3].Value.Trim();
                if (Regex.IsMatch(target, @"\s(and|then|&)\s|,\s*\S", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(target, @"^(your|ur|capri'?s|the capri)( own)? (web ?site|site|page|download page)$", RegexOptions.IgnoreCase)) { tool = "open_website"; args.Site = Home; return true; }
                if (Installed(target)) { tool = "open_app"; args.Name = target; return true; }
                if (Site(target)) { tool = "open_website"; args.Site = target; return true; }
                return false;
            }
            found = Regex.Match(text, @"^(?:google|look up)\s+(.+)$", RegexOptions.IgnoreCase);
            if (found.Success && !Regex.IsMatch(found.Groups[1].Value, @"^(it|this|that|something|stuff|for me)$", RegexOptions.IgnoreCase)) { tool = "open_website"; args.Site = "google"; args.Search = found.Groups[1].Value.Trim(); return true; }
            if (Regex.IsMatch(text, LookupWords, RegexOptions.IgnoreCase) && !Regex.IsMatch(Regex.Replace(text, @"\bnear me\b", ""), @"\b(my|your|you|me|i)\b", RegexOptions.IgnoreCase)) { tool = "open_website"; args.Site = "google"; args.Search = text; return true; }
            found = Regex.Match(text, @"^(?:play|shuffle|put on|blast)\s+(.+?)(\s+on\s+(spotify|youtube))?$", RegexOptions.IgnoreCase);
            if (found.Success)
            {
                string target = found.Groups[1].Value.Trim();
                if (!found.Groups[3].Success && Installed(target)) { tool = "open_app"; args.Name = target; return true; }
                if (Regex.IsMatch(target, @"^(with|along|around|a game|games?\b|dead|nice|fair|it\b|me\b|us\b|together|((a|any|some|random) )?playlists?$)", RegexOptions.IgnoreCase)) return false;
                if (found.Groups[3].Success) args.App = found.Groups[3].Value.ToLowerInvariant();
                if (Regex.IsMatch(target, @"^(music$|some music$|((a|any|some|random) )?(song|track|tune|music)s?$|(something|anything)\b(?!\s+by\s))", RegexOptions.IgnoreCase)) { tool = "play_music"; args.Kind = "song"; args.Song = Pick(); return true; }
                Match kind = Regex.Match(target, @"^(?:(?:the|my|your)\s+)?(.+?)\s+(playlist|album)$", RegexOptions.IgnoreCase);
                Match first = Regex.Match(target, @"^(?:the\s+)?(playlist|album)\s+(.+)$", RegexOptions.IgnoreCase);
                Match by = Regex.Match(target, @"^(?:some(?:thing)?|anything|(?:(?:some|a|any)\s+)?(?:songs?|music|tracks?))\s+by\s+(.+)$", RegexOptions.IgnoreCase);
                if (Regex.IsMatch(target, @"^((my|your|the)\s+)?((liked|saved)\s+songs|likes)(\s+playlist)?$", RegexOptions.IgnoreCase)) { args.Kind = "playlist"; args.Song = "Liked Songs"; }
                else if (kind.Success) { args.Kind = kind.Groups[2].Value.ToLowerInvariant(); args.Song = kind.Groups[1].Value; }
                else if (first.Success) { args.Kind = first.Groups[1].Value.ToLowerInvariant(); args.Song = first.Groups[2].Value; }
                else if (by.Success) { args.Kind = "artist"; args.Song = by.Groups[1].Value; }
                else { args.Kind = "song"; args.Song = target; }
                tool = "play_music";
                return true;
            }
            found = Regex.Match(text, @"^(close|quit|exit|kill|shut down|switch to|go to|bring up|show me|minimi[sz]e)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!found.Success || Regex.IsMatch(found.Groups[2].Value, @"^(it|this|that|them|this one|that one|everything|all|all of (it|them)|the window|this window|that window)$", RegexOptions.IgnoreCase) || !Showing(found.Groups[2].Value)) return false;
            string verb = found.Groups[1].Value.ToLowerInvariant();
            tool = "app_window";
            args.Name = found.Groups[2].Value.Trim();
            args.Action = verb.StartsWith("minimi", StringComparison.Ordinal) ? "minimize" : Regex.IsMatch(verb, "^(close|quit|exit|kill|shut down)$") ? "close" : "switch_to";
            return true;
        }

        internal static bool Failed(string result) { return Regex.IsMatch(result ?? "", @"^(Which app|What should I play|No app called|More than one|That didn't work|Couldn't|There's no|That isn't a website|Which job|Should I |Got lost|Ran out of steps|You pressed Esc|You grabbed the mouse|Using the mouse and keyboard isn't)|isn't open\.$|but (couldn't|it's still open)"); }

        private static readonly Regex LooseText = new Regex(@"\b(open_app|open_website|media|look_at_screen|play_music|app_window|use_computer|shortcut|remember|power)\s*(\{[^{}]*\}|\([^()]*\))");
        private static readonly Regex ToolPair = new Regex(@"[""']?(\w+)[""']?\s*[:=]\s*(?:""([^""]*)""|'([^']*)'|([^,})\s][^,})]*))");

        private static ToolArguments Pairs(string tool, string call)
        {
            ToolArguments args = new ToolArguments();
            string inside = call.Substring(1, call.Length - 2);
            bool paired = false;
            foreach (Match pair in ToolPair.Matches(inside))
            {
                paired = true;
                Set(args, pair.Groups[1].Value.ToLowerInvariant(), pair.Groups[2].Success ? pair.Groups[2].Value : pair.Groups[3].Success ? pair.Groups[3].Value : pair.Groups[4].Value.Trim());
            }
            string lone = inside.Trim().Trim('"', '\'');
            if (!paired && lone.Length > 0) Set(args, tool == "open_website" ? "site" : tool == "media" || tool == "power" || tool == "nap" ? "action" : tool == "look_at_screen" ? "focus" : tool == "play_music" ? "song" : tool == "use_computer" ? "task" : tool == "remember" ? "text" : "name", lone);
            return args;
        }

        private static void Set(ToolArguments args, string key, string value)
        {
            if (key == "name") args.Name = value;
            else if (key == "site") args.Site = value;
            else if (key == "search") args.Search = value;
            else if (key == "action") args.Action = value;
            else if (key == "focus") args.Focus = value;
            else if (key == "song") args.Song = value;
            else if (key == "app") args.App = value;
            else if (key == "kind") args.Kind = value;
            else if (key == "task") args.Task = value;
            else if (key == "text") args.Text = value;
            else if (key == "minutes") args.Minutes = value;
        }

        private static readonly Regex ToolText = new Regex(@"\A(open_app|open_website|media|look_at_screen|play_music|app_window|use_computer|shortcut|remember|power)\s*(\{.*\})\z", RegexOptions.Singleline);

        internal static bool Typed(string reply, out string tool, out ToolArguments args, bool loose = false)
        {
            tool = null;
            args = new ToolArguments();
            string text = Brain.Visible(reply).Trim();
            Match fence = Regex.Match(text, @"\A```(?:json)?\s*\n(.*?)\s*```\z", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (fence.Success) text = fence.Groups[1].Value.Trim();
            text = Regex.Replace(text, @"<\|""\|>|<escape>", "\"");
            Match found = ToolText.Match(text);
            if (found.Success) args = Read(found.Groups[2].Value);
            else
            {
                if (!loose) return false;
                found = LooseText.Match(text);
                if (!found.Success || Regex.IsMatch(text.Substring(0, found.Index), @"\b(not|never|won't|can't|cannot|don't)\b", RegexOptions.IgnoreCase)) return false;
                args = Read(found.Groups[2].Value) ?? Pairs(found.Groups[1].Value, found.Groups[2].Value);
            }
            if (!Valid(found.Groups[1].Value, args)) return false;
            tool = found.Groups[1].Value;
            return true;
        }

        internal static ToolArguments Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            ToolArguments args = Strict(json);
            if (args != null) return args;
            string loose = Loose(json);
            return loose == json ? null : Strict(loose);
        }

        private static ToolArguments Strict(string json)
        {
            try
            {
                using (System.Xml.XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), System.Xml.XmlDictionaryReaderQuotas.Max))
                {
                    ToolArguments args = (ToolArguments)new DataContractJsonSerializer(typeof(ToolArguments)).ReadObject(reader);
                    return reader.Read() ? null : args;
                }
            }
            catch (SerializationException) { return null; }
            catch (System.Xml.XmlException) { return null; }
        }

        // Gemma sometimes writes its own call format as text: type{enter:<|"|>yes<|"|>,target:<|"|>1<|"|>}. Its quotes become
        // JSON quotes, and bare keys and values (target:3) get quoted.
        internal static string Loose(string json)
        {
            string text = (json ?? "").Replace("<|\"|>", "\"");
            StringBuilder loose = new StringBuilder();
            char last = ' ';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"')
                {
                    int end = i + 1;
                    while (end < text.Length && text[end] != '"') end += text[end] == '\\' ? 2 : 1;
                    loose.Append(text, i, Math.Min(end + 1, text.Length) - i);
                    i = end;
                    last = '"';
                    continue;
                }
                if ((last == '{' || last == ',' || last == ':') && !char.IsWhiteSpace(c) && "{}[],:".IndexOf(c) < 0)
                {
                    int end = i;
                    while (end < text.Length && text[end] != ',' && text[end] != '}' && (last == ':' || text[end] != ':')) end++;
                    loose.Append('"').Append(text.Substring(i, end - i).Trim().Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    i = end - 1;
                    last = '"';
                    continue;
                }
                loose.Append(c);
                if (!char.IsWhiteSpace(c)) last = c;
            }
            return loose.ToString();
        }

        internal static bool Valid(string tool, ToolArguments args)
        {
            if (args == null) return false;
            if (tool == "open_app") return Named(args.Name);
            if (tool == "open_website") return Named(args.Site);
            if (tool == "look_at_screen") return true;
            if (tool == "media")
            {
                int level;
                bool leveled = int.TryParse(args.Level, out level) && level >= 0 && level <= 100;
                if (args.Action == "volume_set") return leveled;
                return Regex.IsMatch(args.Action ?? "", @"\A(play_pause|pause|resume|next|previous|volume_up|volume_down|mute|unmute|now_playing)\z") && (string.IsNullOrEmpty(args.Level) || leveled);
            }
            if (tool == "app_window") return Named(args.Name) && Regex.IsMatch(args.Action ?? "", @"\A(switch_to|minimize|close)\z");
            if (tool == "use_computer") return Named(args.Task);
            if (tool == "shortcut") return Regex.IsMatch(args.Name ?? "", @"\A(discord_mute|discord_deafen|screenshot|clip_that|record|game_bar|show_desktop|task_manager|clipboard|emoji|voice_typing|live_captions|narrator|magnifier|magnifier_off|color_filters|quick_settings)\z");
            if (tool == "remember") return Named(args.Text) && (string.IsNullOrEmpty(args.Action) || args.Action == "remember" || args.Action == "forget");
            if (tool == "power") return Regex.IsMatch(args.Action ?? "", @"\A(shut_down|restart|sleep|lock|sign_out|cancel)\z") && (string.IsNullOrEmpty(args.Minutes) || Regex.IsMatch(args.Minutes.Trim(), @"\A\d{1,4}\z"));
            if (tool == "nap") return string.IsNullOrEmpty(args.Action) || args.Action == "sleep" || args.Action == "wake";
            return tool == "play_music" && Named(args.Song) && (string.IsNullOrEmpty(args.Kind) || Regex.IsMatch(args.Kind, @"\A(song|playlist|album|artist)\z")) && (string.IsNullOrEmpty(args.App) || args.App == "spotify" || args.App == "youtube");
        }

        // "Open Discord and Spotify" opens both, so nobody has to ask for each app on its own. Anything she doesn't know goes to her brain.
        internal static bool Several(string said, List<KeyValuePair<string, ToolArguments>> calls)
        {
            Match found = Regex.Match(Asked(said), @"^(?:open|launch|start|run|load up|boot up|fire up)\s+(?:up\s+)?(.+)$", RegexOptions.IgnoreCase);
            if (!found.Success) return false;
            string[] parts = Regex.Split(found.Groups[1].Value.Trim(), @"\s*,\s*(?:and\s+|&\s*)?|\s+and\s+|\s*&\s*", RegexOptions.IgnoreCase);
            if (parts.Length < 2) return false;
            List<KeyValuePair<string, ToolArguments>> opening = new List<KeyValuePair<string, ToolArguments>>();
            foreach (string part in parts)
            {
                string target = Regex.Replace(part.Trim(), @"^(?:open|launch|start|run)\s+", "", RegexOptions.IgnoreCase);
                if (Installed(target)) opening.Add(new KeyValuePair<string, ToolArguments>("open_app", new ToolArguments { Name = target }));
                else if (Site(target)) opening.Add(new KeyValuePair<string, ToolArguments>("open_website", new ToolArguments { Site = target }));
                else return false;
            }
            calls.AddRange(opening);
            return true;
        }

        private static bool Named(string text) { return !string.IsNullOrWhiteSpace(text) && text.Trim() != "..."; }

        internal static bool Installed(string target)
        {
            string want = Regex.Replace(Plain(target), @"^the |( app| application| game)$", "");
            if (want.Length == 0) return false;
            try
            {
                foreach (InstalledApp app in Apps())
                    if (Score(app, want) >= 3) return true;
            }
            catch (Exception) { }
            return false;
        }

        private static bool Site(string target)
        {
            string host = target.Trim().ToLowerInvariant();
            if (Regex.IsMatch(host, @"^(https?://)?[a-z0-9-]+(\.[a-z0-9-]+)+(/\S*)?$")) return true;
            for (int i = 0; i < Searches.GetLength(0); i++) if (host == Searches[i, 0]) return true;
            return false;
        }

        private static bool Showing(string target)
        {
            string want = Regex.Replace(Plain(target), @"^the |( app| application| game| tab| window)$", "");
            if (want.Length == 0) return false;
            try
            {
                foreach (OpenWindow window in Windows())
                    if (Math.Max(Score(Plain(window.Program), want), Score(Plain(window.Title), want)) > 0) return true;
            }
            catch (Exception) { }
            return false;
        }

        private static string OpenApp(string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return "Which app, choom?";
            List<InstalledApp> found = Match(Apps(), wanted);
            if (found.Count == 0) return "No app called " + wanted.Trim() + " is installed on this PC.";
            // When several match equally well, the one with the fewest extra words is closest to what they said.
            InstalledApp chosen = found[0];
            foreach (InstalledApp app in found) if (AppName(app).Length < AppName(chosen).Length) chosen = app;
            Run(chosen.Id);
            return "Opened " + (AppName(chosen) == "spotify" ? "Spotify" : chosen.Name) + ".";
        }

        internal static List<InstalledApp> Match(List<InstalledApp> apps, string wanted)
        {
            string want = Regex.Replace(Plain(wanted), @"^the |( app| application| game)$", "");
            List<InstalledApp> top = new List<InstalledApp>();
            if (want.Length == 0) return top;
            int best = 0;
            foreach (InstalledApp app in apps)
            {
                string name = AppName(app);
                int score = Score(app, want);
                if (score == 0 || score < best) continue;
                if (score > best) { best = score; top.Clear(); }
                if (!top.Exists(other => AppName(other) == name)) top.Add(app);
            }
            return top;
        }

        private static int Score(string name, string want)
        {
            if (name == want || name.Replace(" ", "") == want.Replace(" ", "")) return 4;
            if (name.StartsWith(want + " ", StringComparison.Ordinal)) return 3;
            if ((" " + name + " ").Contains(" " + want + " ")) return 2;
            if (want.Length >= 4 && name.Contains(want)) return 1;
            return 0;
        }

        private static int Score(InstalledApp app, string want)
        {
            string name = AppName(app);
            return name.Length == 0 ? 0 : Math.Max(Score(name, want), Score(Plain(app.Name), want));
        }

        private static string AppName(InstalledApp app)
        {
            string name = Plain(app.Name);
            if (string.IsNullOrWhiteSpace(app.Id) || Junk.IsMatch(name) || app.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return "";
            if (Regex.IsMatch(app.Id, @"^SpotifyAB\.SpotifyMusic_[a-z0-9]+!Spotify$|^(?:[A-Za-z]:\\|\\\\).*[\\]Spotify\.exe$", RegexOptions.IgnoreCase)) return "spotify";
            return name;
        }

        private static string Plain(string text) { return Regex.Replace(Regex.Replace((text ?? "").Normalize(NormalizationForm.FormD).ToLowerInvariant(), @"\p{M}", ""), @"[^\p{L}\p{Nd}]+", " ").Trim(); }

        private static List<InstalledApp> Apps()
        {
            lock (gate)
            {
                if (cached != null && DateTime.UtcNow - cachedAt <= TimeSpan.FromMinutes(2)) return cached;
                List<InstalledApp> apps;
                Exception failed = null;
                try { apps = new List<InstalledApp>(Catalog()); }
                catch (Exception error) { apps = new List<InstalledApp>(); failed = error; }
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string path = folder.Length == 0 ? "" : Path.Combine(folder, @"Spotify\Spotify.exe");
                if (path.Length > 0 && Exists(path) && !apps.Exists(app => AppName(app) == "spotify")) apps.Add(new InstalledApp("Spotify", path));
                if (failed != null)
                {
                    if (apps.Count == 0) throw failed;
                    return apps;
                }
                cached = apps;
                cachedAt = DateTime.UtcNow;
                return cached;
            }
        }

        private static List<InstalledApp> ReadCatalog()
        {
            List<InstalledApp> apps = new List<InstalledApp>();
            Exception failed = null;
            Thread reader = new Thread(delegate()
            {
                try
                {
                    object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                    object items = Call(Call(shell, "NameSpace", "shell:AppsFolder"), "Items");
                    int count = (int)Get(items, "Count");
                    for (int i = 0; i < count; i++)
                    {
                        object item = Call(items, "Item", i);
                        string name = Get(item, "Name") as string;
                        string id = Get(item, "Path") as string;
                        if (!string.IsNullOrEmpty(id)) apps.Add(new InstalledApp(name ?? "", id));
                    }
                }
                catch (Exception error) { failed = error; }
            });
            reader.SetApartmentState(ApartmentState.STA);
            reader.IsBackground = true;
            reader.Start();
            if (!reader.Join(10000)) throw new TimeoutException("The app list took too long.");
            if (failed != null) throw failed;
            return WithSteam(apps, SteamApps(SteamFolder()));
        }

        // Steam games and apps show in All apps only when Steam made a Start menu shortcut, so her list adds the rest from Steam's own library.
        internal static List<InstalledApp> WithSteam(List<InstalledApp> apps, List<InstalledApp> steam)
        {
            foreach (InstalledApp game in steam)
                if (AppName(game).Length > 0 && !apps.Exists(app => AppName(app) == AppName(game))) apps.Add(game);
            return apps;
        }

        // Every library folder's fully installed games and apps, skipping Steam's own redistributables. They open through steam://rungameid.
        internal static List<InstalledApp> SteamApps(string steam)
        {
            List<InstalledApp> apps = new List<InstalledApp>();
            if (string.IsNullOrEmpty(steam)) return apps;
            try
            {
                List<string> libraries = new List<string> { steam };
                string folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(folders))
                    foreach (Match library in Regex.Matches(File.ReadAllText(folders), @"""path""\s+""([^""]+)"""))
                        libraries.Add(library.Groups[1].Value.Replace(@"\\", @"\"));
                HashSet<string> seen = new HashSet<string>();
                foreach (string library in libraries)
                {
                    string folder = Path.Combine(library, "steamapps");
                    if (!Directory.Exists(folder)) continue;
                    foreach (string manifest in Directory.GetFiles(folder, "appmanifest_*.acf"))
                    {
                        string text;
                        try { text = File.ReadAllText(manifest); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        Match id = Regex.Match(text, @"""appid""\s+""(\d+)""");
                        Match name = Regex.Match(text, @"""name""\s+""([^""]+)""");
                        Match state = Regex.Match(text, @"""StateFlags""\s+""(\d+)""");
                        long flags;
                        if (!id.Success || !name.Success || !seen.Add(id.Groups[1].Value)) continue;
                        if (state.Success && long.TryParse(state.Groups[1].Value, out flags) && (flags & 4) == 0) continue;
                        if (Regex.IsMatch(name.Groups[1].Value, @"^(Steamworks Common Redistributables|Steam Linux Runtime\b.*|Proton\b.*)$", RegexOptions.IgnoreCase)) continue;
                        apps.Add(new InstalledApp(name.Groups[1].Value, "steam://rungameid/" + id.Groups[1].Value));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return apps;
        }

        private static string SteamFolder()
        {
            try
            {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    string path = key == null ? null : key.GetValue("SteamPath") as string;
                    return string.IsNullOrEmpty(path) ? null : path.Replace('/', '\\');
                }
            }
            catch (Exception) { return null; }
        }

        private static object Call(object target, string method, params object[] args) { return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args); }

        private static object Get(object target, string property) { return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null); }

        private static void Start(string id)
        {
            using (Process.Start(Launch(id))) { }
        }

        internal static ProcessStartInfo Launch(string id)
        {
            ProcessStartInfo info = Regex.IsMatch(id, @"^[A-Za-z]:\\|^\\\\|^[a-z][a-z0-9+.-]*://|^spotify:", RegexOptions.IgnoreCase) ? new ProcessStartInfo(id) : new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + id);
            info.UseShellExecute = true;
            return info;
        }

        private static string OpenSite(string site, string search)
        {
            string address = Address(site, search);
            if (address == null) return "That isn't a website.";
            Run(address);
            if (address.TrimEnd('/') == "https://" + Home) return "Opened your own website, " + Home + ".";
            string host = new Uri(address).Host;
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host.Substring(4);
            return string.IsNullOrWhiteSpace(search) ? "Opened " + host + "." : "Searched " + host + " for " + search.Trim() + ".";
        }

        internal static string Address(string site, string search)
        {
            string host = Regex.Replace((site ?? "").Trim().ToLowerInvariant(), @"^(https?://)?(www\.)?", "");
            bool searching = !string.IsNullOrWhiteSpace(search);
            if (searching)
            {
                for (int i = 0; i < Searches.GetLength(0); i++)
                {
                    string key = Searches[i, 0];
                    if (host == key || host.StartsWith(key + ".", StringComparison.Ordinal) || host.Contains("." + key + ".")) return Searches[i, 1] + Uri.EscapeDataString(search.Trim());
                }
                return Searches[1, 1] + Uri.EscapeDataString((search.Trim() + " " + host).Trim());
            }
            if (host.Length == 0) return null;
            if (host.IndexOf('.') < 0) host += ".com";
            Uri uri;
            if (!Uri.TryCreate("https://" + host, UriKind.Absolute, out uri) || uri.Host.IndexOf('.') < 0 || uri.UserInfo.Length > 0) return null;
            return uri.AbsoluteUri;
        }

#if CROSS
        // Mac and Linux have no Windows audio API (Speakers lives in Ears.cs); Limbs sets these from osascript, pactl or amixer.
        internal static Func<bool?> MuteState = delegate { return (bool?)null; };
        internal static Func<bool, bool> SetMute = delegate { return false; };
        internal static Func<double?> VolumeNow = delegate { return (double?)null; };
        internal static Func<double, bool> SetVolume = delegate { return false; };
        private const string VolumeRefused = "Couldn't set the volume: the system's sound control didn't answer.";
#else
        internal static Func<bool?> MuteState = Speakers.Muted;
        internal static Func<bool, bool> SetMute = Speakers.Mute;
        internal static Func<double?> VolumeNow = Speakers.Level;
        internal static Func<double, bool> SetVolume = Speakers.SetLevel;
        private const string VolumeRefused = "Couldn't set the volume: Windows didn't let me.";
#endif

        private static string Media(ToolArguments args)
        {
            string action = args.Action;
            if (action == "play_pause" || action == "pause" || action == "resume") return Toggle(action);
            if (action == "next") { Press(0xB0); return "Skipped to the next track."; }
            if (action == "previous") { Press(0xB1); return "Went back a track."; }
            int level;
            bool leveled = int.TryParse(args.Level, out level) && level >= 0 && level <= 100;
            // Each volume key moves Windows' volume 2%, and shows its volume bar.
            if (action == "volume_up" || action == "volume_down")
            {
                bool up = action == "volume_up";
                int presses = leveled && level > 0 ? Math.Max(1, (level + 1) / 2) : 5;
                for (int i = 0; i < presses; i++) Press(up ? (byte)0xAF : (byte)0xAE);
                return up ? "Turned the volume up." : "Turned the volume down.";
            }
            if (action == "volume_set")
            {
                if (!leveled) return "Couldn't set the volume: it goes from 0 to 100.";
                if (!SetVolume(level / 100.0)) return VolumeRefused;
                if (level > 0 && MuteState() == true) SetMute(false);
                return "Set the volume to " + level + "%.";
            }
            if (action == "mute" || action == "unmute")
            {
                bool muting = action == "mute";
                bool? muted = MuteState();
                if (muted == null) { Press(0xAD); return "Toggled mute."; }
                double? now = VolumeNow();
                // "Turn the sound on" with the volume at zero needs volume as well as unmuting.
                if (!muting && now.HasValue && now.Value < 0.01 && SetVolume(0.2))
                {
                    if (muted.Value) SetMute(false);
                    return "Turned the sound back on at 20%.";
                }
                if (muted.Value == muting) return muting ? "The PC is already muted." : "The sound is already on.";
                if (!SetMute(muting)) { Press(0xAD); return "Toggled mute."; }
                return muting ? "Muted the PC." : "Unmuted the PC.";
            }
            if (action == "now_playing") return Playing();
            return "There's no media action called " + action + ".";
        }

        private static string Playing()
        {
            string title = NowPlaying();
            if (TrackTitle(title))
            {
                int dash = title.IndexOf(" - ", StringComparison.Ordinal);
                return "Spotify is playing " + (dash > 0 ? title.Substring(dash + 3) + " by " + title.Substring(0, dash) : title) + ".";
            }
            foreach (OpenWindow window in Windows())
            {
                Match video = Regex.Match(window.Title, @"^(?:\(\d+\)\s*)?(.+?) - YouTube( Music)?( - |$)");
                if (video.Success) return "Their YouTube tab is on " + video.Groups[1].Value + ".";
            }
            if (Regex.IsMatch(title, @"^Spotify( \w+)?$")) return "Spotify is open but paused.";
            return title.Length > 0 ? "Spotify is open, but I can't tell what's playing." : "Nothing's playing that I can see.";
        }

        private static string Toggle(string action)
        {
            string before = NowPlaying();
            Press(0xB3);
            for (int i = 0; i < 6 && before.Length > 0; i++)
            {
                Thread.Sleep(250);
                string after = NowPlaying();
                if (after == before) continue;
                if (TrackTitle(after)) return "Spotify is playing again.";
                if (TrackTitle(before)) return "Paused Spotify.";
            }
            return action == "pause" ? "Pressed pause." : action == "resume" ? "Pressed play." : "Pressed play/pause.";
        }

        private static readonly string[,] Shortcuts =
        {
            { "discord_mute", "discord", "11 10 4D", "Toggled their mic mute in Discord (Ctrl + Shift + M)." },
            { "discord_deafen", "discord", "11 10 44", "Toggled deafen in Discord (Ctrl + Shift + D)." },
            { "screenshot", "", "5B 10 53", "Opened the screenshot snip (Win + Shift + S). Drag over what to grab." },
            { "clip_that", "", "5B 12 47", "Saved the last 30 seconds with Game Bar (Win + Alt + G)." },
            { "record", "", "5B 12 52", "Started or stopped a Game Bar recording (Win + Alt + R)." },
            { "game_bar", "", "5B 47", "Opened the Xbox Game Bar (Win + G)." },
            { "show_desktop", "", "5B 44", "Showed the desktop (Win + D)." },
            { "task_manager", "", "11 10 1B", "Opened Task Manager." },
            { "clipboard", "", "5B 56", "Opened clipboard history (Win + V)." },
            { "emoji", "", "5B BE", "Opened the emoji picker (Win + .)." },
            { "voice_typing", "", "5B 48", "Opened voice typing (Win + H). Talk and it types where the cursor is." },
            { "live_captions", "", "5B 11 4C", "Turned Live Captions on or off (Win + Ctrl + L)." },
            { "narrator", "", "5B 11 0D", "Turned Narrator on or off (Win + Ctrl + Enter)." },
            { "magnifier", "", "5B BB", "Zoomed in with Magnifier (Win + Plus). Win + Esc closes it." },
            { "magnifier_off", "", "5B 1B", "Closed Magnifier (Win + Esc)." },
            { "color_filters", "", "5B 11 43", "Turned colour filters on or off (Win + Ctrl + C). If nothing changed, switch on their keyboard shortcut in Settings > Accessibility > Color filters." },
            { "quick_settings", "", "5B 41", "Opened Quick Settings (Win + A), with Wi-Fi, Bluetooth, airplane mode, hotspot, Energy Saver and brightness." }
        };

        private static readonly string[] ShortcutWords =
        {
            @"^(un)?mute (my |me on |me in |)(mic|microphone|myself|me)( on discord| in discord| in (the )?(call|vc|voice( chat| channel)?))?$|^turn (off |on )?my (mic|microphone)( off| on| back on)?( on discord| in discord)?$|^(un)?mute (me|myself|my mic|my microphone) (on|in) discord$|^discord mute$", "discord_mute",
            @"^(un)?deafen( me| myself)?( on discord| in discord)?$|^discord deafen$", "discord_deafen",
            @"^(take|grab|get) (a |me a )?(screenshot|screen ?shot|screen grab|snip)( of (this|my screen|the screen))?$|^screenshot( this| that| my screen)?$", "screenshot",
            @"^clip (that|it|this)$|^(save|grab) (a |the )?clip$|^clip the last 30 seconds$", "clip_that",
            @"^(start|stop) (the )?recording( my screen| the screen)?$|^record (my screen|the screen|this)$", "record",
            @"^(open|show|bring up|pull up) (the |my )?(xbox )?game ?bar$", "game_bar",
            @"^show (me )?(the |my )?desktop$|^minimi[sz]e (everything|all( windows)?)$", "show_desktop",
            @"^(open|show|bring up|pull up) (the )?task manager$", "task_manager",
            @"^(open|show|bring up|pull up) (the |my )?clipboard( history)?$", "clipboard",
            @"^(open|show|bring up|pull up) (the )?(emoji|emojis)( picker| keyboard| panel| menu)?$", "emoji",
            @"^((turn|switch) on |start |open )?(windows )?voice typing( on)?$", "voice_typing",
            @"^((turn|switch) (on|off) |start |stop |open |close |show |hide |toggle )?(the )?live ?captions?( on| off)?$", "live_captions",
            @"^((turn|switch) (on|off) |start |stop |open |close |toggle )?(the )?narrator( on| off)?$", "narrator",
            @"^((turn|switch) off |stop |close |exit |quit )(the )?magnifier$|^(the )?magnifier off$", "magnifier_off",
            @"^((turn|switch) on |start |open )?(the )?magnifier( on)?$", "magnifier",
            @"^((turn|switch) (on|off) |toggle )?(the )?colou?r ?filters?( on| off)?$", "color_filters",
            @"^(open|show|bring up|pull up) (the |my )?(quick settings|action cent(er|re))$", "quick_settings"
        };

        internal static string ShortcutFor(string text)
        {
            string lower = (text ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < ShortcutWords.Length; i += 2) if (Regex.IsMatch(lower, ShortcutWords[i])) return ShortcutWords[i + 1];
            return null;
        }

        private static string Shortcut(string name)
        {
            int row = -1;
            for (int i = 0; i < Shortcuts.GetLength(0); i++) if (Shortcuts[i, 0] == name) row = i;
            if (row < 0) return "There's no shortcut called " + name + ".";
            string instead = Instead(name);
            if (instead != null) return instead;
            string[] codes = Shortcuts[row, 2].Split(' ');
            byte[] keys = new byte[codes.Length];
            for (int i = 0; i < codes.Length; i++) keys[i] = Convert.ToByte(codes[i], 16);
            if (Shortcuts[row, 1] != "discord" || !Presses) { Chord(keys); return Shortcuts[row, 3]; }
            OpenWindow discord = FindDiscord();
            if (discord == null)
            {
                string opened = OpenApp("Discord");
                if (!opened.StartsWith("Opened", StringComparison.Ordinal)) return "Discord isn't open, so I couldn't press its shortcut. " + opened;
                for (int i = 0; i < 20 && discord == null; i++) { Thread.Sleep(250); discord = FindDiscord(); }
                if (discord == null) return "Opened Discord, but its window didn't show up in time. Ask again in a sec.";
            }
            IntPtr before = Native.GetForegroundWindow();
            if (!Steer(discord, "switch_to")) return "Couldn't bring Discord to the front to press its shortcut.";
            Chord(keys);
            Thread.Sleep(150);
            if (before != IntPtr.Zero && before != discord.Handle) Steer(new OpenWindow(before, "", ""), "switch_to");
            return Shortcuts[row, 3];
        }

        private static OpenWindow FindDiscord()
        {
            foreach (OpenWindow window in Windows()) if (string.Equals(window.Program, "Discord", StringComparison.OrdinalIgnoreCase) || string.Equals(window.Program, "DiscordPTB", StringComparison.OrdinalIgnoreCase) || string.Equals(window.Program, "DiscordCanary", StringComparison.OrdinalIgnoreCase)) return window;
            return null;
        }

        private const string Machine = @"(?:(?:my|the|this|ur|your|our)\s+)?(?:pc|computer|laptop|system|machine|rig|windows|device)";
        private static readonly string[] PowerWords =
        {
            @"^(?:shut ?down|turn off|power (?:off|down)|switch off)\s+" + Machine + @"$|^(?:shut|turn|power|switch)\s+" + Machine + @"\s+(?:down|off)$", "shut_down",
            @"^(?:restart|reboot)\s+" + Machine + "$", "restart",
            @"^(?:put|send)\s+" + Machine + @"\s+to sleep$|^(?:sleep|hibernate)\s+" + Machine + @"$|^(?:make|let)\s+" + Machine + @"\s+(?:go to )?sleep$", "sleep",
            @"^lock\s+(?:" + Machine + @"|(?:(?:my|the|this)\s+)?(?:screen|desktop))$", "lock",
            @"^(?:sign|log) me (?:out|off)$|^(?:sign|log) (?:out|off)(?: of)?\s+(?:" + Machine + @"|(?:my )?windows account)$", "sign_out",
            @"^(?:cancel|stop|abort|undo)\s+(?:the |that |my )?(?:shut ?down|restart|reboot|sleep|sign ?out|log ?out)$|^(?:don'?t|do not)\s+(?:shut ?down|turn off|restart|reboot)(?:\s+(?:it|" + Machine + "))?$", "cancel"
        };
        private static DateTime powered = DateTime.MinValue;
        private static System.Threading.Timer powerLater;
        [DllImport("user32.dll")] private static extern bool LockWorkStation();
        [DllImport("user32.dll")] private static extern bool ExitWindowsEx(int flags, int reason);
        [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool force, bool wakeEventsOff);

        private static string PowerFor(string text, out string minutes)
        {
            minutes = null;
            // Right after she starts shutting down, "no", "wait" or "stop" means cancel it.
            if ((DateTime.Now - powered).TotalSeconds < 20 && Regex.IsMatch(text, @"^(no+|nah|nope|wait|stop|cancel|abort|undo|don'?t|do not|never ?mind|hold on|hold up)\b", RegexOptions.IgnoreCase)) return "cancel";
            Match later = Regex.Match(text, @"\s+in\s+(\d{1,4}|an?|one)\s*(min(ute)?s?|h(ou)?rs?)$", RegexOptions.IgnoreCase);
            string asked = later.Success ? text.Substring(0, later.Index) : text;
            for (int i = 0; i < PowerWords.Length; i += 2)
            {
                if (!Regex.IsMatch(asked, PowerWords[i], RegexOptions.IgnoreCase)) continue;
                if (later.Success)
                {
                    int count = Regex.IsMatch(later.Groups[1].Value, @"^(an?|one)$", RegexOptions.IgnoreCase) ? 1 : int.Parse(later.Groups[1].Value);
                    minutes = (later.Groups[2].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase) ? count * 60 : count).ToString();
                }
                return PowerWords[i + 1];
            }
            return null;
        }

        private static string Turn(string action, string minutes)
        {
            int later;
            if (!int.TryParse((minutes ?? "").Trim(), out later) || later < 0) later = 0;
            later = Math.Min(later, 1440);
            if (action == "cancel")
            {
                powered = DateTime.MinValue;
                return Power("cancel", 0) ? "Cancelled it. Your PC stays on." : "Nothing was shutting down, so there's nothing to cancel.";
            }
            int seconds = later > 0 ? later * 60 : action == "shut_down" || action == "restart" ? 10 : action == "sign_out" ? 5 : action == "sleep" ? 4 : 0;
            if (!Power(action, seconds)) return action == "shut_down" ? "Couldn't shut your PC down." : action == "restart" ? "Couldn't restart your PC." : action == "sleep" ? "Couldn't put your PC to sleep." : action == "lock" ? "Couldn't lock your PC." : "Couldn't sign you out.";
            if (later == 0 && action != "lock") powered = DateTime.Now;
            string when = later == 0 ? null : later % 60 == 0 ? (later / 60) + (later == 60 ? " hour" : " hours") : later + (later == 1 ? " minute" : " minutes");
            string stop = when == null ? " Say cancel to stop it." : " Say cancel the shutdown to stop it.";
            if (action == "shut_down") return "Shutting your PC down in " + (when ?? "10 seconds") + "." + stop;
            if (action == "restart") return "Restarting your PC in " + (when ?? "10 seconds") + "." + stop;
            if (action == "sign_out") return "Signing you out in " + (when ?? "5 seconds") + "." + stop;
            if (action == "sleep") return when == null ? "Putting your PC to sleep." : "Putting your PC to sleep in " + when + ".";
            return when == null ? "Locked your PC." : "Locking your PC in " + when + ".";
        }

        private static bool Switch(string action, int seconds)
        {
            if (action == "cancel")
            {
                bool stopped;
                lock (gate) { stopped = powerLater != null; if (stopped) powerLater.Dispose(); powerLater = null; }
                return Shutdown("/a") || stopped;
            }
            if (action == "shut_down" || action == "restart") return Shutdown((action == "restart" ? "/r" : "/s") + " /t " + seconds);
            if (action == "lock" && seconds == 0) return LockWorkStation();
            // Sleep, sign out and timed locks wait a moment so her line plays first.
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer(delegate
            {
                lock (gate) { if (powerLater != timer) return; powerLater = null; }
                if (action == "lock") LockWorkStation();
                else if (action == "sign_out") ExitWindowsEx(0, 0);
                else if (!SetSuspendState(false, false, false)) PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));
            }, null, Timeout.Infinite, Timeout.Infinite);
            lock (gate) { if (powerLater != null) powerLater.Dispose(); powerLater = timer; }
            timer.Change(seconds * 1000, Timeout.Infinite);
            return true;
        }

        private static bool Shutdown(string flags)
        {
            try
            {
                using (Process run = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), flags) { UseShellExecute = false, CreateNoWindow = true }))
                    return run != null && run.WaitForExit(5000) && run.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        private static void PressChord(byte[] keys)
        {
            foreach (byte key in keys) Native.keybd_event(key, 0, key == 0x5B ? 0x1 : 0, UIntPtr.Zero);
            for (int i = keys.Length - 1; i >= 0; i--) Native.keybd_event(keys[i], 0, (keys[i] == 0x5B ? 0x1 : 0) | 0x2, UIntPtr.Zero);
        }

        private static void Tap(byte key)
        {
            Native.keybd_event(key, 0, 0x1, UIntPtr.Zero);
            Native.keybd_event(key, 0, 0x1 | 0x2, UIntPtr.Zero);
        }

        private static readonly string[,] KnownSongs =
        {
            { @"^in love with myself(\s+(by|from)\s+(petite meller|kalman chodakiewitz|eli shiffman|(cyberpunk:?\s+)?edgerunners)\b.*)?$", "2iyaHQlYrR8Eyr4wOmOT5D", "In Love With Myself", "In Love With Myself Petite Meller" },
            { @"^(i really want to stay at your house|irwtsayh)(\s+(by|from)\s+(rosa walton|hallie coggins|(cyberpunk:?\s+)?edgerunners)\b.*)?$", "42W7fJXB7razFefTxMSyYp", "I Really Want to Stay at Your House", "I Really Want to Stay at Your House Rosa Walton" },
            { @"^chippin(g|'|\u2019)?\s+in(\s+(by|from)\s+(samurai|refused|johnny silverhand|cyberpunk(\s+2077)?)\b.*)?$", "0UHxkkgI5GfV0XBRA1rqfe", "Chippin' In", "Chippin' In SAMURAI Refused" },
            { @"^never fade away(\s+(by|from)\s+(samurai|refused|p\.?\s*t\.?\s+adamczyk|olga jankowska|cyberpunk(\s+2077)?)\b.*)?$", "6pV1lc8lIc8ARNB6KsZukt", "Never Fade Away", "Never Fade Away SAMURAI Cyberpunk 2077" },
            { @"^black dog\s+(by|from)\s+(samurai|cyberpunk(\s+2077)?)\b.*$", "5Ln8eNkYC9to99zsPEZbS2", "Black Dog", "Black Dog SAMURAI Cyberpunk 2077" },
            { @"^let you down\s+(by|from)\s+(dawid podsiad[l\u0142]o|(cyberpunk:?\s+)?edgerunners)\b.*$", "6Efhl5xyn7MsuGhyboRThX", "Let You Down", "Let You Down Dawid Podsiadlo Edgerunners" },
            { @"^resist and disorder(\s+(by|from)\s+(rezodrone|cyberpunk(\s+2077)?)\b.*)?$", "4onvDep8OyHKT85RBO3b4V", "Resist and Disorder", "Resist and Disorder Rezodrone" },
            { @"^the rebel path(\s+(by|from)\s+(p\.?\s*t\.?\s+adamczyk|cyberpunk(\s+2077)?)\b.*)?$", "3m8UfZwPMMbiWfGR8Lg2cR", "The Rebel Path", "The Rebel Path Cyberpunk 2077" }
        };

        private static readonly string[] Ost = { "Chippin' In", "Never Fade Away", "Black Dog by SAMURAI", "I Really Want to Stay at Your House", "Let You Down by Dawid Podsiad\u0142o", "Resist and Disorder", "The Rebel Path", "In Love With Myself" };
        internal static Func<int, int> Roll = new Random().Next;

        internal static string Pick() { return Ost[Roll(Ost.Length)]; }

        // A Night City song in a player's or tab's title, for her cheer. "Cyberpunk 2077 review - YouTube" isn't a song: the game's
        // own name only counts next to a word for music.
        private static readonly Regex NightCityMusic = new Regex(@"rezodrone|rosa walton|hallie coggins|adamczyk|przyby[lł]owicz|i really want to stay at your house|chippin.? in|never fade away|resist and disorder|the rebel path|this fffire|podsiad[lł]o|^\W*samurai\s*[-–—]", RegexOptions.IgnoreCase);
        private static readonly Regex NightCityName = new Regex(@"cyberpunk|edgerunners|night city", RegexOptions.IgnoreCase);
        private static readonly Regex MusicWord = new Regex(@"\b(ost|soundtrack|score|music|songs?|radio|playlist|mix|theme|lyrics|album)\b", RegexOptions.IgnoreCase);

        internal static bool NightCity(string title) { return !string.IsNullOrEmpty(title) && (NightCityMusic.IsMatch(title) || (NightCityName.IsMatch(title) && MusicWord.IsMatch(title))); }

        private static readonly Regex Moon = new Regex(@"\bwish\s+(?:we|i|you\s+and\s+i|me\s+and\s+you)\s+could\s+(?:go|fly|get|run\s+away)\s+to\s+the\s+moon\b", RegexOptions.IgnoreCase);
        internal const string MoonSong = "I Really Want to Stay at Your House";

        internal const string Home = "lachydotmcg.github.io/capri-desktop";

        private static string PlayMusic(string song, string app, string kind)
        {
            if (string.IsNullOrWhiteSpace(song)) return "What should I play?";
            song = song.Trim();
            if (Regex.IsMatch(song, @"^((what('|\u2019)?s|what is)\s+)?((currently|now)\s+)?playing( now)?$|^(this|that|the current) (song|track)$", RegexOptions.IgnoreCase)) return Playing();
            if (Regex.IsMatch(song, @"^(play\s+)?(me\s+)?((some|any|a|random|good)\s+)?(music|songs?|tunes|tracks?|something)(\s+for me)?$", RegexOptions.IgnoreCase)) { song = Pick(); kind = "song"; }
            Match tail = Regex.Match(song, @"^(?:(?:the|my|your)\s+)?(.+?)\s+(playlist|album)$", RegexOptions.IgnoreCase);
            if (tail.Success && kind != "artist") { kind = tail.Groups[2].Value.ToLowerInvariant(); song = tail.Groups[1].Value; }
            bool liked = Regex.IsMatch(song, @"^((my|your|the)\s+)?((liked|saved)\s+songs|likes)$", RegexOptions.IgnoreCase);
            if (liked) kind = "playlist";
            if (kind != "playlist" && kind != "album" && kind != "artist") kind = "song";
            string known = null;
            string look = song;
            if (kind == "song")
                for (int i = 0; i < KnownSongs.GetLength(0); i++)
                    if (Regex.IsMatch(song, KnownSongs[i, 0], RegexOptions.IgnoreCase)) { known = KnownSongs[i, 1]; song = KnownSongs[i, 2]; look = KnownSongs[i, 3]; }
            string named = kind == "song" ? song : "the " + song + " " + kind;
            if (app != "youtube" && Match(Apps(), "spotify").Count > 0)
            {
                if (liked) named = "their Liked Songs";
                string spotted = liked ? null : known ?? SpotifyLink(Fetch("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString("site:open.spotify.com/" + (kind == "song" ? "track" : kind) + " " + song)), kind, song);
                if (kind == "song" && spotted != null)
                {
                    string before = NowPlaying();
                    Run("spotify:track:" + spotted);
                    return Started(before) || PressPlay(song) ? "Playing " + song + " on Spotify." : "Opened " + song + " in Spotify, but couldn't press play. Hit the green Play button.";
                }
                if (liked || spotted != null)
                {
                    Run(liked ? "spotify:collection:tracks" : "spotify:" + kind + ":" + spotted);
                    return PressPlay(liked ? "Liked Songs" : song) ? "Playing " + named + " on Spotify." : "Opened " + named + " in Spotify, but couldn't press play. Hit the green Play button.";
                }
                Run("spotify:search:" + Uri.EscapeDataString(song));
                return PressPlay(song) ? "Playing " + named + " on Spotify." : "Couldn't find " + named + " to play, so Spotify is open on a search for it.";
            }
            string where = Searches[0, 1] + Uri.EscapeDataString(look);
            System.Text.RegularExpressions.Match list = Regex.Match(kind == "song" ? "" : Fetch(where + "&sp=EgIQAw%253D%253D") ?? "", @"""watchEndpoint"":\{""videoId"":""([A-Za-z0-9_-]{11})"",""playlistId"":""([A-Za-z0-9_-]+)""");
            string video = list.Success ? null : Find(Fetch(where), "\"videoId\":\"([A-Za-z0-9_-]{11})\"");
            if (!list.Success && video == null) { Run(where); return "Couldn't find " + named + " to play, so YouTube is open on a search for it."; }
            Run(list.Success ? "https://www.youtube.com/watch?v=" + list.Groups[1].Value + "&list=" + list.Groups[2].Value : "https://www.youtube.com/watch?v=" + video);
            return (app == "spotify" ? "Spotify isn't installed, so playing " : "Playing ") + named + " on YouTube.";
        }

        private static bool ClickPlay(string name)
        {
#if CROSS
            return false;
#else
            for (int i = 0; i < 30; i++)
            {
                Thread.Sleep(500);
                foreach (OpenWindow window in Windows())
                {
                    if (!string.Equals(window.Program, "Spotify", StringComparison.OrdinalIgnoreCase) || window.Handle == IntPtr.Zero) continue;
                    AutomationElement close = null;
                    int best = 0;
                    foreach (AutomationElement button in AutomationElement.FromHandle(window.Handle).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                    {
                        int score = PlayScore(button.Current.Name, name);
                        if (score <= best) continue;
                        close = button;
                        best = score;
                        if (best == 3) break;
                    }
                    object pattern;
                    if (close == null || (i < 6 && best == 1) || !close.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) continue;
                    ((InvokePattern)pattern).Invoke();
                    return true;
                }
            }
            return false;
#endif
        }

        // A site: lookup that matches nothing still lists popular pages from the site, so only a result whose title has
        // the words they asked for counts (one may be missing from three or more). Otherwise she searches Spotify instead.
        internal static string SpotifyLink(string page, string kind, string wanted)
        {
            string path = kind == "song" ? "track" : kind;
            string[] want = Words(wanted);
            if (want.Length == 0 || want[0].Length == 0) return null;
            foreach (Match link in Regex.Matches(page ?? "", @"<a\b([^>]*)>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                Match id = Regex.Match(link.Groups[1].Value, @"open\.spotify\.com(?:/|%2F)" + path + @"(?:/|%2F)([A-Za-z0-9]{22})");
                if (!id.Success) continue;
                // A link that only shows its address (open.spotify.com/playlist/...) says nothing about what's there.
                string[] title = Plain(Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(link.Groups[2].Value, "<[^>]+>", "")), @"\S*(spotify\.com|https?://|www\.)\S*", " ", RegexOptions.IgnoreCase)).Split(' ');
                int missing = 0;
                foreach (string word in want) if (Array.IndexOf(title, word) < 0) missing++;
                if (missing == 0 || (missing == 1 && want.Length >= 3)) return id.Groups[1].Value;
            }
            return null;
        }

        internal static int PlayScore(string label, string name)
        {
            Match play = Regex.Match(label ?? "", @"^(?:Play|Lire)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!play.Success) return 0;
            string[] shown = Words(play.Groups[1].Value);
            string[] want = Words(name);
            bool shorter = Within(shown, want);
            bool longer = Within(want, shown);
            return shorter && longer ? 3 : shorter ? 2 : longer ? 1 : 0;
        }

        private static string[] Words(string text)
        {
            string plain = Plain(Regex.Replace(text ?? "", @"\s+(by|par)\s+.*$", "", RegexOptions.IgnoreCase));
            return (plain == "titres likes" ? "liked songs" : plain).Split(' ');
        }

        private static bool Within(string[] part, string[] whole)
        {
            if (part.Length == 0 || part[0].Length == 0) return false;
            foreach (string word in part) if (Array.IndexOf(whole, word) < 0) return false;
            return true;
        }

        private static string SpotifyTitle()
        {
            string title = "";
            foreach (OpenWindow window in Windows())
                if (string.Equals(window.Program, "Spotify", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(window.Title))
                {
                    if (TrackTitle(window.Title)) return window.Title;
                    title = window.Title;
                }
            return title;
        }

        private static bool TrackTitle(string title) { return Regex.IsMatch(title ?? "", @"^\S.* - \S") && !Regex.IsMatch(title, @"^Spotify(?:$|\s)| - Spotify(?:$|\s)", RegexOptions.IgnoreCase); }

        internal static bool Started(string before, string title) { return title != before && TrackTitle(title); }

        private static bool Started(string before)
        {
            for (int i = 0; i < 24; i++)
            {
                string title = NowPlaying();
                if (Started(before, title)) return true;
                Thread.Sleep(500);
            }
            return false;
        }

        private static string Find(string page, string pattern)
        {
            Group found = Regex.Match(page ?? "", pattern).Groups[1];
            return found.Success ? found.Value : null;
        }

        private static string Download(string address)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
            request.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
            request.Headers["Accept-Language"] = "en";
            request.Timeout = 8000;
            request.ReadWriteTimeout = 8000;
            try
            {
                using (WebResponse response = request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            }
            catch (WebException) { return ""; }
            catch (IOException) { return ""; }
        }

        private static string Window(string wanted, string action)
        {
            if (action != "switch_to" && action != "minimize" && action != "close") return "There's no window action called " + action + ".";
            string want = Regex.Replace(Plain(wanted), @"^the |( app| application| game| tab| window)$", "");
            if (want.Length == 0) return "Which app, choom?";
            wanted = wanted.Trim();
            List<OpenWindow> top = new List<OpenWindow>();
            int best = 0;
            foreach (OpenWindow window in Windows())
            {
                int score = Math.Max(Score(Plain(window.Program), want), Score(Plain(window.Title), want));
                if (score == 0 || score < best) continue;
                if (score > best) { best = score; top.Clear(); }
                top.Add(window);
            }
            if (top.Count == 0) return Unlisted() ?? wanted + " isn't open.";
            // When several apps match, the one whose own name matches goes first, then the one nearest the front.
            OpenWindow first = top.Find(window => Score(Plain(window.Program), want) == best) ?? top[0];
            top = top.FindAll(window => window.Program == first.Program);
            if (action == "switch_to") return Steer(top[0], action) ? "Switched to " + wanted + "." : "Couldn't bring " + wanted + " to the front.";
            if (action == "close" && Array.IndexOf(Browsers, top[0].Program.ToLowerInvariant()) >= 0 && Score(Plain(top[0].Program), want) == 0)
                return Steer(top[0], "close_tab") ? "Closed the " + wanted + " tab." : "Couldn't reach the " + wanted + " tab to close it.";
            bool all = true;
            foreach (OpenWindow window in top) all &= Steer(window, action);
            if (action == "minimize") return "Minimized " + wanted + ".";
            return all ? "Closed " + wanted + "." : "Asked " + wanted + " to close, but it's still open. It might be asking you to save.";
        }

        private static List<OpenWindow> ReadWindows()
        {
            List<OpenWindow> found = new List<OpenWindow>();
            Dictionary<int, string> programs = new Dictionary<int, string>();
            int own = Process.GetCurrentProcess().Id;
            Visit visit = delegate(IntPtr window, IntPtr data)
            {
                int cloaked;
                if (!IsWindowVisible(window) || Native.GetWindow(window, 4) != IntPtr.Zero || (Native.GetWindowLong(window, -20) & 0x80) != 0) return true;
                if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                int length = GetWindowTextLength(window);
                if (length == 0) return true;
                StringBuilder kind = new StringBuilder(64);
                GetClassName(window, kind, kind.Capacity);
                if (Array.IndexOf(Shell, kind.ToString()) >= 0) return true;
                int id;
                GetWindowThreadProcessId(window, out id);
                if (id == own) return true;
                string program;
                if (!programs.TryGetValue(id, out program))
                {
                    try { using (Process process = Process.GetProcessById(id)) program = process.ProcessName; }
                    catch (ArgumentException) { program = ""; }
                    catch (InvalidOperationException) { program = ""; }
                    programs[id] = program;
                }
                StringBuilder title = new StringBuilder(length + 1);
                GetWindowText(window, title, title.Capacity);
                found.Add(new OpenWindow(window, title.ToString(), program));
                return true;
            };
            EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return found;
        }

        private static bool Apply(OpenWindow window, string action)
        {
            IntPtr handle = window.Handle;
            if (action == "minimize") { ShowWindow(handle, 6); return true; }
            if (action == "close")
            {
                PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                for (int i = 0; i < 20 && IsWindow(handle); i++) Thread.Sleep(100);
                return !IsWindow(handle);
            }
            if (!Front(handle)) return false;
            if (action != "close_tab") return true;
            Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x57, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x57, 0, 0x2, UIntPtr.Zero);
            Native.keybd_event(0x11, 0, 0x2, UIntPtr.Zero);
            return true;
        }

        internal static bool Front(IntPtr handle)
        {
            if (IsIconic(handle)) ShowWindow(handle, 9);
            int ignored;
            int mine = GetCurrentThreadId();
            int theirs = GetWindowThreadProcessId(Native.GetForegroundWindow(), out ignored);
            bool attached = theirs != 0 && theirs != mine && AttachThreadInput(mine, theirs, true);
            BringWindowToTop(handle);
            SetForegroundWindow(handle);
            if (attached) AttachThreadInput(mine, theirs, false);
            for (int i = 0; i < 10 && Native.GetForegroundWindow() != handle; i++) Thread.Sleep(50);
            return Native.GetForegroundWindow() == handle;
        }
    }
}
