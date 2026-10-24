using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var app = builder.Build();

// Ondalık ayırıcı sorunlarını önlemek için sunucu kültürü sabit (en-US).
// Tarihler arayüzde elle "dd.MM.yyyy HH:mm" biçiminde gösterilir.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")
    .AddSupportedCultures("en-US")
    .AddSupportedUICultures("en-US"));

// Hata ayrıntıları (stack trace) kullanıcıya gösterilmez; yalnızca loglanır.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    logger.LogError(feature?.Error, "Yakalanmamış hata: {Path}", context.Request.Path);

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 500,
            Title = "Sunucu hatası",
            Detail = "İşlem tamamlanamadı. Lütfen daha sonra tekrar deneyin."
        });
    }
    else
    {
        context.Response.Redirect("/Error");
    }
}));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

app.Run();
