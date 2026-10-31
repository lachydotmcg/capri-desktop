using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using CapriDesktop;
using Microsoft.Win32;

namespace CapriSetup
{
    internal static class Program
    {
        internal static string InstallPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"); } }
        internal const string RegistryPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Capri.Desktop";

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--update") return Update(args);
            if (args.Length == 2 && args[0] == "--verify") return Verify(args[1]);
            if (args.Length == 2 && args[0] == "--verify-voice") return VerifyVoice(args[1]);
            if (args.Length == 1 && args[0] == "--remove-files") return RemoveFiles();
            using (SetupWindow window = new SetupWindow(args.Length == 1 && args[0] == "--uninstall")) Application.Run(window);
            return 0;
        }

        [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        private static int Update(string[] args)
        {
            try
            {
                int parent;
                long started;
                if (args.Length != 3 || !int.TryParse(args[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parent) || parent <= 0 || !long.TryParse(args[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out started) || started <= 0) throw new IOException("The update command is invalid.");
                Process process = null;
                try { process = Process.GetProcessById(parent); } catch (ArgumentException) { }
                if (process != null)
                    using (process)
                    {
                        bool same = false;
                        try { same = process.StartTime.ToUniversalTime().Ticks == started; } catch (InvalidOperationException) { }
                        if (same && !process.WaitForExit(60000)) throw new IOException("Capri is still closing. Try the update again.");
                    }
                Install(false);
                using (Process launched = Process.Start(new ProcessStartInfo(Path.Combine(InstallPath, "Capri.exe")) { UseShellExecute = true, WorkingDirectory = InstallPath }))
                    if (launched == null) throw new IOException("Capri updated but couldn't restart. Open her from the Start menu.");
                return 0;
            }
            catch (Exception error) { Notice.Tell(null, error.Message, "Capri couldn't update", true); return 1; }
        }

        internal static byte[] Payload()
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.App.exe"))
            using (MemoryStream memory = new MemoryStream())
            {
                if (source == null) throw new InvalidDataException("The Capri application payload is missing.");
                source.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static int Verify(string output)
        {
            try
            {
                output = Path.GetFullPath(output);
                Directory.CreateDirectory(output);
                byte[] bytes = Payload();
                if (bytes.Length < 1024 || bytes[0] != 77 || bytes[1] != 90) throw new InvalidDataException("Application payload is not a Windows executable.");
                File.WriteAllBytes(Path.Combine(output, "Capri.exe"), bytes);
                using (SHA256 sha = SHA256.Create()) File.WriteAllText(Path.Combine(output, "payload.sha256"), BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant());
                if (!Ours(Path.Combine(output, "Capri.exe"))) throw new InvalidDataException("The installer doesn't recognise its own Capri.exe.");
                if (Ours(Application.ExecutablePath) || Ours(Path.Combine(Environment.SystemDirectory, "notepad.exe")) || Ours(Path.Combine(output, "missing.exe"))) throw new InvalidDataException("The installer takes other programs for Capri.");
                string home = Path.Combine(output, "Capri");
                if (!Inside(Path.Combine(home, "Capri.exe"), home) || !Inside(Path.Combine(home, "brain", "llama-server.exe"), home + "\\") || Inside(Path.Combine(output, "Capri2", "Capri.exe"), home) || Inside(Path.Combine(Environment.SystemDirectory, "notepad.exe"), home) || Inside(home, home)) throw new InvalidDataException("The uninstaller would close the wrong programs.");
                string link = Path.Combine(output, "Capri.lnk");
                File.WriteAllText(link, "an old shortcut");
                File.SetAttributes(link, FileAttributes.ReadOnly);
                string unsaved = Shortcut(link, Path.Combine(output, "Capri.exe"));
                if (unsaved != null || !string.Equals(LinkTarget(link), Path.Combine(output, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The installer can't save her shortcut. " + unsaved);
                File.Delete(link);
                if (Shortcut(Path.Combine(output, "missing", "Capri.lnk"), Path.Combine(output, "Capri.exe")) == null) throw new InvalidDataException("A shortcut Windows can't save counts as saved.");
                using (SetupWindow preview = new SetupWindow(false))
                {
                    preview.Show();
                    Application.DoEvents();
                    Snap(preview, Path.Combine(output, "installer.png"));
                    preview.Speaking(127L << 20, VoicePack.Size, Megabytes(127L << 20) + " of " + Megabytes(VoicePack.Size));
                    Application.DoEvents();
                    if (!preview.Saying("Installing the local speech model", "This may take some time")) throw new InvalidDataException("The installer doesn't say it's installing the local speech model.");
                    Snap(preview, Path.Combine(output, "installer-speech.png"));
                    preview.Close();
                }
                using (SetupWindow farewell = new SetupWindow(true))
                {
                    farewell.Show();
                    Application.DoEvents();
                    Snap(farewell, Path.Combine(output, "installer-remove.png"));
                    farewell.Close();
                }
                // Her notices, which stand in for Windows' grey boxes, fit their message under their title with OK below it.
                using (Notice told = new Notice("Capri has been removed.\n\nYour position and size settings were kept in " + InstallPath + ".", "Capri", false))
                using (Notice failed = new Notice("There's a Capri.exe in " + InstallPath + " that isn't this Capri. Move it out of that folder, then install again.", "Capri couldn't install", true))
                {
                    told.Show();
                    failed.Show();
                    Application.DoEvents();
                    if (!told.Fits || !failed.Fits || !told.Said.StartsWith("CAPRI\nCapri has been removed.", StringComparison.Ordinal) || !failed.Said.StartsWith("CAPRI COULDN'T INSTALL\n", StringComparison.Ordinal)) throw new InvalidDataException("A notice doesn't fit or show its message: " + told.Said + " / " + failed.Said);
                    Snap(told, Path.Combine(output, "notice.png"));
                    Snap(failed, Path.Combine(output, "notice-error.png"));
                    told.Close();
                    failed.Close();
                }
                File.WriteAllText(Path.Combine(output, "packaging-test.txt"), "PASS embedded executable extracted\nPASS PE signature\nPASS SHA-256 computed\nPASS an unmarked Capri.exe is recognised as Capri, other programs aren't\nPASS installer and uninstaller windows rendered\nPASS her notices show their title, message and OK in her look, sized to fit the message\nPASS the installer says it's installing the local speech model, that it may take some time, and that an API key gives a better, faster voice\nPASS the uninstaller closes Capri and anything running from her folder, like her brain, and nothing else\nPASS her shortcut is saved through Windows' own shell link, even over an old read-only one, and points at Capri.exe\nPASS a shortcut Windows won't save comes back as its reason instead of stopping the install\nLIMIT installation and uninstallation were not performed\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); return 1; }
        }

        // A shortcut Windows won't save, often because of an antivirus, doesn't stop the install. She's installed either way,
        // so its reason comes back for the installer to show. Null means every shortcut was saved.
        internal static string Install(bool desktop)
        {
            string destination = InstallPath;
            string marker = Path.Combine(destination, ".capri-install");
            string existing = Path.Combine(destination, "Capri.exe");
            if (Directory.Exists(destination) && !File.Exists(marker) && File.Exists(existing) && !Ours(existing)) throw new IOException("There's a Capri.exe in " + destination + " that isn't this Capri. Move it out of that folder, then install again.");
            // Her input helper lives a moment after she closes, so a copy on its way out gets a few seconds.
            foreach (Process process in Process.GetProcessesByName("Capri"))
            {
                using (process)
                {
                    try { if (string.Equals(process.MainModule.FileName, Path.Combine(destination, "Capri.exe"), StringComparison.OrdinalIgnoreCase) && !process.WaitForExit(3000)) throw new IOException("Quit Capri from her tray menu, then install again."); }
                    catch (System.ComponentModel.Win32Exception) { }
                }
            }
            Directory.CreateDirectory(destination);
            string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            File.WriteAllText(marker, "Capri desktop companion " + version);
            string target = Path.Combine(destination, "Capri.exe");
            string temporary = target + ".new";
            try
            {
                File.WriteAllBytes(temporary, Payload());
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            File.Copy(Application.ExecutablePath, Path.Combine(destination, "Uninstall.exe"), true);
            string unsaved = Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs, Environment.SpecialFolderOption.Create), "Capri.lnk"), target);
            string onDesktop = desktop ? Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.Create), "Capri.lnk"), target) : null;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", "Capri");
                key.SetValue("DisplayVersion", version);
                key.SetValue("Publisher", "Capri fan project");
                key.SetValue("InstallLocation", destination);
                key.SetValue("DisplayIcon", Path.Combine(destination, "Capri.exe"));
                key.SetValue("UninstallString", "\"" + Path.Combine(destination, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (Payload().Length + new FileInfo(Application.ExecutablePath).Length) / 1024, RegistryValueKind.DWord);
            }
            return unsaved ?? onDesktop;
        }

        // A window drawn on a plain backdrop and cut to its own shape, the way the screen shows it.
        private static void Snap(Form form, string path)
        {
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            using (Bitmap sheet = new Bitmap(form.Width + 48, form.Height + 48))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(Color.FromArgb(44, 47, 56));
                    if (form.Region != null)
                        using (Region shape = form.Region.Clone())
                        {
                            shape.Translate(24, 24);
                            graphics.SetClip(shape, CombineMode.Replace);
                        }
                    graphics.DrawImageUnscaled(bitmap, 24, 24);
                }
                sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        // Fetches the real voice pack into a test folder, the same way an install does.
        private static int VerifyVoice(string data)
        {
            try
            {
                data = Path.GetFullPath(data);
                Directory.CreateDirectory(data);
                FetchVoice(data, delegate { }, delegate { return false; });
                if (!VoicePack.Complete(VoicePack.Folder(data))) throw new IOException("The local speech model is missing files after installing.");
                File.WriteAllText(Path.Combine(data, "voice-test.txt"), "PASS the local speech model downloaded, matched its SHA-256 and unpacked where Capri looks for it\n");
                return 0;
            }
            catch (Exception error) { Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "failure.txt"), error.ToString()); return 1; }
        }

        internal static string Megabytes(long bytes) { return (bytes >> 20) + " MB"; }

        // Her local speech model goes where Capri's own download puts it, under the same file names, so either can finish
        // a download the other started.
        internal static void FetchVoice(string data, Action<string, long, long> progress, Func<bool> stopped)
        {
            string folder = VoicePack.Folder(data);
            if (VoicePack.Complete(folder)) return;
            string root = Path.GetDirectoryName(folder);
            Directory.CreateDirectory(root);
            long needed = VoicePack.Size * 3 + (64L << 20);
            string drive = Path.GetPathRoot(root);
            if (new DriveInfo(drive).AvailableFreeSpace < needed) throw new IOException("The local speech model needs " + Megabytes(needed) + " free on " + drive.TrimEnd('\\') + ".");
            string zip = folder + ".zip";
            string part = zip + ".part";
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > VoicePack.Size) { File.Delete(part); have = 0; }
            int stalls = 0;
            while (have < VoicePack.Size)
            {
                long before = have;
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(VoicePack.Url);
                    request.UserAgent = "Capri-Setup";
                    request.Timeout = 30000;
                    request.ReadWriteTimeout = 60000;
                    if (have > 0) request.AddRange(have);
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (have > 0 && response.StatusCode != HttpStatusCode.PartialContent) have = 0;
                        using (Stream body = response.GetResponseStream())
                        using (FileStream file = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            byte[] buffer = new byte[1 << 16];
                            long shown = 0;
                            int read;
                            while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (stopped()) throw new OperationCanceledException();
                                file.Write(buffer, 0, read);
                                have += read;
                                if (have - shown >= (1L << 20)) { shown = have; progress("Downloading", have, VoicePack.Size); }
                            }
                        }
                    }
                }
                catch (WebException) { if (stopped()) throw new OperationCanceledException(); }
                catch (IOException) { if (stopped()) throw new OperationCanceledException(); }
                if (have < VoicePack.Size)
                {
                    stalls = have > before ? 0 : stalls + 1;
                    if (stalls >= 4) throw new IOException("The download keeps stopping. Check your connection.");
                    Thread.Sleep(1500 * stalls);
                }
            }
            progress("Checking", VoicePack.Size, VoicePack.Size);
            string sum;
            using (SHA256 sha = SHA256.Create())
            using (FileStream file = File.OpenRead(part)) sum = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            if (have != VoicePack.Size || sum != VoicePack.Sha) { File.Delete(part); throw new IOException("The download came down damaged. Try again from Capri Mainframe."); }
            if (File.Exists(zip)) File.Delete(zip);
            File.Move(part, zip);
            progress("Unpacking", VoicePack.Size, VoicePack.Size);
            string staging = folder + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zip, staging);
            if (!VoicePack.Complete(staging)) throw new IOException("The download is missing files.");
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.Move(staging, folder);
            File.Delete(zip);
        }

        internal static bool Ours(string path)
        {
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return info.ProductName == "Capri" && string.Equals(info.OriginalFilename, "Capri.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch (FileNotFoundException) { return false; }
        }

        // Her shortcuts go through Windows' own shell link instead of Windows Script Host, and an old read-only one is
        // replaced. When Windows still won't save one, its reason comes back instead of an error.
        private static string Shortcut(string path, string target)
        {
            try
            {
                if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                IShellLinkW link = (IShellLinkW)new ShellLink();
                try
                {
                    link.SetPath(target);
                    link.SetWorkingDirectory(Path.GetDirectoryName(target));
                    link.SetDescription("Capri desktop companion");
                    link.SetIconLocation(target, 0);
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(path, true);
                }
                finally { Marshal.FinalReleaseComObject(link); }
                return null;
            }
            catch (Exception error) { return error.Message; }
        }

        private static string LinkTarget(string path)
        {
            IShellLinkW link = (IShellLinkW)new ShellLink();
            try
            {
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
                System.Text.StringBuilder target = new System.Text.StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                return target.ToString();
            }
            finally { Marshal.FinalReleaseComObject(link); }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int size, IntPtr found, int flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder folder, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string folder);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relative, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        internal static void BeginUninstall()
        {
            string temporary = Path.Combine(Path.GetTempPath(), "Capri-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, temporary);
            Process.Start(new ProcessStartInfo(temporary, "--remove-files") { UseShellExecute = true });
        }

        private static int RemoveFiles()
        {
            try
            {
                string destination = Path.GetFullPath(InstallPath);
                string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri"));
                if (!string.Equals(destination, expected, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(destination, ".capri-install"))) throw new IOException("The Capri install marker could not be verified.");
                CloseEverything(destination);
                string uninstaller = Path.Combine(destination, "Uninstall.exe");
                for (int i = 0; i < 25; i++)
                {
                    try { File.Delete(uninstaller); break; }
                    catch (IOException) { System.Threading.Thread.Sleep(100); }
                }
                if (File.Exists(uninstaller)) throw new IOException("The uninstaller is still open. Close it and try removing Capri again.");
                Patiently(delegate { File.Delete(Path.Combine(destination, "Capri.exe")); });
                File.Delete(Path.Combine(destination, ".capri-install"));
                foreach (string pack in new[] { "brain", "voice", "ears" })
                {
                    string folder = Path.Combine(destination, pack);
                    Patiently(delegate { if (Directory.Exists(folder)) Directory.Delete(folder, true); });
                }
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Capri.lnk"), destination);
                DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Capri.lnk"), destination);
                Startup.Clear(Path.Combine(destination, "Capri.exe"));
                Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
                Notice.Tell(null, "Capri has been removed.\n\nYour position and size settings were kept in " + destination + ".", "Capri", false);
                return 0;
            }
            catch (Exception error) { Notice.Tell(null, error.Message, "Capri could not be removed", true); return 1; }
        }

        // Capri and anything running from her folder, like her brain's engine, hold its files, so they're closed first.
        // Nothing outside the folder is touched.
        internal static void CloseEverything(string folder)
        {
            int self;
            using (Process me = Process.GetCurrentProcess()) self = me.Id;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == self || !Inside(process.MainModule.FileName, folder)) continue;
                        process.Kill();
                        process.WaitForExit(5000);
                    }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    catch (NotSupportedException) { }
                }
            }
        }

        internal static bool Inside(string file, string folder)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder)) return false;
            return Path.GetFullPath(file).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // A file can stay locked for a moment after the program holding it closes.
        private static void Patiently(Action remove)
        {
            for (int tries = 1; ; tries++)
            {
                try { remove(); return; }
                catch (IOException) { if (tries == 10) throw; }
                catch (UnauthorizedAccessException) { if (tries == 10) throw; }
                System.Threading.Thread.Sleep(300);
            }
        }

        // A shortcut that can't be read or removed stays behind instead of stopping the rest of the uninstall.
        private static void DeleteShortcut(string path, string destination)
        {
            try { if (File.Exists(path) && string.Equals(LinkTarget(path), Path.Combine(destination, "Capri.exe"), StringComparison.OrdinalIgnoreCase)) File.Delete(path); }
            catch (Exception) { }
        }
    }

    // Her installer in her Mainframe's look: a borderless window cut at the corners with a red rim and hot red strokes over her
    // wine glow, INSTALL CAPRI in rose for its title, its options on a band, cyan tick boxes, and a solid cyan Install Capri
    // (a red Remove Capri). It drags from anywhere that isn't a control and lays itself out for the screen's scale.
    internal sealed class SetupWindow : Form
    {
        private readonly bool removing;
        private readonly float s;
        private readonly PictureBox chip;
        private readonly Plate primary;
        private readonly Tick desktop;
        private readonly Tick launch;
        private readonly Label section;
        private readonly Label status;
        private readonly Label stage;
        private readonly Label note;
        private readonly Plate cancel;
        private long got, total;
        private string noShortcut;
        private bool fetching;
        private volatile bool stopping;

        internal SetupWindow(bool uninstall)
        {
            removing = uninstall;
            Text = removing ? "Remove Capri" : "Install Capri";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            s = DeviceDpi / 96f;
            ClientSize = new Size(Px(680), Px(470));
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            Font = Neon.Type(10.5f);
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            LabelAt(removing ? "REMOVE CAPRI" : "INSTALL CAPRI", 24, 14, 420, 32, Neon.Display(15f), Neon.Rose);
            Glyph(1, 680 - 86, delegate { WindowState = FormWindowState.Minimized; });
            Glyph(2, 680 - 54, delegate { Close(); });
            LabelAt(removing ? "See you around." : "Unofficial\nDesktop Mate!", 32, 64, 420, 124, Neon.Display(30f), Neon.White);
            chip = new PictureBox();
            chip.Bounds = Box(462, 74, 180, 112);
            chip.SizeMode = PictureBoxSizeMode.Zoom;
            chip.BackColor = Color.Transparent;
            using (Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.png")) if (image != null) using (Bitmap loaded = new Bitmap(image)) chip.Image = new Bitmap(loaded);
            Controls.Add(chip);
            LabelAt(removing ? "Remove the app and its shortcuts for this Windows user.\nQuit Capri from the tray before continuing." : "Right-click for her menu.", 34, 198, 610, 50, Neon.Type(11f), Neon.Faint);
            section = LabelAt("OPTIONS", 34, 254, 400, 20, Neon.Type(10f, true), Neon.White);
            section.Visible = !removing;
            desktop = new Tick();
            desktop.Text = "Add a desktop shortcut";
            desktop.Bounds = Box(34, 284, 340, 26);
            desktop.Visible = !removing;
            launch = new Tick();
            launch.Text = "Open Capri when finished";
            launch.Checked = true;
            launch.Bounds = Box(34, 314, 340, 26);
            launch.Visible = !removing;
            Controls.Add(desktop);
            Controls.Add(launch);
            status = LabelAt(removing ? "Your saved position and size will be kept." : "", 34, 352, 604, 22, Neon.Type(9.5f), Neon.Faint);
            stage = LabelAt("", 34, 282, 604, 28, Neon.Type(13f, true), Neon.White);
            stage.Visible = false;
            note = LabelAt("", 34, 314, 604, 22, Neon.Type(10f), Neon.Faint);
            note.Visible = false;
            primary = new Plate();
            primary.Text = removing ? "Remove Capri" : "Install Capri";
            primary.Accent = removing ? Neon.Hot : Neon.Cyan;
            primary.Solid = true;
            primary.Font = Neon.Type(11.5f, true);
            primary.Bounds = Box(450, 400, 194, 42);
            primary.Click += Perform;
            Controls.Add(primary);
            cancel = new Plate();
            cancel.Text = "Cancel";
            cancel.Font = Neon.Type(11f, true);
            cancel.Bounds = Box(318, 400, 116, 42);
            cancel.Click += delegate
            {
                if (!fetching) { Close(); return; }
                // Skipping the speech model still leaves Capri installed; she can fetch it later from her Mainframe.
                stopping = true;
                cancel.Enabled = false;
                status.Text = "Skipping it…";
            };
            Controls.Add(cancel);
            AcceptButton = primary;
            CancelButton = cancel;
            Neon.Clip(this, 16 * s, 6 * s);
        }

        // Without a title bar it keeps a taskbar button that minimizes and restores it, and Alt + Space's menu.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.Style = (value.Style | 0x00020000 | 0x00080000) & ~0x00010000;
                return value;
            }
        }

        private int Px(float value) { return (int)Math.Round(value * s); }

        private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Px(x), Px(y), Px(width), Px(height)); }

        private Label LabelAt(string text, int x, int y, int width, int height, Font font, Color color)
        {
            Label label = new Neon.Caption();
            label.Text = text;
            label.Bounds = Box(x, y, width, height);
            label.Font = font;
            label.ForeColor = color;
            Controls.Add(label);
            return label;
        }

        // Minimize and close, top right like the Mainframe's. They're for the mouse, so Enter still means the main button.
        private void Glyph(int glyph, int x, EventHandler click)
        {
            Plate mark = new Plate();
            mark.Glyph = glyph;
            mark.TabStop = false;
            mark.AccessibleName = glyph == 1 ? "Minimize" : "Close";
            mark.Bounds = Box(x, 14, 30, 26);
            mark.Click += click;
            Controls.Add(mark);
        }

        // It drags from anywhere that isn't a control.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * s); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            if (section.Visible)
            {
                // Its options sit on a dark band under a teal rule with a red tick, like a Mainframe section.
                RectangleF band = new RectangleF(section.Left - 14 * s, section.Top - 5 * s, ClientSize.Width - section.Left - 6 * s, 24 * s);
                using (Brush fill = new SolidBrush(Neon.Band)) graphics.FillRectangle(fill, band);
                using (Brush rule = new SolidBrush(Neon.Rule)) graphics.FillRectangle(rule, band.Left, band.Top, band.Width, Math.Max(1, (int)s));
                using (Brush tick = new SolidBrush(Neon.Hot)) graphics.FillRectangle(tick, band.Left, band.Top + 7 * s, 2.5f * s, band.Height - 13 * s);
            }
            using (Brush line = new SolidBrush(Neon.Edge)) graphics.FillRectangle(line, Px(34), Px(384), ClientSize.Width - Px(68), Math.Max(1, (int)s));
            Neon.Frame(graphics, ClientSize, s, 16 * s, 6 * s, 64 * s, 34 * s);
            if (!fetching || total <= 0) return;
            // The download's progress: a dark well filling with cyan segments.
            int tall = Px(7);
            Rectangle bar = new Rectangle(status.Left, status.Top - tall - Px(8), status.Width, tall);
            using (Brush track = new SolidBrush(Neon.Well)) graphics.FillRectangle(track, bar);
            int filled = (int)(bar.Width * Math.Min(1.0, (double)got / total));
            using (Brush fill = new SolidBrush(Neon.Cyan)) graphics.FillRectangle(fill, bar.X, bar.Y, filled, bar.Height);
            using (Brush gap = new SolidBrush(Neon.Well))
                for (int x = bar.X + Px(10); x < bar.X + filled; x += Px(10)) graphics.FillRectangle(gap, x, bar.Y, Math.Max(1, Px(2)), bar.Height);
            using (Pen rim = new Pen(Neon.WellEdge, Math.Max(1, (int)s))) graphics.DrawRectangle(rim, bar.X, bar.Y, bar.Width - 1, bar.Height - 1);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            stopping = true;
            base.OnFormClosing(e);
        }

        private void Perform(object sender, EventArgs e)
        {
            primary.Enabled = false;
            try
            {
                if (removing) { Program.BeginUninstall(); Close(); return; }
                status.Text = "Making a little room for Capri…";
                noShortcut = Program.Install(desktop.Checked);
            }
            catch (Exception error) { status.Text = "Capri could not finish. Your files are still here."; primary.Enabled = true; Notice.Tell(this, error.Message, "Capri couldn't install", true); return; }
            if (Environment.Is64BitOperatingSystem && !VoicePack.Complete(VoicePack.Folder(Program.InstallPath))) FetchVoice();
            else Finish();
        }

        // Without her shortcut she's still installed, so they're told why and shown Capri.exe to pin.
        private void Finish()
        {
            string app = Path.Combine(Program.InstallPath, "Capri.exe");
            if (noShortcut != null)
            {
                Notice.Tell(this, "Capri is installed, but Windows wouldn't let the installer save her shortcut:\n" + noShortcut + "\n\nShe's in " + Program.InstallPath + ". Right-click Capri.exe there and pick Pin to Start.", "Capri", false);
                try { using (Process.Start("explorer.exe", "/select,\"" + app + "\"")) { } }
                catch (System.ComponentModel.Win32Exception) { }
            }
            if (launch.Checked) Process.Start(new ProcessStartInfo(app) { UseShellExecute = true });
            Close();
        }

        // Her local speech model comes with the install, and the window says so plainly while it downloads.
        private void FetchVoice()
        {
            Speaking(0, VoicePack.Size, "Starting the download…");
            Thread worker = new Thread(delegate()
            {
                string failed = null;
                try
                {
                    Program.FetchVoice(Program.InstallPath, delegate(string what, long have, long size)
                    {
                        string detail = what == "Downloading" ? Program.Megabytes(have) + " of " + Program.Megabytes(size) : what == "Checking" ? "Checking the download…" : "Unpacking…";
                        Post(delegate { Speaking(have, size, detail); });
                    }, delegate { return stopping; });
                }
                catch (OperationCanceledException) { failed = ""; }
                catch (Exception error) { failed = error.Message; }
                Post(delegate { Fetched(failed); });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Post(Action action)
        {
            try { if (!IsDisposed) BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        internal void Speaking(long have, long size, string detail)
        {
            fetching = true;
            desktop.Visible = false;
            launch.Visible = false;
            section.Text = "SPEECH MODEL";
            stage.Text = "Installing the local speech model";
            note.Text = "This may take some time. For a better sounding, faster voice, get an API key.";
            stage.Visible = true;
            note.Visible = true;
            if (!stopping) cancel.Text = "Skip";
            primary.Text = "Installing…";
            primary.Enabled = false;
            got = have;
            total = size;
            if (!stopping) status.Text = detail;
            Invalidate();
        }

        internal bool Saying(string heading, string warning) { return stage.Visible && note.Visible && stage.Text == heading && note.Text.StartsWith(warning, StringComparison.Ordinal) && note.Text.IndexOf("API key", StringComparison.Ordinal) >= 0; }

        private void Fetched(string failed)
        {
            fetching = false;
            if (string.IsNullOrEmpty(failed)) { Finish(); return; }
            stage.Text = "Couldn't install the local speech model";
            note.Text = "Capri is installed. Get it later in Capri Mainframe, or add an API key.";
            status.Text = failed;
            cancel.Visible = false;
            primary.Text = "Finish";
            primary.Click -= Perform;
            primary.Click += delegate { Finish(); };
            primary.Enabled = true;
            Invalidate();
        }

        protected override void Dispose(bool disposing) { if (disposing && chip.Image != null) chip.Image.Dispose(); base.Dispose(disposing); }

        // A tick box in her look: a small dark box with a red rim that fills cyan with a dark tick when it's on, its words in
        // white beside it. The rim lights up under the mouse or with keyboard focus.
        private sealed class Tick : CheckBox
        {
            private bool hot;

            internal Tick()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                Cursor = Cursors.Hand;
                UseMnemonic = false;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = DeviceDpi / 96f;
                Graphics graphics = e.Graphics;
                Neon.Behind(graphics, this, 210 * s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float side = 16 * s;
                RectangleF box = new RectangleF(1.5f * s, (Height - side) / 2f, side, side);
                bool lit = hot || (Focused && ShowFocusCues);
                using (GraphicsPath plate = Neon.Cut(box, 0, 0, 4 * s, 0))
                {
                    using (Brush fill = new SolidBrush(Checked ? Neon.Cyan : Neon.Well)) graphics.FillPath(fill, plate);
                    using (Pen rim = new Pen(Checked ? (lit ? Color.FromArgb(190, 252, 254) : Neon.Cyan) : lit ? Neon.Hot : Neon.WellEdge, Math.Max(1f, 1.2f * s))) graphics.DrawPath(rim, plate);
                }
                if (Checked)
                    using (Pen tick = new Pen(Neon.OnInk, 1.8f * s))
                    {
                        tick.LineJoin = LineJoin.Miter;
                        graphics.DrawLines(tick, new[] { new PointF(box.Left + side * 0.22f, box.Top + side * 0.52f), new PointF(box.Left + side * 0.42f, box.Top + side * 0.72f), new PointF(box.Left + side * 0.78f, box.Top + side * 0.3f) });
                    }
                int left = (int)(box.Right + 10 * s);
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Neon.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
