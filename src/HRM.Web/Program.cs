using System.Text.Json;
using System.Text.Json.Serialization;
using HRM.Web.Data;
using HRM.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<HrmDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddControllersWithViews().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

builder.Services.Configure<PayrollOptions>(builder.Configuration.GetSection("Payroll"));
builder.Services.Configure<PerformanceOptions>(builder.Configuration.GetSection("Performance"));
builder.Services.AddScoped<RecruitmentService>();
builder.Services.AddScoped<LeaveService>();
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<PayrollService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Use full type names so two types with the same short name can never break swagger.json.
    c.CustomSchemaIds(t => (t.FullName ?? t.Name).Replace('+', '.'));
    c.ResolveConflictingActions(apis => apis.First());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<HrmDbContext>();
    db.Database.EnsureCreated(); // switch to EF migrations before production (see README)
    SchemaPatches.Apply(db);
    DbSeeder.Seed(db);
}

// Turn business-rule errors into clean 400 / 404 JSON responses for the Angular client.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or DbUpdateException)
    {
        if (ctx.Response.HasStarted) throw;
        ctx.Response.Clear();
        ctx.Response.StatusCode = ex is KeyNotFoundException ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
        ctx.Response.ContentType = "application/json";
        var message = ex is DbUpdateException ? "The change conflicts with existing data (duplicate or in use)." : ex.Message;
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
    }
    catch (Exception ex) when (app.Environment.IsDevelopment() && !ctx.Response.HasStarted)
    {
        // Development only: show the real cause in the browser toast / Swagger instead of a bare "500".
        app.Logger.LogError(ex, "Unhandled error for {Path}", ctx.Request.Path);
        ctx.Response.Clear();
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";
        var detail = ex.InnerException != null ? ex.Message + " -> " + ex.InnerException.Message : ex.Message;
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message = ex.GetType().Name + ": " + detail }));
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
// Unknown API routes must not fall through to the Angular page (that returned HTML with status 200).
app.MapFallback("/api/{**path}", () => Results.NotFound(new
{
    message = "API endpoint not found. If you just updated the code, stop and restart the backend."
})).ExcludeFromDescription();
// Any other URL (Angular client routes such as /employees) is served by the MVC Home view.
app.MapFallbackToController("Index", "Home");

app.Run();
