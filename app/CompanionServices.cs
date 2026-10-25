using System;
using System.Threading;
using System.Threading.Tasks;
namespace CapriDesktop.Services
{
    // Future adapters must be invoked by an explicit UI action. No services start here.
    public interface ILanguageModel { Task<string> ReplyAsync(string prompt, CancellationToken cancellation); }
    public interface ISpeechRecognition { Task<string> ListenAsync(CancellationToken cancellation); }
    public interface ISpeechSynthesis { Task SpeakAsync(string text, CancellationToken cancellation); }
    public interface IConversationMemory
    {
        Task<string[]> ReadAsync(CancellationToken cancellation);
        Task AppendAsync(string role, string text, CancellationToken cancellation);
        Task ClearAsync(CancellationToken cancellation);
    }
    public enum DesktopCapability { ReadActiveWindow, CaptureScreen, MoveWindow }
    public interface IPermissionPrompt { Task<bool> RequestAsync(DesktopCapability capability, string reason, CancellationToken cancellation); }
    // A permission is requested for each operation; denial is the default without a prompt.
    public sealed class DesktopPermissionGate
    {
        private readonly IPermissionPrompt prompt;
        public DesktopPermissionGate(IPermissionPrompt prompt) { this.prompt = prompt; }
        public async Task<bool> RequestAsync(DesktopCapability capability, string reason, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return prompt != null && await prompt.RequestAsync(capability, reason, cancellation); }
    }
    public interface IDesktopAwareness { Task<string> DescribeAsync(CancellationToken cancellation); }
    public interface IDesktopInteraction { Task MoveWindowAsync(IntPtr window, int x, int y, CancellationToken cancellation); }
}
