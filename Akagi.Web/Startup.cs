using Akagi.Web.Data;
using Akagi.Web.Exporters;
using Akagi.Web.Services;
using Akagi.Web.Services.Sockets;
using Akagi.Web.Services.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Threading.RateLimiting;
using TabBlazor;

namespace Akagi.Web;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; private init; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging();
        services.AddMemoryCache();

        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddTabler();
        services.AddHttpContextAccessor();
        services.AddControllers();
        services.AddData(Configuration);
        services.AddExporters(Configuration);
        services.AddServices(Configuration);

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "Google";
        }).AddCookie(options =>
        {
            options.Events = new CookieAuthenticationEvents
            {
                OnValidatePrincipal = async context =>
                {
                    Claim? internalIdClaim = context.Principal?.FindFirst("internal_id");
                    if (internalIdClaim != null)
                    {
                        IUserDatabase userDatabase = context.HttpContext.RequestServices.GetRequiredService<IUserDatabase>();
                        string userId = internalIdClaim.Value;

                        Models.User? user = await userDatabase.GetDocumentByIdAsync(userId);

                        if (user == null)
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                            return;
                        }

                        ClaimsIdentity identity = (ClaimsIdentity)context.Principal!.Identity!;
                        if (!context.Principal.HasClaim(c => c.Type == "access_token"))
                        {
                            string? accessToken = context.Properties?.GetTokenValue("access_token");
                            if (accessToken != null)
                            {
                                identity.AddClaim(new Claim("access_token", accessToken));
                            }
                        }

                        if (!context.Principal.HasClaim(c => c.Type == "id_token"))
                        {
                            string? idToken = context.Properties?.GetTokenValue("id_token");
                            if (idToken != null)
                            {
                                identity.AddClaim(new Claim("id_token", idToken));
                            }
                        }
                    }
                }
            };
        }).AddGoogle(options =>
        {
            IConfigurationSection googleAuthNSection = Configuration.GetSection("Authentication:Google");
            options.ClientId = googleAuthNSection["ClientId"]!;
            options.ClientSecret = googleAuthNSection["ClientSecret"]!;
            options.CallbackPath = "/signin-google";

            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.SaveTokens = true;

            options.Events = new OAuthEvents
            {
                OnCreatingTicket = async context =>
                {
                    string googleId = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
                    string name = context.Principal!.FindFirstValue(ClaimTypes.Name)!;
                    string email = context.Principal!.FindFirstValue(ClaimTypes.Email)!;

                    string accessToken = context.AccessToken!;

                    string? idToken = context.Properties?.GetTokenValue("id_token");

                    IUserService userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                    Models.User user = await userService.FindOrCreateUserAsync(googleId, name, email);

                    context.Properties?.StoreTokens(
                    [
                        new AuthenticationToken { Name = "access_token", Value = accessToken },
                        new AuthenticationToken { Name = "id_token", Value = idToken ?? "" }
                    ]);

                    ClaimsIdentity claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;
                    claimsIdentity.AddClaim(new Claim("internal_id", user!.Id!.ToString()));

                    if (idToken != null)
                    {
                        claimsIdentity.AddClaim(new Claim("id_token", idToken));
                    }

                    claimsIdentity.AddClaim(new Claim("access_token", accessToken));

                    if (context.ExpiresIn.HasValue)
                    {
                        DateTime expiresAt = DateTimeOffset.UtcNow.Add(context.ExpiresIn.Value).DateTime;
                        claimsIdentity.AddClaim(new Claim("access_token_expires_at", expiresAt.ToString("o")));
                    }
                }
            };

            options.ClaimActions.MapJsonKey("id_token", "id_token");
        });

        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
                    factory: partition => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 50,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseRateLimiter();

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new SocketFileProvider(
                app.ApplicationServices.GetRequiredService<ISocketService>(),
                app.ApplicationServices.GetRequiredService<IMemoryCache>(),
                app.ApplicationServices.GetRequiredService<ILogger<SocketFileProvider>>(),
                app.ApplicationServices.GetRequiredService<IHttpContextAccessor>()
            ),
            RequestPath = "/socket",
            OnPrepareResponse = ctx =>
            {
                if (!ctx.Context.User.Identity?.IsAuthenticated == true)
                {
                    ctx.Context.Response.StatusCode = 401;
                    ctx.Context.Response.Body = Stream.Null;
                }
            }
        });

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
        });
    }
}
