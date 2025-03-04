﻿using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data;
using BTCPayServer.RockstarDev.Plugins.Subscriptions.Data.Models;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;

namespace BTCPayServer.RockstarDev.Plugins.Subscriptions.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("~/plugins/{storeId}/subscriptions/")]
public class SubscriptionController : Controller
{
    private readonly PluginDbContext _dbContext;

    public SubscriptionController(PluginDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    
    [FromRoute] public string StoreId { get; set; }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var subscriptions = await _dbContext.Subscriptions
            .Include(s => s.Customer)
            .Include(s => s.Product)
            .Where(s => s.Customer.StoreId == StoreId)
            .ToListAsync();

        return View(subscriptions);
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        ViewBag.Customers = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId)
            .ToListAsync();

        ViewBag.Products = await _dbContext.Products
            .Where(p => p.StoreId == StoreId)
            .ToListAsync();

        return View(new Subscription());
    }

    [HttpPost("create")]
    public async Task<IActionResult> Create(Subscription subscription)
    {
        if (!ModelState.IsValid)
            return View(subscription);

        subscription.Created = DateTimeOffset.UtcNow;
        subscription.State = SubscriptionStates.Active;

        _dbContext.Subscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("edit/{id}")]
    public async Task<IActionResult> Edit(string id)
    {
        var subscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
            return NotFound();

        ViewBag.Customers = await _dbContext.Customers
            .Where(c => c.StoreId == StoreId)
            .ToListAsync();

        ViewBag.Products = await _dbContext.Products
            .Where(p => p.StoreId == StoreId)
            .ToListAsync();

        return View(subscription);
    }

    [HttpPost("edit/{id}")]
    public async Task<IActionResult> Edit(string id, Subscription subscription)
    {
        if (!ModelState.IsValid)
            return View(subscription);

        var existingSubscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (existingSubscription == null)
            return NotFound();

        existingSubscription.ProductId = subscription.ProductId;
        existingSubscription.CustomerId = subscription.CustomerId;
        existingSubscription.Expires = subscription.Expires;
        existingSubscription.State = subscription.State;
        existingSubscription.PaymentRequestId = subscription.PaymentRequestId;

        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("delete/{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var subscription = await _dbContext.Subscriptions
            .Where(s => s.Customer.StoreId == StoreId && s.Id == id)
            .FirstOrDefaultAsync();

        if (subscription == null)
            return NotFound();

        _dbContext.Subscriptions.Remove(subscription);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
