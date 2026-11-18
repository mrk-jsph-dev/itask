using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using ITask.Api.Infrastructure.Demo;
using ITask.Api.Infrastructure.Email;
using ITask.Api.Infrastructure.Storage;
using ITask.Api.Infrastructure.Turnstile;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Common;

public static class ITaskServiceExtensions
{
    private const string FrontendCors = "frontend";

    public static WebApplicationBuilder AddITaskServices(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddHttpContextAccessor();

        services.AddDatabase(config);

        services.AddAuthorizationBuilder()
            .AddPolicy(PlatformRoles.PlatformAdmin, policy => policy.RequireRole(PlatformRoles.PlatformAdmin));
        services.AddSingleton(TimeProvider.System);
        services.AddIdentityApiEndpoints<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.SignIn.RequireConfirmedEmail = config.GetValue("Auth:RequireConfirmedEmail", true);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "itask.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
        });

        // Re-check the security stamp often so a locked account's cookie stops working quickly.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddSingleton<IEmailSender<AppUser>, LoggingEmailSender>();

        services.Configure<TurnstileOptions>(config.GetSection(TurnstileOptions.Section));
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IBoardAccess, BoardAccess>();
        services.AddScoped<IActivityRecorder, ActivityRecorder>();

        services.Configure<AiOptions>(config.GetSection(AiOptions.Section));
        services.AddSingleton<IAiService, DisabledAiService>();

        services.AddBlobStorage(config);

        services.Configure<DemoOptions>(config.GetSection(DemoOptions.Section));
        services.AddScoped<DemoSeeder>();

        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
        services.AddEndpoints(typeof(Program).Assembly);
        services.AddITaskRateLimits(config);

        services.AddCors(options => options.AddPolicy(FrontendCors, policy => policy
            .WithOrigins(config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        // Azure Container Apps terminates TLS at its ingress.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    public static async Task UseITaskPipelineAsync(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseApiSecurityHeaders();
        app.UseCors(FrontendCors);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapEndpoints();

        await app.InitializeDatabaseAsync();

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync(CancellationToken.None);
    }
}
