// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth.Markets;
using RimWorld;
using Silverline;
using Verse;

namespace Hearth.Ai
{
    /// <summary>
    /// Ask Ottoline (F05): the chat state and its logic. The conversation lives in memory only (not in the save); every question is answered on its own from a fresh snapshot of
    /// the books. Without AI, or when the relay fails, Ottoline answers from the books herself: five quick questions with real numbers, and typed questions are matched to
    /// them by keyword. The AI only explains. It cannot move money, open or cancel anything; the snapshot is read-only data.
    /// </summary>
    public static class AiChat
    {
        public sealed class Msg { public bool User, Ai, Offline; public string Text; }
        public static readonly List<Msg> History = new List<Msg>();
        public const int MaxMessages = 40, QuestionMax = 500;
        public static bool Pending, ScrollToEnd;
        public static string Input = "";
        static AiTicket ticket;
        public static int Asked;

        public static void Reset() { History.Clear(); Pending = false; ticket?.Cancel(); ticket = null; Input = ""; Asked = 0; ScrollToEnd = true; }

        static void Add(Msg m) { History.Add(m); if (History.Count > MaxMessages) History.RemoveRange(0, History.Count - MaxMessages); ScrollToEnd = true; }

        public static readonly string[] FaqKeys = { "net", "loans", "claim", "interest", "invest" };
        public static string FaqLabel(string key)
        {
            switch (key)
            {
                case "net": return AiT.T("Faq.Net", "What am I worth?");
                case "loans": return AiT.T("Faq.Loans", "What are my loans?");
                case "claim": return AiT.T("Faq.Claim", "Why was my claim denied?");
                case "interest": return AiT.T("Faq.Interest", "How do I earn interest?");
                default: return AiT.T("Faq.Invest", "How are my investments?");
            }
        }

        /// <summary>A quick question from the buttons: answered from the books at once, never sent anywhere.</summary>
        public static void Quick(string key)
        {
            Add(new Msg { User = true, Text = FaqLabel(key) });
            Add(new Msg { Text = Answer(key), Offline = true });
        }

        /// <summary>A typed question: AI when available, otherwise matched to a quick answer.</summary>
        public static void Ask(string question)
        {
            question = AiSchema.Clean(question, QuestionMax, true);
            if (question.Length == 0 || Pending) return;
            Asked++;
            Add(new Msg { User = true, Text = question });
            Input = "";
            var snap = Snapshot();
            // claim questions are answered from the claim records (the relay snapshot has no claims section, so a model would have to guess a reason)
            if (snap == null || Route(question) == "claim" || !AiService.Active(AiFn.Ottoline)) { Offline(question); return; }
            Pending = true;
            var fields = new Dictionary<string, object> { { "question", question }, { "snapshot", snap } };
            ticket = AiService.Send(AiFn.Ottoline, fields, res =>
            {
                Pending = false; ticket = null;
                string text = res.Ok ? AiText.Reply(res.Text, 900) : "";
                if (text.Length > 0) Add(new Msg { Text = text, Ai = true });
                else if (res.Error == "cancelled") Add(new Msg { Text = AiT.T("Chat.Stopped", "(You stopped the question.)"), Offline = true });
                else { AiService.NoteFallback(); Offline(question); }
            }, AiService.DefaultTimeout, true);
            if (ticket == null) { Pending = false; Offline(question); }
        }

        public static void Stop() { ticket?.Cancel(); }

        static void Offline(string question)
        {
            string key = Route(question);
            Add(new Msg { Text = key != null ? Answer(key) : AiT.T("Chat.NoMatch", "I can only answer a few questions from the books on my own. Use the quick questions below, or ask again when the AI is back."), Offline = true });
        }

        /// <summary>Keyword match of a typed question to a quick answer, null when nothing fits.</summary>
        public static string Route(string q)
        {
            string l = (q ?? "").ToLowerInvariant();
            bool Has(params string[] words) { foreach (var w in words) if (l.Contains(w)) return true; return false; }
            if (Has("loan", "debt", "borrow", "credit", "owe", "instal")) return "loans";
            if (Has("claim", "insur", "polic", "denied", "cover", "clawback")) return "claim";
            if (Has("interest", "saving", "earn", "yield")) return "interest";
            if (Has("invest", "stock", "share", "market", "portfolio", "coin", "bond")) return "invest";
            if (Has("worth", "balance", "money", "rich", "silver", "wealth", "afford")) return "net";
            return null;
        }

        // ------------------------------------------------------------------------------------------ the books
        public static string Answer(string key)
        {
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) return AiT.T("Chat.NoAccount", "There is no account yet. Open one in the lobby first and I will gladly look at it.");
            switch (key)
            {
                case "net": return NetAnswer(c);
                case "loans": return LoansAnswer(c);
                case "claim": return ClaimAnswer();
                case "interest": return InterestAnswer(c);
                default: return InvestAnswer();
            }
        }

        static string NetAnswer(BankGameComp c) =>
            AiT.T("Ans.Net", "Your net worth is {0} silver: {1} in the account, {2} at home in silver, minus {3} of debt. The bank sorts you into the tier \"{4}\".",
                c.NetWorth.ToStringShort(), c.Balance.ToStringShort(), c.CashAtHome.ToString("#,##0"), c.Debt.ToStringShort(),
                L.T("Tier." + c.ShownTier, c.ShownTier >= 0 && c.ShownTier < FinanceMath.TierNamesEn.Length ? FinanceMath.TierNamesEn[c.ShownTier] : "?"));

        static string LoansAnswer(BankGameComp c)
        {
            if (c.Loans.Count == 0) return AiT.T("Ans.NoLoans", "You have no loans. Debt-free is a lovely state; I will pretend not to be disappointed.");
            int now = c.Now;
            var lines = new List<string>();
            foreach (var l in c.Loans)
            {
                double rate = FinanceMath.LoanYearlyRate(BankSettings.ProfitM, l.Spread);
                int days = Math.Max(0, (l.NextDueTick - now) / 60000);
                string line = AiT.T("Ans.Loan", "Loan {0}: {1} silver still owed, instalment {2} silver in {3} day(s), about {4} % a year, {5} instalment(s) left.",
                    l.Id, l.Balance.ToStringShort(), l.Installment.ToStringShort(), days, (rate * 100).ToString("0.#"), l.InstallmentsLeft);
                if (l.InArrears) line += " " + AiT.T("Ans.Arrears", "You are {0} silver behind. Please settle that before anything else.", l.Arrears.ToStringShort());
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }

        static string ClaimAnswer()
        {
            var ins = InsuranceComponent.Instance;
            if (ins == null || ins.claims.Count == 0) return AiT.T("Ans.NoClaims", "There is no claim on file. Clawback Mutual has not had the pleasure.");
            var denied = ins.claims.Where(x => x.outcome == ClaimOutcome.Denied).OrderByDescending(x => x.filedTick).FirstOrDefault();
            if (denied == null)
            {
                var last = ins.claims.OrderByDescending(x => x.filedTick).First();
                return AiT.T("Ans.NoDenied", "None of your claims was denied. The latest one, \"{0}\", is {1}.", last.subject.NullOrEmpty() ? "?" : last.subject, last.state.ToString().ToLowerInvariant());
            }
            string why = denied.clauseNote.NullOrEmpty() ? AiT.T("Ans.NoReason", "the small print did not leave a note") : denied.clauseNote;
            return AiT.T("Ans.Denied", "Your claim for \"{0}\" was denied: {1}. You can read the clause in the insurance tab, and the cancel and change buttons live there too.", denied.subject.NullOrEmpty() ? "?" : denied.subject, why);
        }

        static string InterestAnswer(BankGameComp c) =>
            AiT.T("Ans.Interest", "Money in the account earns a small, steady interest, paid out once per quadrum. This quadrum so far: {0} silver; all time: {1} silver. Investments can earn much more, and lose it just as fast: the account is the sensible friend, the market is the exciting one.",
                c.InterestThisQuadrum.ToStringShort(), c.TotalInterest.ToStringShort());

        static string InvestAnswer()
        {
            var g = FinanceGame.Instance;
            if (g == null || g.positions.Count == 0) return AiT.T("Ans.NoInvest", "You hold no investments. The Invest tab shows what is on offer; start small and read the news ticker before you trust it.");
            var lines = new List<string> { AiT.T("Ans.Invest", "Your portfolio is worth {0} silver in {1} position(s).", g.PortfolioValue().ToStringShort(), g.positions.Count) };
            foreach (var p in g.positions.Take(5))
            {
                var a = g.Asset(p.assetId); if (a == null) continue;
                var pl = g.UnrealizedPL(p.assetId);
                lines.Add("• " + a.Label + ": " + new Money(MarketMath.ValueOf(p.units, a.price)).ToStringShort() + " silver (" + (pl.Micro >= 0 ? "+" : "-") + pl.Abs.ToStringShort() + " since you bought)");
            }
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------------------------------ snapshot for the relay
        const int ListMax = 10;

        /// <summary>The read-only picture of the books that goes to the relay (numbers and short labels; no colony name, no save name). Null without an account.</summary>
        public static Dictionary<string, object> Snapshot()
        {
            var c = BankGameComp.Current;
            if (c == null || !c.HasAccount) return null;
            var snap = new Dictionary<string, object>();
            snap["balance"] = Math.Round(c.Balance.Silver, 2);
            var fin = FinanceGame.Instance;
            snap["savings"] = Math.Round(fin != null ? fin.BondsValue().Silver : 0, 2);
            var loans = new List<object>(); int now = c.Now;
            foreach (var l in c.Loans.Take(ListMax))
            {
                double rate = FinanceMath.LoanYearlyRate(BankSettings.ProfitM, l.Spread);
                string kind = "loan" + (l.InArrears ? ", " + l.Arrears.ToStringShort() + " in arrears" : "") + ", instalment " + l.Installment.ToStringShort() + ", " + l.InstallmentsLeft + " left";
                loans.Add(new Dictionary<string, object> { { "kind", kind }, { "amount", Math.Round(l.Balance.Silver, 2) }, { "rate", Math.Round(rate * 100, 1) + " % a year" } });
            }
            snap["loans"] = loans;
            var inv = new List<object>();
            if (fin != null)
                foreach (var p in fin.positions.Take(ListMax))
                {
                    var a = fin.Asset(p.assetId); if (a == null) continue;
                    inv.Add(new Dictionary<string, object> { { "name", a.Label }, { "value", Math.Round(new Money(MarketMath.ValueOf(p.units, a.price)).Silver, 2) }, { "change_pct", Math.Round(a.Change24h * 100, 1) } });
                }
            snap["investments"] = inv;
            var pol = new List<object>();
            var ins = InsuranceComponent.Instance;
            if (ins != null)
                foreach (var k in ins.Active.Take(ListMax))
                {
                    var def = DefDatabase<SL_PolicyDef>.GetNamedSilentFail(k.defName);
                    pol.Add(new Dictionary<string, object> { { "kind", (def?.label ?? k.defName) + " (" + k.tier + ")" }, { "pawn", k.holder?.LabelShort ?? k.holderName ?? "colony" }, { "coverage", Math.Round(k.sum, 0) } });
                }
            snap["policies"] = pol;
            var recent = new List<object>();
            var led = Ledger.Current;
            if (led != null)
            {
                var journal = led.Journal;
                for (int i = journal.Count - 1; i >= 0 && recent.Count < ListMax; i--)
                {
                    var e = journal[i];
                    bool inn = e.To == BankGameComp.Colony, outt = e.From == BankGameComp.Colony;
                    if (!inn && !outt) continue;
                    recent.Add(new Dictionary<string, object> { { "memo", e.Memo ?? "" }, { "amount", Math.Round(inn ? e.Amount.Silver : -e.Amount.Silver, 2) } });
                }
            }
            snap["recent"] = recent;
            return snap;
        }
    }
}
