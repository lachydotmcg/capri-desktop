using System;
using System.Collections.Generic;
using System.Threading;

namespace CapriDesktop
{
    // One voice request owns exactly one dispatch path. AsyncLocal preserves that
    // path on delayed callbacks without making non-voice requests share its state.
    internal sealed class VoiceActionRecovery
    {
        private sealed record Dispatch(VoiceActionRecovery Owner, long Ticket, bool Recovery);
        private static readonly AsyncLocal<Dispatch> context = new AsyncLocal<Dispatch>();
        private sealed class Scope : IDisposable
        {
            private readonly Dispatch previous;
            private bool disposed;
            internal Scope(Dispatch value) { previous = context.Value; context.Value = value; }
            public void Dispose() { if (!disposed) { disposed = true; context.Value = previous; } }
        }
        private sealed class Authority
        {
            private readonly Brain brain;
            private readonly IBrainProvider provider;
            private readonly int generation;
            private readonly string identity, model, remoteModel;
            private readonly Cloud remote;
            internal Authority(Brain brain)
            {
                this.brain = brain; provider = brain.Provider; generation = provider?.Generation ?? 0;
                identity = provider?.Identity; model = brain.Model; remote = brain.Remote; remoteModel = remote?.Model;
            }
            internal bool Current()
            {
                try
                {
                    return ReferenceEquals(provider, brain.Provider) && generation == (provider?.Generation ?? 0) && identity == provider?.Identity &&
                        model == brain.Model && ReferenceEquals(remote, brain.Remote) && remoteModel == remote?.Model;
                }
                catch (Exception error) when (error is BrainException || error is System.IO.IOException || error is UnauthorizedAccessException) { return false; }
            }
        }
        private readonly object gate = new object();
        private long generation;
        private Authority authority;
        private bool recovering, claimed, ended;
        private int primaryTools, recoveryTools;

        internal long Begin()
        {
            lock (gate) { generation++; authority = null; recovering = claimed = ended = false; primaryTools = recoveryTools = 0; return generation; }
        }
        internal void Cancel() { lock (gate) { generation++; ended = true; } }
        internal void End(long ticket) { lock (gate) if (ticket == generation) ended = true; }
        internal bool Current(long ticket)
        {
            Authority captured;
            lock (gate) { if (ticket != generation || ended) return false; captured = authority; }
            if (captured != null && !captured.Current()) return false;
            lock (gate) return ticket == generation && !ended;
        }
        internal IDisposable EnterPrimary(long ticket, Brain brain = null)
        {
            if (!Current(ticket)) throw new OperationCanceledException("The voice request ended before primary execution.");
            Authority captured = brain == null ? null : new Authority(brain);
            lock (gate)
            {
                if (ticket != generation || ended) throw new OperationCanceledException("The voice request ended before primary execution.");
                if (captured != null) authority = captured;
            }
            return new Scope(new Dispatch(this, ticket, false));
        }
        internal IDisposable EnterRecovery(long ticket) => new Scope(new Dispatch(this, ticket, true));
        internal int Tools(long ticket) { lock (gate) return ticket == generation ? primaryTools + recoveryTools : 0; }

        // Call immediately before the real Act executor, not on a model proposal.
        internal static bool AllowCurrentTool()
        {
            Dispatch owner = context.Value;
            return owner == null || owner.Owner.Allow(owner.Ticket, owner.Recovery);
        }
        internal static Func<bool> DispatchGuard
        {
            get
            {
                Dispatch owner = context.Value;
                return owner == null ? null : () => owner.Owner.PathCurrent(owner.Ticket, owner.Recovery);
            }
        }
        private bool PathCurrent(long ticket, bool recovery)
        {
            if (!Current(ticket)) return false;
            lock (gate) return ticket == generation && !ended && recovery == recovering;
        }
        private bool Allow(long ticket, bool recovery)
        {
            if (!Current(ticket)) return false;
            lock (gate)
            {
                if (ticket != generation || ended || recovery != recovering) return false;
                if (recovery) recoveryTools++; else primaryTools++;
                return true;
            }
        }
        internal bool TryClaimRecovery(long ticket, string heard, string said)
        {
            if (string.IsNullOrWhiteSpace(heard) || !Hands.Wants(heard) || !Hands.Promised(said) || Hands.Holds(said) || Security.IsHold(said)) return false;
            if (!Current(ticket)) return false;
            lock (gate)
            {
                if (ticket != generation || ended || claimed || primaryTools != 0) return false;
                claimed = recovering = true;
                return true;
            }
        }

        // The synchronous Brain already performs normal Reach/Rescue. A second
        // look is only for a returned promise which dispatched no actual tool.
        internal string Recover(Brain brain, long ticket, string heard, string said)
        {
            if (!TryClaimRecovery(ticket, heard, said)) return said;
            using IDisposable scope = EnterRecovery(ticket);
            if (!Current(ticket)) throw new OperationCanceledException("The voice authority changed before its second look.");
            Hands.Kit kit = Hands.KitFor(brain.Blind);
            var calls = new List<KeyValuePair<string, ToolArguments>>();
            brain.QuickTools(Hands.Keeping, Hands.KeepAsk(heard, said), 300, calls, kit);
            if (!Current(ticket)) throw new OperationCanceledException("The voice request ended during its second look.");
            if (Hands.Rescue(heard, calls, kit)) return kit.Unable("use_computer", null);
            if (calls.Count == 0) return Hands.KeptNothing;
            if (calls.Count > 8) return "No voice recovery action ran: the proposed tool batch exceeded its bounded limit.";
            var results = new List<string>();
            foreach (var call in calls)
            {
                if (!Current(ticket)) throw new OperationCanceledException("The voice request ended before recovery execution.");
                var args = call.Value ?? new ToolArguments();
                args.Asked = heard;
                Func<bool> previous = args.Current;
                args.Current = () => Current(ticket) && (previous == null || previous());
                string result = brain.Act?.Invoke(call.Key, args) ?? "No tool executor is attached.";
                results.Add(result);
                if (Security.IsHold(result) || Hands.Holds(result)) break;
            }
            if (!Current(ticket)) throw new OperationCanceledException("The voice request ended after recovery execution.");
            return Tools(ticket) == 0 ? Hands.KeptNothing : string.Join("\n", results);
        }
    }
}
