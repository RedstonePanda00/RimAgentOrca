using System;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RimAgent.Core;
using RimWorld;
using Verse;

namespace RimAgent;

internal sealed partial class OrcaAgentGameAdapter
{
    private sealed class TradeObservation { public string Id = Guid.NewGuid().ToString("N"); }
    private readonly ConditionalWeakTable<TradeDeal, TradeObservation> tradeObservations = new();
    private static Dialog_Trade TradeWindow(JsonObject a)
    {
        var window = Find.WindowStack.WindowOfType<Dialog_Trade>();
        if (window == null || !TradeSession.Active || TradeSession.playerNegotiator?.Map != Map(a.Number("mapId",0,int.MaxValue)))
            throw new BridgeException("no_trade", "Use the negotiator's native pawn_orders on a trader first; wait for its trade window.");
        if (TradeSession.giftMode) throw new BridgeException("unsupported_trade", "Gift mode is not a priced trade. Close or switch it in the game.");
        return window;
    }
    private static string TradeRevision() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",TradeSession.deal.AllTradeables.Select(t =>
        $"{t.ThingDef?.defName}:{t.CountHeldBy(Transactor.Colony)}:{t.CountHeldBy(Transactor.Trader)}:{t.CountToTransfer}:{t.GetPriceFor(TradeAction.PlayerBuys).ToString("R",CultureInfo.InvariantCulture)}:{t.GetPriceFor(TradeAction.PlayerSells).ToString("R",CultureInfo.InvariantCulture)}")))));
    private object TradeRead(JsonObject a)
    {
        TradeWindow(a); var deal = TradeSession.deal; deal.UpdateCurrencyCount();
        var observation = tradeObservations.GetValue(deal,_ => new TradeObservation());
        return new { tradeId = observation.Id, revision = TradeRevision(), trader = TradeSession.trader.TraderName,
            negotiatorId = TradeSession.playerNegotiator.GetUniqueLoadID(), currency = TradeSession.TradeCurrency.ToString(),
            colonyCanAfford = deal.CurrencyTradeable?.CountPostDealFor(Transactor.Colony) >= 0, traderCanAfford = deal.DoesTraderHaveEnoughSilver(),
            items = deal.AllTradeables.Select((t,i) => new { index = i, defName = t.ThingDef?.defName, stuff = t.StuffDef?.defName, label = t.Label,
                currency = t.IsCurrency, interactive = t.Interactive, colonyCount = t.CountHeldBy(Transactor.Colony), traderCount = t.CountHeldBy(Transactor.Trader),
                count = t.CountToTransfer, minimum = t.GetMinimumToTransfer(), maximum = t.GetMaximumToTransfer(),
                buyPrice = t.GetPriceFor(TradeAction.PlayerBuys), sellPrice = t.GetPriceFor(TradeAction.PlayerSells) }).ToArray() };
    }
    private object TradeAct(string tool, JsonObject a)
    {
        var window = TradeWindow(a); var deal = TradeSession.deal; var id = a.Text("tradeId",64);
        if (!tradeObservations.TryGetValue(deal,out var observed) || observed.Id != id) throw new BridgeException("trade_stale", "Read the current trade again.");
        if (tool == "rimagent_trade_cancel") { window.Close(); return new { cancelled = true }; }
        if (Find.WindowStack.Windows.Any(w => w != window && w.forcePause)) throw new BridgeException("simulation_blocked", "Resolve the overlaid game dialog before trading.");
        deal.UpdateCurrencyCount();
        if (a.Text("revision",128) != TradeRevision()) throw new BridgeException("trade_stale", "Counts or prices changed; read trade_get again.");
        if (tool == "rimagent_trade_set")
        {
            int index = a.Number("index",0,deal.AllTradeables.Count - 1); var item = deal.AllTradeables[index];
            if (item.IsCurrency || !item.Interactive) throw new BridgeException("trade_item_disabled", "This row cannot be adjusted directly.");
            int count = a.Number("count",item.GetMinimumToTransfer(),item.GetMaximumToTransfer());
            var report = item.CanAdjustTo(count);
            if (!report.Accepted) throw new BridgeException("trade_rejected", Rejection(report,"Native trade count rejected."));
            item.AdjustTo(count); deal.UpdateCurrencyCount(); return TradeRead(a);
        }
        if (!TradeSession.trader.CanTradeNow) throw new BridgeException("trade_unavailable", "Trader is no longer available.");
        if (!deal.DoesTraderHaveEnoughSilver()) throw new BridgeException("trader_cannot_afford", "Reduce sales; the adapter does not silently waive unpaid silver.");
        bool accepted = deal.TryExecute(out bool traded);
        if (accepted) window.Close();
        return new { accepted, traded, completion = accepted ? "Native trade resolved; query items independently." : "Native trade rejected or requires an additional game decision." };
    }
}
