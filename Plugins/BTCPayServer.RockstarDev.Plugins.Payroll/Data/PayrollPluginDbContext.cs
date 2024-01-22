﻿using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.RockstarDev.Plugins.Payroll.Data.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.RockstarDev.Plugins.Payroll.Data;

public class PayrollPluginDbContext : DbContext
{
    private readonly bool _designTime;

    public PayrollPluginDbContext(DbContextOptions<PayrollPluginDbContext> options, bool designTime = false)
        : base(options)
    {
        _designTime = designTime;
    }

    public DbSet<PayrollInvoice> PayrollInvoices { get; set; }
    public DbSet<PayrollUser> PayrollUsers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.RockstarDev.Plugins.Payroll");

        PayrollInvoice.OnModelCreating(modelBuilder);
        PayrollUser.OnModelCreating(modelBuilder);
    }
}
