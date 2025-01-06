using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Data.Models;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Logic;
using BTCPayServer.RockstarDev.Plugins.BitcoinStacker.ViewModels.ExchangeOrder;
using Strike.Client.Deposits;

namespace BTCPayServer.RockstarDev.Plugins.BitcoinStacker.Services;

public class ExchangeOrderHeartbeatService(
    EventAggregator eventAggregator,
    Logs logs,
    PluginDbContextFactory strikeDbContextFactory,
    StrikeClientFactory strikeClientFactory,
    StripeClientFactory stripeClientFactory) : EventHostedServiceBase(eventAggregator, logs), IPeriodicTask
{
    private Dictionary<string, DateTimeOffset> _lastRunForStore = new();
    protected override void SubscribeToEvents()
    {
        base.SubscribeToEvents();
    }

    public Task Do(CancellationToken cancellationToken)
    {
        using var db = strikeDbContextFactory.CreateContext();
        var stores = db.Settings.Where(a=>a.Key == DbSettingKeys.ExchangeOrderSettings.ToString()).ToList();
        foreach (var store in stores)
        {
            if (!_lastRunForStore.TryGetValue(store.StoreId, out var lastRun))
            {
                lastRun = DateTimeOffset.MinValue;
            }

            var setting = SettingsViewModel.FromDbSettings(store);
            if (!setting.AutoEnabled)
                continue;
            
            if (lastRun.AddMinutes(setting.MinutesHeartbeatInterval) < DateTimeOffset.UtcNow)
            {
                lastRun = DateTimeOffset.UtcNow;
                _lastRunForStore[store.StoreId] = lastRun;
                PushEvent(new PeriodProcessEvent { StoreId = store.StoreId, Setting = setting});
            }
        }
        
        return Task.CompletedTask;
    }

    private class PeriodProcessEvent
    {
        public string StoreId { get; set; }
        public SettingsViewModel Setting { get; set; }
    } 
    
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PeriodProcessEvent ppe)
        {
            await using var db = strikeDbContextFactory.CreateContext();

            var lastOrder = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                                                     && a.CreatedBy ==
                                                     DbExchangeOrder.CreateByTypes.Automatic.ToString())
                .OrderByDescending(a => a.CreatedForDate)
                .FirstOrDefault();

            var settings = ppe.Setting;

            DateTimeOffset dateToFetch = lastOrder?.CreatedForDate ?? (settings.StartDateExchangeOrders ?? DateTimeOffset.UtcNow);

            // create list of orders to execute from payouts
            var payouts = await stripeClientFactory.PayoutsSince(settings.StripeApiKey, dateToFetch);
            payouts = payouts.OrderBy(a=>a.Created).ToList();
            foreach (var payout in payouts)
            {
                var exchangeOrder = new DbExchangeOrder
                {
                    StoreId = ppe.StoreId,
                    Operation = DbExchangeOrder.Operations.BuyBitcoin,
                    Amount = payout.Amount / 100.0m * settings.PercentageOfPayouts, // Stripe uses cents
                    Created = DateTimeOffset.UtcNow,
                    CreatedBy = DbExchangeOrder.CreateByTypes.Automatic.ToString(),
                    CreatedForDate = payout.Created,
                    State = DbExchangeOrder.States.Created,
                    DelayUntil = new DateTimeOffset(2026, 01, 01, 0, 0, 0, DateTimeOffset.UtcNow.Offset)
                };
                db.ExchangeOrders.Add(exchangeOrder);
            }
            await db.SaveChangesAsync(cancellationToken);
            
            // get the list of orders in created mode and initiate deposits
            var orders = db.ExchangeOrders
                .Where(a => a.StoreId == ppe.StoreId && a.Operation == DbExchangeOrder.Operations.BuyBitcoin
                             && a.State == DbExchangeOrder.States.Created
                             && a.DelayUntil < DateTimeOffset.UtcNow)
                .OrderBy(a => a.Created)
                .ToList();
            var strikeClient = strikeClientFactory.InitClient(settings.StrikeApiKey);
            foreach (var order in orders)
            {
                var req = new DepositReq
                {
                    PaymentMethodId = settings.StrikePaymentMethodId,
                    Amount = order.Amount.ToString()
                };
                db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.CreatingDeposit, req);
                await db.SaveChangesAsync(cancellationToken);
                
                var resp = await strikeClient.Deposits.Create(req);
                if (resp.IsSuccessStatusCode)
                {
                    order.State = DbExchangeOrder.States.DepositWaiting;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.CreatingDeposit, resp, resp.Id.ToString());
                }
                else
                {
                    order.State = DbExchangeOrder.States.Error;
                    db.AddExchangeOrderLogs(order.Id, DbExchangeOrderLog.Events.Error, resp);
                }
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}