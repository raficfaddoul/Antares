using AliantWebS.Areas.Identity;
using AliantWebS.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AliantWebS.Models;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.Json.Serialization;
using Radzen;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Localization;
using System.Globalization;
using System.Net.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AliantWebS
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddBlazorise(options =>
            {
                options.ChangeTextOnKeyPress = true; // optional
            }).AddBootstrapProviders().AddFontAwesomeIcons();

            // Add TimeProvider for .NET 8 Identity support
            services.AddSingleton(TimeProvider.System);

            // var s = Configuration.GetConnectionString("DefaultConnection");
            //        services.AddDbContextFactory<AliantWebContext>(options =>
            //            options.UseSqlServer(
            //                Configuration.GetConnectionString("DefaultConnection"), options => options.EnableRetryOnFailure()));

            //        services.AddDbContext<AliantWebContext>(options =>
            //            options.UseSqlServer(
            //                Configuration.GetConnectionString("DefaultConnection"), options => options.EnableRetryOnFailure()));
            //        services.AddDbContext<ApplicationDbContext>(options =>
            //options.UseSqlServer(
            //    Configuration.GetConnectionString("DefaultConnection")));

            /////////////////////////////////////////////////////////////////////
            var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
            var dbName = Environment.GetEnvironmentVariable("DB_NAME");
            var dbPassword = Environment.GetEnvironmentVariable("DB_SA_PASSWORD");
            var connectionStringMne = $"Data Source ={dbHost}; Initial Catalog ={dbName}; User ID =sa; Password={dbPassword};TrustServerCertificate=true";
            services.AddDbContextFactory<AliantWebContext>(opt => opt.UseSqlServer(
                    connectionStringMne, options => options.UseNetTopologySuite().EnableRetryOnFailure()));


            services.AddDbContext<AliantWebContext>(options =>
    options.UseSqlServer(connectionStringMne, options => options.UseNetTopologySuite().EnableRetryOnFailure()));
      

            //var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            var connectionString = $"Data Source ={dbHost}; Initial Catalog ={dbName}; User ID =sa; Password={dbPassword};TrustServerCertificate=true";

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(connectionString, options => options.UseNetTopologySuite().EnableRetryOnFailure()));
            services.AddDatabaseDeveloperPageExceptionFilter();
            ///////////////////////////////////////////////////////////



            services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
    .AddRoles<IdentityRole>() // This enables role-based authorization
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders()
    // Invitation links get their own provider so they can outlive the 24 h that Identity's shared
    // token lifespan gives password resets and e-mail confirmations.
    .AddTokenProvider<InvitationTokenProvider<ApplicationUser>>(InvitationTokens.ProviderName);

            services.Configure<InvitationTokenProviderOptions>(options =>
            {
                options.TokenLifespan = TimeSpan.FromDays(InvitationTokens.ExpiryDays(Configuration));
            });

            services.AddRazorPages();
            services.AddServerSideBlazor();
            services.AddScoped<AuthenticationStateProvider, RevalidatingIdentityAuthenticationStateProvider<ApplicationUser>>();
            services.AddDatabaseDeveloperPageExceptionFilter();
            services.AddSingleton<WeatherForecastService>();
            services.AddScoped<EntitiesService>();
            //services.AddScoped<FileManagementService>();
            services.AddControllers().AddJsonOptions(x =>
                        x.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve);

            services.AddScoped<DialogService>();
            services.AddScoped<NotificationService>();
            services.AddScoped<TooltipService>();
            services.AddScoped<ContextMenuService>();

            // Email sender registration
            services.AddTransient<IEmailSender<ApplicationUser>, EmailSender>();
            // Also by concrete type: the invitation e-mail is not part of IEmailSender<T>.
            services.AddTransient<EmailSender>();

            services.AddScoped<Data.AppData>();
            services.AddScoped<Data.DataUpdateService>();
            services.AddScoped<Data.JournalService>();
            // Owns every path under ScenariosData. Registered so no page has to build one by hand.
            services.AddScoped<Data.ScenarioStorageService>();
            // Produces the Word implantation study for a scenario.
            services.AddScoped<Reporting.ReportGenerator>();

            /// for antares api
            /// 
            var apiUrl = Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:8080/";
            services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(apiUrl) // Antares API running on local machine
            });
            services.AddMemoryCache();
            services.AddScoped<ReportsService>();
            services.AddHostedService<ReportBackgroundFetcher>();
            /////////////////////////////////


            services.AddBlazoredLocalStorage();
            services.AddLocalization();// opts => { opts.ResourcesPath = "/Resources"; });

            services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 50 * 1024 * 1024;
            });
            services.AddSignalR(e => {
                e.MaximumReceiveMessageSize = 1048576;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Give the sensor calibration (y = C1·x + C0) a DbContext so InfluxService transforms
            // raw values → reported values on every read (plots, gauges, charge ratios, export).
            SensorCalibration.DbFactory = app.ApplicationServices.GetRequiredService<IDbContextFactory<AliantWebContext>>();
            AppSettings.DbFactory = app.ApplicationServices.GetRequiredService<IDbContextFactory<AliantWebContext>>();

            // Journal rows written by the background monitoring loop, which has no user and no scope.
            JournalWriter.DbFactory = app.ApplicationServices.GetRequiredService<IDbContextFactory<AliantWebContext>>();

            var basePath = Environment.GetEnvironmentVariable("BasePath")
               ?? Configuration["BasePath"];
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                if (!basePath.StartsWith("/")) basePath = "/" + basePath;
                app.UsePathBase(basePath);
                // Optional: rewrite redirects/links if needed
            }

            app.UseStaticFiles();

            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".wexbim"] = "application/octet-stream";

            app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = provider });

            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "Images" )),
                RequestPath = "/Images"
            });
            //app.UseStaticFiles(new StaticFileOptions
            //{
            //    FileProvider = new PhysicalFileProvider(Path.Combine(Directory.GetCurrentDirectory(), "BIM")),
            //    RequestPath = "/BIM"
            //});

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseDeveloperExceptionPage(); //app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Begin I18N configuration
            var supportedCultures = new List<CultureInfo>
  {
    new CultureInfo("en"),
    new CultureInfo("fr")
  };

            var options = new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture("en"),
                SupportedCultures = supportedCultures,
                SupportedUICultures = supportedCultures
            };

            //options.RequestCultureProviders = new List<IRequestCultureProvider>
            //        {
            //            new QueryStringRequestCultureProvider(),
            //            new CookieRequestCultureProvider()
            //        };

            app.UseRequestLocalization(options);
            // End I18N configuration

            //app.UseHttpsRedirection();
           // app.UseStaticFiles();

            app.UseRouting();

           // app.Map("/", () => "Hello, World!");

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });
        }
    }
}

// git clone https://github.com/raficfaddoul/AliantWebS
//docker exec -it AliantDbContainer /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'password@12345#'