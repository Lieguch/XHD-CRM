using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;



namespace XHD.Core.View
{
    public class Program
    {
        public static IHost build;
        public static void Main(string[] args)
        {
            build = CreateHostBuilder(args).Build();
            build.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    var env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", true, true);
                    // Sprint 10.36: 加载环境专属配置（appsettings.Production.json 等）
                    config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: !env.IsDevelopment(), reloadOnChange: true);
                })
                
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
