﻿using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class PayrollUserController : Controller
{
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly PayrollPluginDbContextFactory _payrollPluginDbContextFactory;
    private readonly PayrollPluginPassHasher _hasher;

    public PayrollUserController(ApplicationDbContextFactory dbContextFactory,
        PayrollPluginDbContextFactory payrollPluginDbContextFactory,
        StoreRepository storeRepository,
        PayrollPluginPassHasher hasher)
    {
        _dbContextFactory = dbContextFactory;
        _payrollPluginDbContextFactory = payrollPluginDbContextFactory;
        _hasher = hasher;
    }
    public StoreData CurrentStore => HttpContext.GetStoreData();


    [HttpGet("~/plugins/{storeId}/payroll/users")]
    public async Task<IActionResult> List(string storeId)
    {
        var now = DateTimeOffset.UtcNow;
        await using var ctx = _payrollPluginDbContextFactory.CreateContext();
        var payrollUsers = await ctx.PayrollUsers
            .Where(a => a.StoreId == storeId)
            .OrderByDescending(data => data.Name).ToListAsync();

        return View(payrollUsers.ToList());
    }

    [HttpGet("~/plugins/{storeId}/payroll/users/create")]
    public async Task<IActionResult> Create()
    {
        return View(new PayrollUserCreateViewModel());
    }

    [HttpPost("~/plugins/{storeId}/payroll/users/create")]

    public async Task<IActionResult> Create(PayrollUserCreateViewModel model)
    {
        if (CurrentStore is null)
            return NotFound();

        await using var dbPlugins = _payrollPluginDbContextFactory.CreateContext();

        var userInDb = dbPlugins.PayrollUsers.SingleOrDefault(a =>
            a.StoreId == CurrentStore.Id && a.Email == model.Email.ToLowerInvariant());
        if (userInDb != null)
            ModelState.AddModelError(nameof(model.Email), "User with the same email already exists");

        if (!ModelState.IsValid)
            return View(model);

        var uid = Guid.NewGuid().ToString();

        var passHashed = _hasher.HashPassword(uid, model.Password);

        var dbUser = new PayrollUser
        {
            Id = uid,
            Name = model.Name,
            Email = model.Email.ToLowerInvariant(),
            Password = passHashed,
            StoreId = CurrentStore.Id
        };

        dbPlugins.Add(dbUser);
        await dbPlugins.SaveChangesAsync();

        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Message = $"The user {dbUser.Name} cannot be deleted due to existing invoices associated with the account.",
            Severity = StatusMessageModel.StatusSeverity.Error
        });
        return RedirectToAction(nameof(List), new { storeId = CurrentStore.Id });
    }

    public class PayrollUserCreateViewModel
    {
        public string Id { get; set; }
        [MaxLength(50)]
        [Required]

        public string Name { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }
        [Required]
        [MinLength(6)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "Password fields don't match")]
        public string ConfirmPassword { get; set; }

    }
}