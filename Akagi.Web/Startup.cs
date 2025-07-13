using Akagi.Web.Data;
using Akagi.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
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
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddTabler();
        services.AddHttpContextAccessor();
        services.AddControllers();

        services.AddData(Configuration);
        services.AddServices();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = "Google";
        }
        ).AddCookie()
         .AddGoogle(options =>
         {
             IConfigurationSection googleAuthNSection = Configuration.GetSection("Authentication:Google");
             options.ClientId = googleAuthNSection["ClientId"]!;
             options.ClientSecret = googleAuthNSection["ClientSecret"]!;
             options.CallbackPath = "/signin-google";

             options.Events = new OAuthEvents
             {
                 OnCreatingTicket = async context =>
                 {
                     string googleId = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!;
                     string name = context.Principal!.FindFirstValue(ClaimTypes.Name)!;
                     string email = context.Principal!.FindFirstValue(ClaimTypes.Email)!;

                     IUserService userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                     Models.User user = await userService.FindOrCreateUserAsync(googleId, name, email);

                     ClaimsIdentity claimsIdentity = (ClaimsIdentity)context.Principal!.Identity!;
                     claimsIdentity.AddClaim(new Claim("internal_id", user!.Id!.ToString()));
                 }
             };
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

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
        });
    }
}
