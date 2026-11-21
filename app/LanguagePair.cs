using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class LanguagePair
    {
        internal const string Same = "Same";
        internal const string Auto = "Auto";
        internal const string SameName = "Same as I speak";
        internal const string AutoName = "Auto (match what I say)";

        internal static string Known(string language)
        {
            if (string.IsNullOrEmpty(language)) return null;
            for (int i = 0; i < Brain.Languages.GetLength(0); i++) if (Brain.Languages[i, 1] == language) return language;
            return null;
        }

        internal static string Answers(string saved) { return saved == Auto ? Auto : Known(saved); }

        internal static string Reply(string answers, string speaks)
        {
            string reply = answers == Auto ? Known(speaks) : Known(answers) ?? Known(speaks);
            return reply == null || Brain.LanguageCode(reply) == "en" ? null : reply;
        }

        internal static string Shown(string answers) { return answers == Auto ? AutoName : Known(answers) ?? SameName; }

        internal static string Line(string heard)
        {
            return "\nAnswer in the language of their latest message, which is " + (Known(heard) ?? "English") + ". If they switch languages, switch with them. Every rule you have holds in every language. Keep your Night City slang words as they are.";
        }

        internal static string Detect(string text, string lean)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = Regex.Replace(text.Normalize(NormalizationForm.FormC), @"https?://\S+|\[[^\]]*\]|<[^>]*>|@\w+|:[a-z_]+:", " ", RegexOptions.IgnoreCase);
            int latin = 0;
            int[] counts = new int[ScriptNames.Length];
            foreach (char letter in text)
            {
                if (!char.IsLetter(letter)) continue;
                if (letter < 0x0250 || letter >= 0x1E00 && letter <= 0x1EFF) { latin++; continue; }
                for (int i = 0; i < ScriptRanges.Length; i++) if (In(letter, ScriptRanges[i])) { counts[i]++; break; }
            }
            int kana = Array.IndexOf(ScriptNames, "Japanese"), hangul = Array.IndexOf(ScriptNames, "Korean"), han = Array.IndexOf(ScriptNames, "Chinese");
            if (counts[kana] > 0) { counts[kana] += counts[han]; counts[han] = 0; }
            else if (counts[hangul] > 0) { counts[hangul] += counts[han]; counts[han] = 0; }
            int best = -1;
            for (int i = 0; i < counts.Length; i++) if (counts[i] >= 2 && (best < 0 || Weight(i, counts) > Weight(best, counts))) best = i;
            if (best >= 0 && Weight(best, counts) * 2 >= latin) return ScriptLanguage(ScriptNames[best], text, lean);
            return latin == 0 ? null : LatinLanguage(text, lean);
        }

        private static int Weight(int script, int[] counts)
        {
            string name = ScriptNames[script];
            return name == "Japanese" || name == "Korean" || name == "Chinese" ? counts[script] * 2 : counts[script];
        }

        private static bool In(char letter, int[] ranges)
        {
            for (int at = 0; at + 1 < ranges.Length; at += 2) if (letter >= ranges[at] && letter <= ranges[at + 1]) return true;
            return false;
        }

        private static readonly string[] ScriptNames =
        {
            "Japanese", "Korean", "Chinese", "Cyrillic", "Greek", "Georgian", "Armenian", "Amharic", "Thai", "Lao", "Khmer", "Burmese",
            "Devanagari", "Bengali", "Punjabi", "Gujarati", "Tamil", "Telugu", "Kannada", "Malayalam", "Arabic script", "Hebrew"
        };

        private static readonly int[][] ScriptRanges =
        {
            new[] { 0x3040, 0x30FF, 0x31F0, 0x31FF, 0xFF66, 0xFF9D },
            new[] { 0xAC00, 0xD7FF, 0x1100, 0x11FF, 0x3130, 0x318F, 0xA960, 0xA97F },
            new[] { 0x4E00, 0x9FFF, 0x3400, 0x4DBF, 0xF900, 0xFAFF },
            new[] { 0x0400, 0x052F, 0x1C80, 0x1C8F, 0x2DE0, 0x2DFF, 0xA640, 0xA69F },
            new[] { 0x0370, 0x03FF, 0x1F00, 0x1FFF },
            new[] { 0x10A0, 0x10FF, 0x1C90, 0x1CBF, 0x2D00, 0x2D2F },
            new[] { 0x0530, 0x058F },
            new[] { 0x1200, 0x139F, 0x2D80, 0x2DDF, 0xAB00, 0xAB2F },
            new[] { 0x0E00, 0x0E7F },
            new[] { 0x0E80, 0x0EFF },
            new[] { 0x1780, 0x17FF, 0x19E0, 0x19FF },
            new[] { 0x1000, 0x109F, 0xA9E0, 0xA9FF, 0xAA60, 0xAA7F },
            new[] { 0x0900, 0x097F, 0xA8E0, 0xA8FF },
            new[] { 0x0980, 0x09FF },
            new[] { 0x0A00, 0x0A7F },
            new[] { 0x0A80, 0x0AFF },
            new[] { 0x0B80, 0x0BFF },
            new[] { 0x0C00, 0x0C7F },
            new[] { 0x0C80, 0x0CFF },
            new[] { 0x0D00, 0x0D7F },
            new[] { 0x0600, 0x06FF, 0x0750, 0x077F, 0x08A0, 0x08FF, 0xFB50, 0xFDFF, 0xFE70, 0xFEFC },
            new[] { 0x0590, 0x05FF, 0xFB1D, 0xFB4F }
        };

        private static string ScriptLanguage(string script, string text, string lean)
        {
            switch (script)
            {
                case "Chinese": return lean == "Japanese" ? "Japanese" : Chinese(text, lean);
                case "Cyrillic": return Cyrillic(text, lean);
                case "Devanagari": return Devanagari(text, lean);
                case "Arabic script": return ArabicScript(text);
                default: return Known(script);
            }
        }

        private const string TraditionalOnly = "這們個來時為說會對國與後從開關嗎麼還點讓實現發經長學問題聽見覺謝電話頭邊樣種聲歡愛裡遊戲網沒鐘機東車馬魚鳥";
        private const string SimplifiedOnly = "这们个来时为说会对国与后从开关吗么还点让实现发经长学问题听见觉谢电话头边样种声欢爱里游戏网没钟机东车马鱼鸟";

        private static string Chinese(string text, string lean)
        {
            int traditional = 0, simplified = 0;
            foreach (char letter in text)
            {
                if (TraditionalOnly.IndexOf(letter) >= 0) traditional++;
                if (SimplifiedOnly.IndexOf(letter) >= 0) simplified++;
            }
            if (traditional > simplified) return "Traditional Chinese";
            if (simplified > traditional) return "Simplified Chinese";
            return lean == "Traditional Chinese" ? "Traditional Chinese" : "Simplified Chinese";
        }

        private static bool Any(string text, string letters)
        {
            foreach (char letter in text.ToLowerInvariant()) if (letters.IndexOf(letter) >= 0) return true;
            return false;
        }

        private static readonly string[] CyrillicLanguages = { "Russian", "Ukrainian", "Belarusian", "Bulgarian", "Macedonian", "Serbian", "Kazakh", "Mongolian" };

        private static string Cyrillic(string text, string lean)
        {
            if (Any(text, "ў")) return Any(text, "ҳқғ") ? "Uzbek" : "Belarusian";
            if (Any(text, "әғқңұһ")) return "Kazakh";
            if (Any(text, "өү")) return "Mongolian";
            if (Any(text, "ѓќѕ")) return "Macedonian";
            if (Any(text, "ђћ")) return "Serbian";
            if (Any(text, "јљњџ")) return lean == "Macedonian" ? "Macedonian" : "Serbian";
            if (Any(text, "їєґ")) return "Ukrainian";
            if (Any(text, "і")) return Any(text, "ыэ") ? "Belarusian" : "Ukrainian";
            if (Any(text, "ыэё")) return "Russian";
            string best = Best(Scores(text, CyrillicWords, ref cyrillicWords, 1), lean, 1, 0);
            if (best != null) return best;
            return Array.IndexOf(CyrillicLanguages, lean) >= 0 ? lean : "Russian";
        }

        private static string Devanagari(string text, string lean)
        {
            string best = Best(Scores(text, DevanagariWords, ref devanagariWords, 1), lean, 1, 0);
            if (Any(text, "ळ") && best != "Nepali") return "Marathi";
            if (best != null) return best;
            return lean == "Marathi" || lean == "Nepali" ? lean : "Hindi";
        }

        private static string ArabicScript(string text)
        {
            if (Any(text, "ےںٹڈڑھ")) return "Urdu";
            if (Any(text, "پچژگ")) return "Persian";
            bool arabic = Any(text, "ةيك"), persian = Any(text, "یک");
            return persian && !arabic ? "Persian" : "Arabic";
        }

        private static readonly string[,] CyrillicWords =
        {
            { "Russian", "привет как дела что это ты мы вы он она они не да нет спасибо пожалуйста хорошо очень сегодня вечером играть хочешь можешь мой твой тоже сейчас здесь по за из от до для но или если ещё уже все всё только меня тебя давай почему где когда" },
            { "Bulgarian", "здравей здрасти как си съм са на за от до не да какво защо но или ако благодаря моля добре много днес вечерта искаш можеш мой твой също сега тук това този тази ли ще нещо" },
            { "Ukrainian", "що як це ти ми ви він вона вони не так ні дякую будь ласка добре дуже сьогодні ввечері грати хочеш можеш мій твій теж зараз тут але або якщо вже" },
            { "Serbian", "здраво како си сам је су на са за од до не да шта зашто али или ако хвала молим добро много данас вечерас играти хоћеш можеш мој твој такође сада овде" },
            { "Macedonian", "здраво како си сум се на во со за од до не да што зошто но или ако благодарам те молам добро многу денес вечерва сакаш можеш мој твој исто сега тука" }
        };

        private static readonly string[,] DevanagariWords =
        {
            { "Hindi", "है हैं और मैं का की के नहीं हूँ हूं तुम आप क्या कैसे क्यों यह वह में से को पर भी था थी हो रहा रही धन्यवाद अच्छा बहुत आज खेलना" },
            { "Marathi", "आहे आहेत आणि मी तू तुम्ही काय कसे का हे ते मध्ये ला ने नाही होते धन्यवाद छान खूप आज खेळायचे आहेस" },
            { "Nepali", "छ छन् छु छौ हो र म तिमी तपाईं के कसरी किन यो त्यो मा ले लाई पनि थियो धन्यवाद राम्रो धेरै आज खेल्न हुन्छ" }
        };

        private static readonly string[,] LatinWords =
        {
            { "English", "the and is are you your im i'm it's its what what's how why this that that's with have was not but can can't do don't dont my me to of in it for on be just so like hey hi hello thanks thank please yes know want going good really think about will would there they we he she all get got now today tonight let's lets play game hows whats wanna gonna you're isn't didn't", "" },
            { "Spanish", "el la los las que qué de en es por para con una uno un pero cómo como está estás estoy soy eres muy más mi tu te yo hola gracias sí también bien hoy esta este eso esto quieres puedes vamos ahora porque cuando dónde donde nosotros ellos hacer tengo tienes hay del al lo le se su sus buenos días noche jugar juego tal", "ñ¿¡" },
            { "French", "le la les des un une et est je tu il elle nous vous ils que qui quoi pas ne de du en dans pour avec sur mais ou où ce cette c'est j'ai suis êtes sont ai avoir être faire bonjour salut merci oui non très bien aussi comment pourquoi quand mon ma mes ton ta tes ça va veux peux au aux chez moi toi lui leur jouer soir aujourd'hui", "œêûùïëç" },
            { "German", "der die das und ist ich du er sie es wir ihr nicht ein eine einen zu mit auf für von den dem was wie warum ja nein auch aber oder wenn dann noch schon mein dein bin bist sind hast habe hallo danke bitte gut sehr heute abend spielen willst kannst möchte mal doch jetzt hier gibt", "ßäöü" },
            { "Italian", "il lo la gli le di che è un una per non con sono sei siamo ho hai abbiamo mi ti ci si cosa perché quando dove ciao grazie sì anche molto bene oggi stasera giocare vuoi puoi questo questa quello nel nella del della al alla ma io lui lei noi voi loro", "èìòù" },
            { "Portuguese", "os de da dos das é um uma que não nao com para pra por em na eu você voce vc ele ela nós eles meu minha seu sua tem tenho está estou sou são muito bem obrigado obrigada oi olá sim também hoje noite jogar quer pode isso isto mas como quando onde porque agora aqui vamos tudo", "ãõçêô" },
            { "Catalan", "els les és que del amb per però què quan jo nosaltres vosaltres ells gràcies bé molt avui aquesta nit vols pots meu meva teu teva també ara aquí això aquest sóc ets estic estàs tinc tens", "ŀ" },
            { "Galician", "unha miña túa teño moito grazas ola tamén hoxe xogar queres podes iso isto cando onde agora aquí non nós vós eles", "" },
            { "Basque", "eta da dira naiz zara ni zu hura gu zuek haiek ez bai zer nola zergatik baina edo kaixo eskerrik asko mila esker ondo oso gaur jolastu nahi dut duzu du dugu nire zure ere orain hemen hau hori", "" },
            { "Dutch", "de het een en ik je jij hij zij wij niet wat hoe waarom van op met voor maar dat dit zijn ben bent heb hebt heeft hallo hoi dank bedankt ja nee goed heel vandaag vanavond spelen wil kun kan mijn jouw ook nog naar te zo", "" },
            { "Afrikaans", "die en ek jy hy sy ons julle hulle nie wat hoe hoekom maar ja nee hallo dankie baie goed vandag vanaand speel wil kan jou ook nou hier het 'n vir", "" },
            { "Swedish", "och är jag du han hon vi ni det den en ett inte att som på med för av till vad hur varför eller om så hej tack ja nej bra mycket idag ikväll spela vill kan min mitt din ditt också har var här nu", "å" },
            { "Danish", "og er jeg du han hun vi det den en et ikke at som på med af til hvad hvordan hvorfor eller om så hej tak ja nej godt meget spille vil kan min mit din dit også har var her nu mig dig", "æø" },
            { "Norwegian", "og er jeg du han hun vi dere det den en et ikke at som på med av til hva hvordan hvorfor eller om så hei takk ja nei bra veldig kveld spille vil kan min mitt din ditt også har var her nå meg deg", "æø" },
            { "Icelandic", "og er ég þú hann hún við þið þeir ekki hvað hvernig en eða ef já nei halló hæ takk gott mjög dag kvöld spila vilt getur minn mín þinn þín líka núna hér", "þð" },
            { "Finnish", "ja on olen olet ei en et minä mä sinä sä hän mitä miten miksi mutta tai kun niin kyllä joo moi hei kiitos hyvä hyvin tänään illalla pelata haluatko voitko minun sinun myös nyt täällä tämä tuo", "" },
            { "Estonian", "ja on olen oled ei need mina sina sa tema ta meie teie nemad nad mida kuidas miks aga või kui nii jah tere aitäh hästi väga täna õhtul mängida tahad saad minu sinu ka nüüd siin", "õ" },
            { "Latvian", "un ir es tu viņš viņa mēs jūs viņi nav kas kā kāpēc bet vai ja tā jā nē sveiki sveiks paldies labi ļoti šodien vakarā spēlēt gribi vari mans mana tavs tava arī tagad šeit", "āēīģķļņ" },
            { "Lithuanian", "ir yra aš tu jis ji mes jūs jie ne kas kaip kodėl bet ar jei taip labas ačiū gerai labai šiandien vakare žaisti nori gali mano tavo dabar čia", "ėįų" },
            { "Polish", "na nie się sie że ze jest jestem jesteś ty ja wy ona oni co jak dlaczego czy ale od po dla tak cześć czesc dzięki dzieki dziękuję proszę dobrze bardzo dzisiaj dziś wieczorem grać chcesz możesz mój moja twój też tez już juz mam masz ma mnie ci tego", "łśżźćńęą" },
            { "Czech", "je jsem jsi jsou že se na ve co jak proč ale nebo když tak ano ne ahoj děkuji díky prosím dobře moc velmi dnes večer hrát chceš můžeš můj tvůj taky také už mám máš má mě tě být", "řěů" },
            { "Slovak", "je som si sú že sa na vo čo ako prečo ale alebo keď tak áno nie ahoj ďakujem vďaka prosím dobre veľmi dnes večer hrať chceš môžeš môj tvoj aj už mám máš má ťa", "ľĺŕ" },
            { "Hungarian", "az és nem igen én te ő ti ők hogy mit hogyan miért de vagy ha akkor szia köszönöm köszi jó jól nagyon ma este játszani akarsz tudsz vagyok van egy ez azt meg már még csak", "őű" },
            { "Slovenian", "je sem si smo ste jaz ti ona mi vi oni ne da kaj kako zakaj ampak ali če živjo zdravo hvala dobro zelo danes nocoj igrati želiš lahko moj tvoj tudi zdaj tukaj na za", "" },
            { "Croatian", "je sam si smo ste su ja ti ona mi vi oni ne da što kako zašto ali ili ako bok hvala dobro jako vrlo danas večeras igrati hoćeš možeš moj tvoj također sada ovdje ovo tko", "đ" },
            { "Bosnian", "je sam si smo ste su ja ti ona mi vi oni ne da šta kako zašto ali ili ako zdravo hvala dobro jako vrlo danas večeras igrati hoćeš možeš moj tvoj također sada ovdje ovo ko", "đ" },
            { "Serbian", "je sam si smo ste su ja ti ona mi vi oni ne da šta kako zašto ali ili ako zdravo ćao hvala dobro jako vrlo danas večeras igrati hoćeš možeš moj tvoj takođe sada ovde ovo ko", "đ" },
            { "Albanian", "dhe është jam je jemi jeni janë unë ti ai ajo ne ju ata nuk çfarë si pse por ose nëse po jo përshëndetje faleminderit mirë shumë sot sonte luaj mund yt gjithashtu tani këtu një në për të", "ë" },
            { "Romanian", "și si este sunt eu tu el ea noi voi ei nu ce cum dar sau dacă daca da salut bună mulțumesc mersi bine foarte azi astăzi diseară să sa joci vrei poți meu mea tău acum aici la cu pe în", "șțăşţ" },
            { "Irish", "agus tá níl mé tú sé sí muid sibh siad conas ach nó má duit haigh raibh maith agat inniu anocht imirt mhaith féidir freisin anois anseo ag ar", "" },
            { "Welsh", "ac yw ydy mae dw rwy ti chi ni nhw beth sut pam ond neu os ie nage helo shwmae diolch iawn heddiw heno chwarae eisiau gallu fy dy hefyd nawr rŵan yma yr yn gyda", "ŵŷ" },
            { "Maltese", "hu hi huma jien int aħna intom mhux kif għaliex imma jew jekk iva le bonġu ċaw grazzi tajjeb ħafna illum illejla trid tiegħi tiegħek ukoll issa hawn", "ħġċ" },
            { "Turkish", "ve bir bu şu ben sen biz siz onlar ne nasıl neden niye ama veya eğer evet hayır merhaba selam teşekkürler sağol iyi çok bugün akşam oynamak istiyor musun mısın benim senin için ile var yok mi mı şimdi burada", "ığ" },
            { "Azerbaijani", "və bir bu mən sən biz siz onlar nə necə niyə amma ya əgər bəli hə xeyr yox salam təşəkkürlər yaxşı çox axşam oynamaq istəyirsən bilərsən mənim sənin də üçün ilə var indi burada", "ə" },
            { "Uzbek", "va bir bu sen biz siz ular nima qanday nega lekin yoki agar ha yo'q salom rahmat yaxshi juda bugun kechqurun xohlaysan mening sening ham hozir uchun bilan bor", "ʻ" },
            { "Vietnamese", "tôi bạn anh chị em là và của có không gì sao nhưng hoặc nếu vâng dạ chào cảm ơn tốt rất hôm nay tối chơi muốn được này đó đây rồi chưa cũng bây giờ với cho một những các", "ơưạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ" },
            { "Indonesian", "yang dan di ke dari ini itu aku saya kamu anda dia kita kami mereka apa bagaimana kenapa mengapa tapi atau kalau jika ya tidak nggak gak halo hai makasih terima kasih baik sangat banget hari malam mau bisa sudah belum juga sekarang sini dengan untuk ada", "" },
            { "Malay", "yang dan di ke dari ini itu aku saya kamu awak anda dia kita kami mereka apa bagaimana kenapa mengapa tetapi tapi atau kalau jika ya tidak tak helo hai terima kasih baik sangat hari malam mahu boleh sudah belum juga sekarang sini dengan untuk ada", "" },
            { "Javanese", "aku kowe sampeyan panjenengan iku iki ora opo piye kepiye ngopo lan karo ning nang wis durung arep iso matur nuwun suwun apik saiki kene ono dheweke", "" },
            { "Filipino", "ang ng sa mga si ni ako ikaw ka siya kami tayo kayo sila ano paano bakit pero kung oo hindi salamat kumusta mabuti napaka ngayon gabi maglaro gusto mo ko ba na naman din rin dito wala po", "" },
            { "Swahili", "na ni ya wa kwa katika la za cha mimi wewe yeye sisi ninyi wao nini vipi jinsi lakini au kama ndiyo ndio hapana habari jambo asante nzuri sana leo usiku kucheza unataka unaweza yangu yako pia sasa hapa hii hiyo", "" },
            { "Hausa", "da ne ce na ta ka ki shi ita mu ku su ba yaya yadda amma ko idan eh sannu nagode gode lafiya sosai yau dare wasa kana kina iya nawa naka kuma yanzu nan", "ƙɗɓƴ" },
            { "Yoruba", "ni ti si mo wọn kí kini báwo bawo idi ṣugbọn tabi bí bẹẹni rara ẹ ṣeun dáadáa pupọ loni alẹ ṣere fẹ le temi tire bayi nibi", "ẹṣ" },
            { "Igbo", "na nke ka gị ya anyị unu ha gịnị kedu olee mana ee mba ndewo daalụ ọma taa abalị egwu chọrọ nwere ugbu ebe", "ịụṅ" },
            { "Zulu", "ngi ukuthi futhi kodwa noma uma yebo cha sawubona ngiyabonga kahle kakhulu namuhla kusihlwa ukudlala ufuna ungakwazi wami wakho manje lapha lokhu lowo unjani ngikhona", "" },
            { "Xhosa", "ndi ukuba kwaye kodwa okanye ewe hayi molo molweni enkosi kakuhle kakhulu namhlanje ngokuhlwa ukudlala ufuna unako wam wakho ngoku apha oku eso unjani ndiphilile", "" },
            { "Somali", "waa iyo ku ka la ma aan aniga adiga isaga iyada annaga idinka iyaga maxaa sidee maxay laakiin ama haddii haa maya salaan mahadsanid fiican aad maanta caawa ciyaar rabtaa kartaa kayga kaaga hadda halkan", "" }
        };

        private static Dictionary<string, HashSet<string>> latinWords, cyrillicWords, devanagariWords;
        private static readonly object wordsGate = new object();

        private static Dictionary<string, HashSet<string>> Words(string[,] table, ref Dictionary<string, HashSet<string>> made)
        {
            lock (wordsGate)
            {
                if (made != null) return made;
                Dictionary<string, HashSet<string>> words = new Dictionary<string, HashSet<string>>();
                for (int i = 0; i < table.GetLength(0); i++)
                    words[table[i, 0]] = new HashSet<string>(table[i, 1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
                made = words;
                return made;
            }
        }

        private static readonly Regex Token = new Regex(@"[\p{L}\p{M}]+(?:'[\p{L}\p{M}]+)*");

        private static List<string> Tokens(string text)
        {
            List<string> tokens = new List<string>();
            string plain = text.Replace((char)0x2019, '\'').Replace((char)0x2018, '\'').Replace((char)0x02BC, '\'').ToLowerInvariant();
            foreach (Match word in Token.Matches(plain)) if (word.Value.Length > 1) tokens.Add(word.Value);
            return tokens;
        }

        private static Dictionary<string, int> Scores(string text, string[,] table, ref Dictionary<string, HashSet<string>> made, int each)
        {
            Dictionary<string, HashSet<string>> words = Words(table, ref made);
            Dictionary<string, int> scores = new Dictionary<string, int>();
            foreach (string token in Tokens(text))
                foreach (KeyValuePair<string, HashSet<string>> language in words)
                    if (language.Value.Contains(token)) scores[language.Key] = (scores.ContainsKey(language.Key) ? scores[language.Key] : 0) + each;
            return scores;
        }

        private static string LatinLanguage(string text, string lean)
        {
            Dictionary<string, int> scores = Scores(text, LatinWords, ref latinWords, 2);
            string lower = text.ToLowerInvariant();
            for (int i = 0; i < LatinWords.GetLength(0); i++)
            {
                string letters = LatinWords[i, 2];
                if (letters.Length == 0) continue;
                int found = 0;
                foreach (char letter in lower) if (letters.IndexOf(letter) >= 0) found++;
                if (found > 0) scores[LatinWords[i, 0]] = (scores.ContainsKey(LatinWords[i, 0]) ? scores[LatinWords[i, 0]] : 0) + Math.Min(6, found * 3);
            }
            return Best(scores, lean, 4, 2);
        }

        private static string Best(Dictionary<string, int> scores, string lean, int least, int margin)
        {
            int top = 0;
            foreach (int score in scores.Values) top = Math.Max(top, score);
            if (top < least) return null;
            int leaning;
            if (lean != null && scores.TryGetValue(lean, out leaning) && leaning >= top - margin) return Known(lean);
            List<string> tied = new List<string>();
            foreach (KeyValuePair<string, int> score in scores) if (score.Value == top) tied.Add(score.Key);
            return tied.Count == 1 ? Known(tied[0]) : null;
        }
    }
}
