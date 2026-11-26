using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CapriDesktop
{
    // Code mods on Mac and Linux: the same .capri-code files as Windows, compiled with Roslyn as C# 5, the language Windows'
    // own compiler reads, so a mod that runs here runs there too. Each mod loads into its own collectible context, let go when
    // it stops, so turning a mod off and on doesn't keep every old copy.
    internal sealed partial class Mods
    {
        private static readonly object compiling = new object();
        private static List<MetadataReference> references;
        private static readonly Dictionary<ICapriMod, AssemblyLoadContext> loaded = new Dictionary<ICapriMod, AssemblyLoadContext>(ReferenceEqualityComparer.Instance);

        // The parts of the mod API only Windows has, in the order her line names them.
        private static readonly string[] WindowsOnlyParts = { "Windows Forms", "System.Drawing pictures", "Art", "Startup Intros", "Ui.Drawing", "Ui.SetOptions", "Ui.Window and Mainframe.Window", "Mainframe.ShowPage", "Screen.Looked and Screen.Capture" };

        // Types only Windows has, by the name a mod writes for them.
        private static readonly Dictionary<string, string> WindowsOnlyTypes = new Dictionary<string, string>
        {
            { "Bitmap", "System.Drawing pictures" }, { "Graphics", "System.Drawing pictures" }, { "Brush", "System.Drawing pictures" }, { "Brushes", "System.Drawing pictures" },
            { "Pen", "System.Drawing pictures" }, { "Pens", "System.Drawing pictures" }, { "Font", "System.Drawing pictures" }, { "Image", "System.Drawing pictures" },
            { "SolidBrush", "System.Drawing pictures" }, { "ICapriIntro", "Startup Intros" }, { "CapriFrame", "Ui.Drawing" }
        };

        // Members of her API only Windows has: the interface, the member and the part.
        private static readonly string[,] WindowsOnlyMembers =
        {
            { "ICapriHost", "Art", "Art" },
            { "ICapriUi", "Drawing", "Ui.Drawing" },
            { "ICapriUi", "SetOptions", "Ui.SetOptions" },
            { "ICapriUi", "Window", "Ui.Window and Mainframe.Window" },
            { "ICapriMainframe", "Window", "Ui.Window and Mainframe.Window" },
            { "ICapriMainframe", "ShowPage", "Mainframe.ShowPage" },
            { "ICapriScreen", "Looked", "Screen.Looked and Screen.Capture" },
            { "ICapriScreen", "Capture", "Screen.Looked and Screen.Capture" }
        };

        // A mod that reaches for a part only Windows has is refused before she asks to run it, naming those parts. Each name is
        // looked up the way the compiler would, so a mod's own Font property or settings.Window isn't taken for Windows' one.
        partial void CheckPlatform(string text)
        {
            text = text ?? "";
            CheckShape(text);
            // Looking names up costs most of a second for a big mod, so it's only done for one that writes a word it could be.
            if (!WindowsOnlyWords.IsMatch(text)) return;
            HashSet<string> found = new HashSet<string>();
            try
            {
                SyntaxTree tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.CSharp5));
                List<IdentifierNameSyntax> names = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Where(MaybeWindowsOnly).ToList();
                if (names.Count == 0) return;
                SemanticModel model;
                lock (compiling) model = CSharpCompilation.Create("capri-mod-check", new[] { tree }, References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)).GetSemanticModel(tree);
                foreach (IdentifierNameSyntax name in names)
                {
                    string part = WindowsOnlyPart(name, model);
                    if (part != null) found.Add(part);
                }
            }
            catch (InsufficientExecutionStackException) { throw TooDeep(); }
            if (found.Count > 0) throw new ModException("That mod uses parts of Capri only Windows has (" + string.Join(", ", WindowsOnlyParts.Where(found.Contains)) + "), so it can't run on " + (OperatingSystem.IsMacOS() ? "a Mac" : "Linux") + ".");
        }

        // Every word a Windows-only part can be written with, and \u escapes, which can spell one.
        private static readonly Regex WindowsOnlyWords = new Regex(@"\b(?:Bitmap|Graphics|Brushes?|Pens?|Font|Image|SolidBrush|ICapriIntro|CapriFrame|Art|Drawing|SetOptions|Window|ShowPage|Looked|Capture|Forms)\b|\\[uU]", RegexOptions.CultureInvariant);

        // Roslyn's parser and binder call themselves once for each bracket, operator, cast or dot a mod nests, and some of those
        // calls have no guard, so a mod nested thousands deep ended her with a stack overflow, or stalled her for seconds, before
        // she could ask about it. One pass over its text refuses that first: brackets and type arguments open at once, and the
        // operators, dots and casts of one expression.
        private const int DeepestNesting = 100, LongestChain = 500;

        private static void CheckShape(string text)
        {
            if (System.Text.Encoding.UTF8.GetByteCount(text) > LargestCode) throw new ModException("That code mod is too big.");
            int depth = 0, angles = 0, chain = 0, ended = -1;
            bool conditional = false;
            // Each else if is inside the one before it, braces or not, so they're counted by how deep the chain sits, until
            // a statement there ends without an else after it.
            int[] elseIfs = new int[DeepestNesting + 2];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i], next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '/' && next == '/') { while (i + 1 < text.Length && !LineEnd(text[i + 1])) i++; continue; }
                if (c == '/' && next == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal), stop = end < 0 ? text.Length : end + 2;
                    if (conditional && Directive(text, i, stop)) throw Unreadable();
                    i = stop - 1;
                    continue;
                }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '$') throw new ModException("That code mod uses a $ string, which code mods' C# 5 doesn't have.");
                if (c == '#')
                {
                    int start = i;
                    while (i + 1 < text.Length && !LineEnd(text[i + 1])) i++;
                    string line = text.Substring(start + 1, i - start).TrimStart();
                    string name = new string(line.TakeWhile(char.IsLetter).ToArray());
                    if (name == "if" || name == "elif" || name == "else" || name == "endif" || name == "define" || name == "undef") conditional = true;
                    if ((name == "if" || name == "elif") && line.Count(part => "(!&|=".IndexOf(part) >= 0) > DeepestNesting) throw TooDeep();
                    continue;
                }
                if ((char.IsLetter(c) || c == '_') && (i == 0 || !char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_' && text[i - 1] != '@'))
                {
                    int start = i;
                    while (i + 1 < text.Length && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_')) i++;
                    string word = text.Substring(start, i - start + 1);
                    if (ended >= 0 && word != "else") elseIfs[ended] = 0;
                    ended = -1;
                    if (word == "else" && Word(text, i + 1) == "if" && ++elseIfs[depth] > LongestChain) throw TooDeep();
                    if (word == "do" && ++chain > LongestChain) throw TooDeep();
                    continue;
                }
                if (ended >= 0) { elseIfs[ended] = 0; ended = -1; }
                if (c == '@' && next == '"')
                {
                    int start = i;
                    for (i += 2; i < text.Length; i++) if (text[i] == '"') { if (i + 1 < text.Length && text[i + 1] == '"') i++; else break; }
                    if (conditional && Directive(text, start, Math.Min(i, text.Length))) throw Unreadable();
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    for (i++; i < text.Length && text[i] != c && !LineEnd(text[i]); i++) if (text[i] == '\\' && i + 1 < text.Length && !LineEnd(text[i + 1])) i++;
                    if (i < text.Length && LineEnd(text[i])) i--;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{') { if (++depth > DeepestNesting) throw TooDeep(); }
                else if (c == ')' || c == ']' || c == '}') depth = Math.Max(0, depth - 1);
                if (c == '<') { if (++angles > DeepestNesting) throw TooDeep(); }
                else if (c == '>') angles = Math.Max(0, angles - 1);
                if (c == ';' || c == '{' || c == '}') angles = chain = 0;
                else if (c == ',') chain = 0;
                else if ("=?:!~-+*/%&|^<>.[".IndexOf(c) >= 0 || c == ')' && Operand(text, i + 1)) { if (++chain > LongestChain) throw TooDeep(); }
                if (c == ';' || c == '}') ended = depth;
            }
        }

        private static string Word(string text, int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            int start = at;
            while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_')) at++;
            return text.Substring(start, at - start);
        }

        // Whether a value starts here, as after a cast or an if without braces, each another call deep for her parser.
        private static bool Operand(string text, int at)
        {
            while (at < text.Length && (text[at] == ' ' || text[at] == '\t' || text[at] == '\r' || text[at] == '\n')) at++;
            if (at >= text.Length) return false;
            char c = text[at];
            return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '"' || c == '\'' || c == '(' || c == '-' || c == '+' || c == '!' || c == '~';
        }

        private static ModException TooDeep() { return new ModException("That code mod nests too deeply to read safely."); }

        private static ModException Unreadable() { return new ModException("That code mod runs a comment or @ string across an #if line, so I can't read it safely."); }

        private static bool LineEnd(char c) { return c == '\n' || c == '\r' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029; }

        private static bool Directive(string text, int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                if (!LineEnd(text[i])) continue;
                int at = i + 1;
                while (at < to && !LineEnd(text[at]) && char.IsWhiteSpace(text[at])) at++;
                if (at < to && text[at] == '#') return true;
            }
            return false;
        }

        private static bool MaybeWindowsOnly(IdentifierNameSyntax name)
        {
            string text = name.Identifier.ValueText, space = Qualifier(name);
            if (WindowsOnlyTypes.ContainsKey(text) || space != null && (space == "System.Windows" || space == "System.Drawing" || space.StartsWith("System.Drawing.", StringComparison.Ordinal))) return true;
            for (int i = 0; i < WindowsOnlyMembers.GetLength(0); i++)
                if (WindowsOnlyMembers[i, 1] == text) return true;
            return false;
        }

        // What's written before a name and its dot, like "System.Drawing" for System.Drawing.Bitmap, or null for a name on its own.
        private static string Qualifier(IdentifierNameSyntax name)
        {
            MemberAccessExpressionSyntax access = name.Parent as MemberAccessExpressionSyntax;
            QualifiedNameSyntax qualified = name.Parent as QualifiedNameSyntax;
            SyntaxNode left = access != null && access.Name == name ? access.Expression : qualified != null && qualified.Right == name ? qualified.Left : null;
            // A namespace like System.Drawing.Imaging is short, and reading out a long chain of calls for every name in it was slow.
            return left == null || left.Span.Length > 100 ? null : Regex.Replace(left.ToString(), @"\s+|^global::", "");
        }

        private static string WindowsOnlyPart(IdentifierNameSyntax name, SemanticModel model)
        {
            if (name.Parent is NameEqualsSyntax || name.Parent is NameColonSyntax || !Unresolved(name, model)) return null;
            string text = name.Identifier.ValueText, space = Qualifier(name);
            MemberAccessExpressionSyntax access = name.Parent as MemberAccessExpressionSyntax;
            // On a value, like capri.Ui.Window, it's only Windows' when the value is the API part that has it there.
            if (access != null && access.Name == name && !(model.GetSymbolInfo(access.Expression).Symbol is INamespaceSymbol))
            {
                ITypeSymbol receiver = model.GetTypeInfo(access.Expression).Type;
                for (int i = 0; i < WindowsOnlyMembers.GetLength(0); i++)
                    if (WindowsOnlyMembers[i, 1] == text && IsApi(receiver, WindowsOnlyMembers[i, 0])) return WindowsOnlyMembers[i, 2];
                return null;
            }
            if (space == "System.Windows" && text == "Forms") return "Windows Forms";
            if (space != null && (space == "System.Drawing" || space.StartsWith("System.Drawing.", StringComparison.Ordinal))) return "System.Drawing pictures";
            string part;
            return WindowsOnlyTypes.TryGetValue(text, out part) ? part : null;
        }

        // Windows-only parts aren't in the API here, so a name that reaches one finds nothing.
        private static bool Unresolved(IdentifierNameSyntax name, SemanticModel model)
        {
            SymbolInfo info = model.GetSymbolInfo(name);
            return info.Symbol == null && info.CandidateSymbols.IsEmpty;
        }

        private static bool IsApi(ITypeSymbol type, string name)
        {
            if (type == null || type.TypeKind == TypeKind.Error) return false;
            Func<ITypeSymbol, bool> same = each => each.Name == name && each.ContainingNamespace != null && each.ContainingNamespace.ToDisplayString() == "CapriDesktop";
            return same(type) || type.AllInterfaces.Any(each => same(each));
        }

        partial void Unload(ICapriMod mod)
        {
            if (mod == null) return;
            AssemblyLoadContext context;
            lock (loaded)
            {
                if (!loaded.TryGetValue(mod, out context)) return;
                loaded.Remove(mod);
            }
            try { context.Unload(); }
            catch (InvalidOperationException) { }
        }

        private static ICapriMod Compile(string text)
        {
            CheckShape(text ?? "");
            SyntaxTree tree;
            try { tree = CSharpSyntaxTree.ParseText(text ?? "", new CSharpParseOptions(LanguageVersion.CSharp5)); }
            catch (InsufficientExecutionStackException) { throw TooDeep(); }
            CSharpCompilation compilation;
            lock (compiling) compilation = CSharpCompilation.Create("capri-mod-" + Guid.NewGuid().ToString("N"), new[] { tree }, References(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            byte[] built;
            using (MemoryStream output = new MemoryStream())
            {
                Microsoft.CodeAnalysis.Emit.EmitResult result;
                try { result = compilation.Emit(output); }
                catch (Exception error) { throw new ModException("Couldn't compile that mod: " + error.Message); }
                if (!result.Success)
                {
                    Diagnostic first = result.Diagnostics.First(each => each.Severity == DiagnosticSeverity.Error);
                    throw new ModException("That code didn't compile, line " + (first.Location.GetLineSpan().StartLinePosition.Line + 1) + ": " + first.GetMessage());
                }
                built = output.ToArray();
            }
            AssemblyLoadContext context = new AssemblyLoadContext("capri mod", true);
            List<Type> mods = new List<Type>();
            try
            {
                Assembly assembly = context.LoadFromStream(new MemoryStream(built));
                foreach (Type type in assembly.GetTypes())
                    if (typeof(ICapriMod).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface) mods.Add(type);
            }
            catch (Exception error)
            {
                context.Unload();
                if (error is ReflectionTypeLoadException || error is TypeLoadException || error is BadImageFormatException || error is FileLoadException) throw new ModException("Couldn't load that mod: " + error.Message);
                throw;
            }
            if (mods.Count != 1) { context.Unload(); throw new ModException("A code mod needs exactly one class that implements ICapriMod. That one has " + mods.Count + "."); }
            ICapriMod made;
            try { made = (ICapriMod)Activator.CreateInstance(mods[0]); }
            catch (Exception error) { context.Unload(); throw new ModException("That mod failed while loading: " + Inner(error).Message); }
            lock (loaded) loaded[made] = context;
            return made;
        }

        // What a mod compiles against: her own assemblies and the runtime's, read from memory, since a one-file build has no
        // files to point at. A mod's usings reach the same namespaces Windows gives it, less Windows Forms and GDI.
        private static List<MetadataReference> References()
        {
            if (references != null) return references;
            foreach (string name in new[] { "System.Runtime", "System.Collections", "System.Linq", "System.Text.RegularExpressions", "System.Drawing.Primitives", "System.ComponentModel.Primitives",
                "System.Console", "System.Threading", "System.Threading.Thread", "System.IO", "System.Net.Http", "System.Net.Primitives", "System.Diagnostics.Process", "System.ObjectModel", "System.Runtime.Extensions",
                "System.Collections.Concurrent", "System.Private.Uri", "System.Text.Encoding.Extensions", "System.Xml.ReaderWriter", "System.Private.Xml", "System.Runtime.Serialization.Json", "netstandard" })
            {
                try { Assembly.Load(new AssemblyName(name)); }
                catch (Exception error) when (error is FileNotFoundException || error is FileLoadException || error is BadImageFormatException) { }
            }
            List<MetadataReference> made = new List<MetadataReference>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default) continue;
                string name = assembly.GetName().Name;
                if (name == null || !seen.Add(name) || name.StartsWith("capri-mod-", StringComparison.Ordinal)) continue;
                MetadataReference reference = Reference(assembly);
                if (reference != null) made.Add(reference);
            }
            references = made;
            return made;
        }

        private static unsafe MetadataReference Reference(Assembly assembly)
        {
            try
            {
                byte* blob;
                int length;
                if (assembly.TryGetRawMetadata(out blob, out length)) return AssemblyMetadata.Create(ModuleMetadata.CreateFromMetadata((IntPtr)blob, length)).GetReference();
                return string.IsNullOrEmpty(assembly.Location) ? null : MetadataReference.CreateFromFile(assembly.Location);
            }
            catch (Exception error) when (error is BadImageFormatException || error is IOException || error is NotSupportedException) { return null; }
        }
    }
}
