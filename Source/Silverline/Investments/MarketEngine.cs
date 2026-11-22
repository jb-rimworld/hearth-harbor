// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Linq;
using Hearth;
using Hearth.Markets;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Silverline
{
    public sealed partial class FinanceGame
    {
        const int Hour = 2500, Day = 60000;
        const int HourlyKeep = 168, DailyKeep = 90;
        // system A: market-wide shocks. Order matters: it is the index of the loads in AssetSpec.Loads.
        const int SysCrash = 0, SysRally = 1, SysRazzia = 2;
        static readonly double[] sysY = new double[3];

        SystemicSpec[] sysCache; string sysKey;
        SystemicSpec[] Sys()
        {
            double hz = FinanceSettings.Hazard;
            string key = hz.ToString("R");
            if (sysCache == null || key != sysKey)
            {
                sysCache = new[]
                {
                    new SystemicSpec("crash", 1.0 / 120.0 * hz, -0.25, 0.08),
                    new SystemicSpec("rally", 1.0 / 45.0 * hz, 0.05, 0.02),
                    new SystemicSpec("razzia", 1.0 / 90.0 * hz, -0.12, 0.04),
                };
                sysKey = key;
            }
            return sysCache;
        }
        public double CrashLambdaPerDay => Sys()[SysCrash].LambdaPerDay;
        public double RallyLambdaPerDay => Sys()[SysRally].LambdaPerDay;
        public double RazziaLambdaPerDay => Sys()[SysRazzia].LambdaPerDay;
        public double HackLambdaPerDay(AssetDef d) => d.hazard * FinanceSettings.Hazard;
        public double BankRunLambdaPerDay => 1.0 / 150.0 * FinanceSettings.Hazard;

        // ---- initialisation --------------------------------------------------------------------------------
        public void EnsureInit()
        {
            if (!Modules.Available(ModuleId.Bank)) return;
            if (!initialized)
            {
                initialized = true;
                SyncAssets(true);
                PrerollAll(60);
                int t = Tick;
                lastHourTick = t / Hour * Hour; lastDayIndex = DayIndex(t);
                if (FinanceSettings.MemeOn && FinanceSettings.CryptoOn) ListMemecoin(t, true);
                nextMemeTick = t + MemeGapTicks(JRand.Sealed("finance.meme"));
                HLog.Info("Bank", $"Investments initialised: {assets.Count} assets, profitability M={FinanceSettings.M:0.000}");
            }
            else BindDefs();
        }

        /// <summary>Attach AssetDefs to loaded states (defs are not saved). States whose def vanished stay but are ignored.</summary>
        public void BindDefs()
        {
            foreach (var a in assets) if (a.def == null) a.def = AssetDefs.Get(a.defName);
        }

        Faction FactionFor(AssetDef d)
        {
            if (d.factionDefName.NullOrEmpty()) return null;
            var fm = Find.FactionManager; if (fm == null) return null;
            foreach (var f in fm.AllFactionsListForReading) if (f.def.defName == d.factionDefName && !f.IsPlayer) return f;
            return null;
        }

        /// <summary>Create states for defs that have none yet (also when a mod adds assets to a running game).</summary>
        public void SyncAssets(bool initial = false)
        {
            BindDefs();
            foreach (var d in AssetDefs.All)
            {
                if (d.rotating) continue;
                if (assets.Any(a => a.defName == d.defName && !a.delisted)) continue;
                if (!d.factionDefName.NullOrEmpty() && FactionFor(d) == null) continue;
                var st = new AssetState { id = d.defName, defName = d.defName, def = d, price = d.startPrice, dayOpen = d.startPrice, listedTick = Tick };
                var r = new RngStream(Seed(), RngStream.HashString("alpha." + d.defName));
                st.alpha = d.alphaSd > 0 || d.alphaMean != 0 ? d.alphaMean + d.alphaSd * DetMath.Gaussian(r) : 0;
                st.hourly.Add((float)st.price); st.daily.Add((float)st.price);
                assets.Add(st);
                if (!initial) PrerollOne(st, 60);
            }
        }

        static ulong Seed() => HearthGame.Instance?.Rand.Seed ?? 12345UL;

        // ---- spec cache ----------------------------------------------------------------------------------
        public void RefreshSpec(AssetState st)
        {
            var d = st.def; if (d == null) return;
            double lnM = FinanceSettings.LnMValue, vol = FinanceSettings.Vol, jump = FinanceSettings.Jump, hz = FinanceSettings.Hazard;
            double dyn = st.dynHaz <= 0 ? 1 : st.dynHaz, pUp = d.hazardUpChance + st.dynPUp;
            string key = $"{lnM:R}|{vol:R}|{jump:R}|{hz:R}|{dyn:R}|{pUp:R}|{st.alpha:R}";
            if (st.spec != null && st.specKey == key) return;
            var sp = new AssetSpec
            {
                Beta = d.IsSavings ? 0 : d.beta, AnnualAdjust = d.annualAdjust, SigmaDay = d.sigmaDay * vol,
                IdioLambda = d.idioLambda * jump, IdioMean = d.idioMean, IdioSd = d.idioSd, IdioMaxY = d.idioMaxY, IdioMinY = d.idioMinY,
                EventHazard = d.hazard * hz * dyn, EventPUp = Math.Min(0.95, Math.Max(0.0, pUp)), EventUpMult = d.hazardUpMult, EventDownMult = d.hazardDownMult,
                AlphaAnnual = st.alpha,
                Loads = d.IsSavings ? new double[] { 0, 0, 0 } : new double[] { d.loadCrash >= 0 ? d.loadCrash : d.beta, d.loadRally >= 0 ? d.loadRally : d.beta, d.loadRazzia },
            };
            st.lnMa = d.IsSavings ? DetMath.Ln(MarketMath.SavingsMultiple(Math.Exp(lnM))) + DetMath.Ln(1 + d.annualAdjust) : MarketMath.LnMAsset(d.beta, lnM, d.annualAdjust);
            st.spec = sp; st.drift = MarketMath.DriftPerDay(sp, st.lnMa, Sys()); st.specKey = key;
        }

        // ---- pre-roll: the market existed before the game started ---------------------------------------------
        void PrerollAll(int days)
        {
            var ev = new RngStream(Seed(), RngStream.HashString("finance.preroll.events"));
            var rngs = new Dictionary<string, RngStream>();
            var sys = Sys(); int hours = days * 24;
            foreach (var a in assets) { RefreshSpec(a); rngs[a.id] = new RngStream(Seed(), RngStream.HashString("finance.preroll." + a.id)); }
            for (int h = 0; h < hours; h++)
            {
                for (int k = 0; k < sys.Length; k++)
                    sysY[k] = ev.NextDouble() < 1 - DetMath.Exp(-sys[k].LambdaPerDay * MarketConst.Dt) ? sys[k].Mean + sys[k].Sd * DetMath.Gaussian(ev) : 0;
                foreach (var a in assets) StepAsset(a, rngs[a.id], 0, false, 0, null);
                bool dayEnd = (h + 1) % 24 == 0;
                foreach (var a in assets)
                {
                    a.hourly.Add((float)a.price); if (a.hourly.Count > HourlyKeep) a.hourly.RemoveAt(0);
                    if (dayEnd) { a.daily.Add((float)a.price); if (a.daily.Count > DailyKeep) a.daily.RemoveAt(0); }
                }
            }
            foreach (var a in assets) a.dayOpen = a.price;
        }

        void PrerollOne(AssetState st, int days)
        {
            var ev = new RngStream(Seed(), RngStream.HashString("finance.preroll.events"));
            var rng = new RngStream(Seed(), RngStream.HashString("finance.preroll." + st.id));
            RefreshSpec(st); var sys = Sys();
            for (int h = 0; h < days * 24; h++)
            {
                for (int k = 0; k < sys.Length; k++)
                    sysY[k] = ev.NextDouble() < 1 - DetMath.Exp(-sys[k].LambdaPerDay * MarketConst.Dt) ? sys[k].Mean + sys[k].Sd * DetMath.Gaussian(ev) : 0;
                StepAsset(st, rng, 0, false, 0, null);
                st.hourly.Add((float)st.price); if (st.hourly.Count > HourlyKeep) st.hourly.RemoveAt(0);
                if ((h + 1) % 24 == 0) { st.daily.Add((float)st.price); if (st.daily.Count > DailyKeep) st.daily.RemoveAt(0); }
            }
            st.dayOpen = st.price;
        }

        // ---- one step of one asset -----------------------------------------------------------------------------
        void StepAsset(AssetState st, RngStream rng, int tick, bool live, double unused, Action<AssetState, StepInfo> onInfo)
        {
            RefreshSpec(st);
            double sig = st.signal * (st.def?.signalWeight ?? 1f);
            double signal = MarketMath.CapSignal(sig, st.spec.SigmaDay);
            st.price = MarketMath.Step(st.price, st.spec, st.drift, MarketConst.Dt, rng, sysY, signal, out var info);
            if (live) onInfo?.Invoke(st, info);
            else if (info.Event && st.def != null && st.def.kind == AssetKind.Memecoin) st.rugged = true;
        }

        // ---- the hourly tick -------------------------------------------------------------------------------------
        public void Tick_(int tick)
        {
            if (!initialized) EnsureInit();
            if (!initialized) return;
            if (tick % Hour != 0 || tick == lastHourTick) return;
            lastHourTick = tick;
            StepHourLive(tick);
            if (tick % Day == 0 && DayIndex(tick) != lastDayIndex) { lastDayIndex = DayIndex(tick); DayChange(tick); }
        }

        /// <summary>One live hour: systemic events (system A), every asset, pending sales, delisting. Public for tests (they pass their own tick).</summary>
        public void StepHourLive(int tick)
        {
            var ev = JRand.Sealed("finance.events");
            var sys = Sys();
            for (int k = 0; k < sys.Length; k++) sysY[k] = 0;
            for (int k = 0; k < sys.Length; k++)
                if (ev.NextDouble() < 1 - DetMath.Exp(-sys[k].LambdaPerDay * MarketConst.Dt))
                {
                    sysY[k] = sys[k].Mean + sys[k].Sd * DetMath.Gaussian(ev);
                    OnSystemic(k, sysY[k], tick);
                }
            if (ev.NextDouble() < 1 - DetMath.Exp(-BankRunLambdaPerDay * MarketConst.Dt)) OnBankRun(tick);

            for (int i = 0; i < assets.Count; i++)
            {
                var a = assets[i];
                if (a.delisted || a.def == null) continue;
                StepAsset(a, JRand.Sealed("finance.px." + a.id), tick, true, 0, OnAssetInfo);
                a.hourly.Add((float)a.price); if (a.hourly.Count > HourlyKeep) a.hourly.RemoveAt(0);
            }
            ExecutePendingSales(tick);
            ExpireMemecoins(tick);
            Guard.Run(ModuleId.Bank, "Projects.Hour", () => ProjectsHour(tick));
            Guard.Run(ModuleId.Bank, "Omen.Hour", () => OmenHour(tick));
        }

        // ---- events -> news --------------------------------------------------------------------------------------
        void OnSystemic(int k, double y, int tick)
        {
            var rng = JRand.Sealed("finance.news");
            if (k == SysCrash) { AddEvent(MarketEventKind.Crash, null, tick); Push(MarketEventKind.Crash, FT.Pick("HH.Finance.News.Crash", rng, FT.Pct(y, 0)), false, null, (float)y, tick, true, null); }
            else if (k == SysRally) { AddEvent(MarketEventKind.Rally, null, tick); Push(MarketEventKind.Rally, FT.Pick("HH.Finance.News.Rally", rng, FT.Pct(y, 0)), false, null, (float)y, tick, true, null); }
            else { AddEvent(MarketEventKind.Razzia, null, tick); Push(MarketEventKind.Razzia, FT.Pick("HH.Finance.News.Razzia", rng, FT.Pct(y, 0)), false, null, (float)y, tick, true, d => d.Volatile); }
        }

        void OnBankRun(int tick)
        {
            savingsLockedUntil = tick + 2 * Day;
            AddEvent(MarketEventKind.BankRun, null, tick);
            Push(MarketEventKind.BankRun, FT.Pick("HH.Finance.News.BankRun", JRand.Sealed("finance.news")), false, null, 0, tick, true, d => d.IsSavings);
        }

        void OnAssetInfo(AssetState st, StepInfo info)
        {
            var d = st.def; int tick = lastHourTick; var rng = JRand.Sealed("finance.news");
            string name = st.Label;
            if (info.Event)
            {
                switch (d.kind)
                {
                    case AssetKind.Memecoin:
                        st.rugged = true; AddEvent(MarketEventKind.RugPull, st.id, tick);
                        Push(MarketEventKind.RugPull, FT.Pick("HH.Finance.News.Rug", rng, name), false, st.id, -0.98f, tick, true, null); break;
                    case AssetKind.Crypto:
                        st.frozenUntil = tick + (int)(Math.Max(0.5f, d.freezeDaysOnHazard) * Day); AddEvent(MarketEventKind.Hack, st.id, tick);
                        Push(MarketEventKind.Hack, FT.Pick("HH.Finance.News.Hack", rng, name, ((int)Math.Max(0.5f, d.freezeDaysOnHazard)).ToString()), false, st.id, -0.03f, tick, true, null); break;
                    case AssetKind.WarBond:
                        AddEvent(MarketEventKind.WarEnd, st.id, tick);
                        Push(MarketEventKind.WarEnd, FT.Pick(info.EventUp ? "HH.Finance.News.WarWon" : "HH.Finance.News.WarLost", rng, name), false, st.id, info.EventUp ? 0.5f : -0.8f, tick, true, null); break;
                    default:
                        AddEvent(MarketEventKind.Default, st.id, tick);
                        Push(MarketEventKind.Default, FT.Pick("HH.Finance.News.Default", rng, name), false, st.id, -0.7f, tick, true, null); break;
                }
            }
            if (info.IdioY != 0)
            {
                if (d.kind == AssetKind.Memecoin && info.IdioY > 0.35) { AddEvent(MarketEventKind.Pump, st.id, tick); Push(MarketEventKind.Pump, FT.Pick("HH.Finance.News.Pump", rng, name, FT.Pct(Math.Exp(info.IdioY) - 1, 0)), false, st.id, (float)info.IdioY, tick, true, null); }
                else if (d.kind == AssetKind.FactionStock && Math.Abs(info.IdioY) > 0.04) { AddEvent(MarketEventKind.Quarterly, st.id, tick); Push(MarketEventKind.Quarterly, FT.Pick(info.IdioY > 0 ? "HH.Finance.News.Quarter.Up" : "HH.Finance.News.Quarter.Down", rng, name, d.Ticker), false, st.id, (float)info.IdioY, tick, true, null); }
                else if (d.kind == AssetKind.Crypto && Math.Abs(info.IdioY) > 0.15) Push(MarketEventKind.Pump, FT.Pick(info.IdioY > 0 ? "HH.Finance.News.Whale.Up" : "HH.Finance.News.Whale.Down", rng, name), false, st.id, (float)info.IdioY, tick, true, null);
            }
        }

        public void Push(MarketEventKind kind, string text, bool byYou, string assetId, float mag, int tick, bool message, Func<AssetDef, bool> relevant)
        {
            news.Add(new NewsItem { tick = tick, text = text, byYou = byYou, kind = kind, assetId = assetId, magnitude = mag });
            if (news.Count > 40) news.RemoveRange(0, news.Count - 40);
            HLog.Debug("Bank", "News: " + text);
            if (!message || !HSettings.GetBool("finance.newsMessages") || Find.TickManager == null) return;
            // only tell the player if it touches something they hold
            bool holds = false;
            foreach (var p in positions) { var a = Asset(p.assetId); if (a?.def == null) continue; if (assetId != null ? a.id == assetId : (relevant == null || relevant(a.def))) { holds = true; break; } }
            if (!holds) return;
            Messages.Message("Silverline: " + text, mag >= 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
        }

        // ---- the daily tick -------------------------------------------------------------------------------------
        public void DayChange(int tick)
        {
            foreach (var a in assets)
            {
                if (a.delisted) continue;
                a.daily.Add((float)a.price); if (a.daily.Count > DailyKeep) a.daily.RemoveAt(0);
                a.dayOpen = a.price;
            }
            tradeSalt = (int)((tradeCountDay * 2654435761u) & 0x7fffffff); tradeCountDay = 0;
            if (HSettings.GetBool("core.sealedFate")) { var s = JRand.Sealed("finance.events"); for (int i = 0; i < tradeSalt % 7; i++) s.NextULong(); }
            SyncAssets();
            Guard.Run(ModuleId.Bank, "Signals", () => UpdateSignals(tick));
            Guard.Run(ModuleId.Bank, "Memecoins", () => MaybeListMemecoin(tick));
            Guard.Run(ModuleId.Bank, "Bonds", () => BondsDay(tick));
            Guard.Run(ModuleId.Bank, "Year", () => RollYear(tick));
            Guard.Run(ModuleId.Bank, "Omen", () => OmenDay(tick));
            Guard.Run(ModuleId.Bank, "Projects", () => ProjectsDay(tick));
            PurgeDelisted(tick);
        }

        void RollYear(int tick)
        {
            int y = tick / (Day * 60);
            if (yearIndex != y) { yearIndex = y; yearStartNetWorth = NetWorth().Micro; yearGains = 0; }
        }

        void PurgeDelisted(int tick)
        {
            assets.RemoveAll(a => a.delisted && a.delistTick >= 0 && tick - a.delistTick > 4 * Day && UnitsOf(a.id) == 0 && !OmenReferences(a.id));
        }

        // ---- memecoin lifecycle ---------------------------------------------------------------------------------
        static int MemeGapTicks(RngStream r)
        {
            double mean = HSettings.Get("finance.memeInterval");
            double days = Math.Max(0.5, mean * (0.5 + r.NextDouble()));         // 0.5x .. 1.5x of the mean
            return (int)(days * Day);
        }

        void MaybeListMemecoin(int tick)
        {
            if (!FinanceSettings.MemeOn || !FinanceSettings.CryptoOn) return;
            if (nextMemeTick < 0) nextMemeTick = tick;
            if (tick < nextMemeTick) return;
            ListMemecoin(tick, false);
            nextMemeTick = tick + MemeGapTicks(JRand.Sealed("finance.meme"));
        }

        public AssetState ListMemecoin(int tick, bool initial)
        {
            var live = assets.Where(a => !a.delisted && a.def != null && a.def.kind == AssetKind.Memecoin).ToList();
            if (live.Count >= 3) return null;
            var pool = AssetDefs.All.Where(d => d.rotating && !live.Any(a => a.defName == d.defName)).ToList();
            if (pool.Count == 0) return null;
            var rng = JRand.Sealed("finance.meme");
            var d0 = pool[rng.Range(0, pool.Count)];
            serialCounter++;
            double mult = 0.6 + 1.2 * rng.NextDouble();
            var st = new AssetState { id = d0.defName + "#" + serialCounter, defName = d0.defName, def = d0, serial = serialCounter, price = d0.startPrice * mult, listedTick = tick, delistTick = -1 };
            st.dayOpen = st.price; st.hourly.Add((float)st.price); st.daily.Add((float)st.price);
            assets.Add(st);
            if (!initial) Push(MarketEventKind.Listing, FT.Pick("HH.Finance.News.Listing", JRand.Sealed("finance.news"), d0.LabelCap.ToString()), false, st.id, 0, tick, false, null);
            return st;
        }

        void ExpireMemecoins(int tick)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                var a = assets[i];
                if (a.delisted || a.def == null || !a.def.rotating) continue;
                if (tick - a.listedTick < (int)(a.def.lifeDays * Day)) continue;
                Delist(a, tick);
            }
        }

        /// <summary>Delisting: holders are paid out at the last price (the exchange is kind where the coin was not).</summary>
        public void Delist(AssetState a, int tick)
        {
            a.delisted = true; a.delistTick = tick;
            var pos = PositionOf(a.id);
            if (pos != null && pos.units > 0) { var m = new Money(MarketMath.ValueOf(pos.units, a.price)); FinanceAccounts.Post(FinanceAccounts.Market(a.id), FinanceAccounts.Colony, m, "Delisting of " + a.Label, true); positions.Remove(pos); Push(MarketEventKind.Delisting, FT.T("HH.Finance.News.Delisted", a.Label, FT.Silver(m)), false, a.id, 0, tick, true, null); }
            else Push(MarketEventKind.Delisting, FT.T("HH.Finance.News.Delisted.None", a.Label), false, a.id, 0, tick, false, null);
            AddEvent(MarketEventKind.Delisting, a.id, tick);
        }

        // ---- market halt by solar flare ---------------------------------------------------------------------------
        public bool MarketHalted
        {
            get
            {
                try
                {
                    var def = DefDatabase<GameConditionDef>.GetNamedSilentFail("SolarFlare"); if (def == null) return false;
                    foreach (var m in Find.Maps) if (m.IsPlayerHome && m.gameConditionManager.ConditionIsActive(def)) return true;
                }
                catch { }
                return false;
            }
        }
    }
}
