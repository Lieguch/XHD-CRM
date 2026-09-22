

using FreeSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using XHD.Core.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using XHD.Core.Common;
using XHD.Core.Common.SMS;
using XHD.Core.Common.CDKEY;
using XHD.Core.Common.Mail;
using XHD.Core.Common.RSA;
using XHD.Core.Common.Cache;
using XHD.Core.View.Configs;
using Microsoft.Extensions.Logging;

namespace XHD.Core.View
{
    public class Startup
    {
        public IConfiguration Configuration { get; }
        private readonly IHostEnvironment _env;
        private readonly ConfigHelper _configHelper;

        public Startup(IConfiguration configuration, IWebHostEnvironment env)
        {
            Configuration = configuration;
            _env = env;
            _configHelper = new ConfigHelper();
        }


        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddSingleton<Ihr_employeeService, hr_employeeService>();
            //services.AddSingleton<Ihr_employeeRepository, hr_employeeRepository>();
            //services.AddUEditorService();
            services.AddControllersWithViews();

            services.AddService();
            services.AddRepository();

            // Sprint 7 新增：ISMSHelper → SMSHelper（HttpClient 调用外部短信服务商）
            services.AddHttpClient<SMSHelper>(client => { });
            services.AddScoped<ISMSHelper, SMSHelper>();

            // Sprint 9 新增：5 个 Common 工具类（#144 ECBC_CDKEY / #145 Verify / #147 MailSender / #148 RSACryption / #158 DataCache）
            services.AddScoped<ICDKEYHelper, CDKEYHelper>();
            services.AddScoped<IRSACryptionHelper, RSACryptionHelper>();
            services.AddMemoryCache();
            services.AddScoped<IDataCacheHelper, DataCacheHelper>();
            services.AddScoped<IMailHelper>(sp =>
            {
                var cfg = Configuration.GetSection("Mail");
                var logger = sp.GetRequiredService<ILogger<MailHelper>>();
                return new MailHelper(
                    host: cfg["Host"] ?? "localhost",
                    port: int.TryParse(cfg["Port"], out int p) ? p : 587,
                    useTls: bool.TryParse(cfg["UseTls"], out bool tls) ? tls : true,
                    fromAddress: cfg["FromAddress"] ?? string.Empty,
                    username: cfg["Username"] ?? string.Empty,
                    password: cfg["Password"] ?? string.Empty,
                    logger: logger);
            });

            services.AddSession();

            // Sprint 10.10 (2026-09-22): 持久化 DataProtection 密钥到 /app/Data 命名卷
            // 根因：ASP.NET Core 默认使用 EphemeralXmlRepository（进程内内存），
            // 容器重启 → 密钥丢失 → 旧 session cookie 无法解密
            // → CryptographicException: The key {guid} was not found in the key ring
            // → HttpContext.Session 里存的 AES_Key 丢失 → 登录时 AesDecrypt(pwd, null) 失败
            // 修复：把 DataProtection 密钥写到 /app/Data（已在 xhd-crm-data 命名卷 + chown 1001:1001）
            // 同时保护：session、cookie auth、 antiforgery、.NET encryption token 全都共享这一套密钥环
            services.AddDataProtection()
                .PersistKeysToFileSystem(new System.IO.DirectoryInfo("/app/Data"));

            services.AddDb(_env);
            // Sprint 0 修复 (2026-09-18): 补 AddHsts() 注册
            // 原代码只调 app.UseHsts() 但未 services.AddHsts()，导致 HstsOptions 未注册，
            // 容器部署时 HTTPS 失败。参考 https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl
            services.AddHsts(options =>
            {
                options.IncludeSubDomains = true;
                options.MaxAge = System.TimeSpan.FromDays(90);
                options.Preload = true;
            });
            services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.LoginPath = "/Account/Index";
                    options.LogoutPath = "/Account/SignOut";
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(120);

                });


            #if DEBUG
                 services.AddRazorPages().AddRazorRuntimeCompilation();
            #endif

            //跨域
            services.AddCors(options => options.AddPolicy("CorsPolicy",
               builder =>
               {
                   builder.AllowAnyMethod()
                       .AllowAnyHeader()
                       //.SetIsOriginAllowed(origin => origin.StartsWith("http://192.168.*.*"));
                       .AllowAnyOrigin();  //测试环境才用这个
                       //.WithOrigins("https://sfs.huilongtech.com");

               }));

            
        }

        //public void ConfigureContainer(ContainerBuilder builder)
        //{
        //    builder.RegisterModule(new Configs.RepositoryModule());
        //    builder.RegisterModule(new Configs.ServiceModule());
        //}

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,IHostApplicationLifetime lifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler(configure =>
                {
                    configure.Run(async context =>
                    {
                        var exHeader = context.Features.Get<IExceptionHandlerPathFeature>();
                        var ex = exHeader.Error;
                        if (ex != default)
                        {
                            await context.Response.WriteAsJsonAsync(new { code = 500, errPath = exHeader.Path, msg = $"服务器内部错误->{ex.Message}" });
                        }

                        //NLogger.WriteLog("sys_Error1_", ex.Message);
                        NLogger.WriteLog("sys_Error_", ex.ToString());

                    });
                });
                //app.UseExceptionHandler("/Home/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            

            var provider = new FileExtensionContentTypeProvider();
            provider.Mappings[".less"] = "text/css";

            app.UseSession();
            // Sprint 0 修复 (2026-09-18): UseHttpsRedirection 改条件判断
            // 原代码无条件调用，容器无 HTTPS 绑定时全被 307 弹走。
            // 通过配置项控制，appsettings.Production.json 可设置 HttpsRedirection:Enabled=false
            var httpsRedirEnabled = Configuration.GetValue<bool>("HttpsRedirection:Enabled", true);
            if (httpsRedirEnabled)
            {
                app.UseHttpsRedirection();
            }

            var filePath = AppContext.BaseDirectory;

            //app.UseStaticFiles();

            app.UseStaticFiles(new StaticFileOptions
            {
                ContentTypeProvider = provider
            });

            app.UseCors("CorsPolicy");

            app.UseRouting();

           
            app.UseCookiePolicy();
            app.UseAuthentication();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");
            });
        }

    }
}
