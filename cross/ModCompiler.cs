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

        // The parts of the mod API only Windows has, by what a mod writes to reach them, and what to call them in her line.
        private static readonly string[,] WindowsOnlyParts =
        {
            { @"\bSystem\.Windows\.Forms\b", "Windows Forms" },
            { @"\b(?:Bitmap|Graphics|Brushes?|Pens?|Font|Image|SolidBrush)\b", "System.Drawing pictures" },
            { @"\.Art\s*\(", "Art" },
            { @"\bICapriIntro\b", "Startup Intros" },
            { @"\bCapriFrame\b|\.Drawing\s*[+-]=", "Ui.Drawing" },
            { @"\.SetOptions\s*\(", "Ui.SetOptions" },
            { @"\.Window\b", "Ui.Window and Mainframe.Window" },
            { @"\.ShowPage\s*\(", "Mainframe.ShowPage" },
            { @"\.Looked\b|\.Capture\s*\(", "Screen.Looked and Screen.Capture" }
        };

        // A mod that reaches for a part only Windows has is refused before she asks to run it, naming those parts.
        partial void CheckPlatform(string text)
        {
            string code = Regex.Replace(text ?? "", @"//[^\n]*|/\*.*?\*/|@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'", " ", RegexOptions.Singleline);
            List<string> found = new List<string>();
            for (int i = 0; i < WindowsOnlyParts.GetLength(0); i++)
                if (Regex.IsMatch(code, WindowsOnlyParts[i, 0]) && !found.Contains(WindowsOnlyParts[i, 1])) found.Add(WindowsOnlyParts[i, 1]);
            if (found.Count > 0) throw new ModException("That mod uses parts of Capri only Windows has (" + string.Join(", ", found) + "), so it can't run on " + (OperatingSystem.IsMacOS() ? "a Mac" : "Linux") + ".");
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
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text ?? "", new CSharpParseOptions(LanguageVersion.CSharp5));
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
