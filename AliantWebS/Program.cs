using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AliantWebS
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                   // webBuilder.UseUrls("http://localhost:5002");
                    webBuilder.UseStartup<Startup>();

                  //  if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                    {
                        webBuilder.UseUrls("http://+:80"); // Bind to HTTP port 80
                    }

                });
    }
    /*
Scaffold-DbContext "Data Source=CSTFI-DGC04; Initial Catalog=AliantWeb; Integrated Security = True; ConnectRetryCount=0" Microsoft.EntityFrameworkCore.SqlServer -OutputDir Models -Context AliantWebContext -DataAnnotations -Force
*/
    /*Scaffold-DbContext "Data Source=RaficLaptop; Initial Catalog=AliantWeb; Encrypt=False; Integrated Security = True; ConnectRetryCount=0" Microsoft.EntityFrameworkCore.SqlServer -OutputDir Models -Context AliantWebContext -DataAnnotations -Force*/
    //Guid.Parse("9245fe4a-d402-451c-b9ed-9c1a04247482") = -1
    //Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7") =-2

    //Server=localhost\MSSQLSERVER01;Database=master;Trusted_Connection=True;

    //influxdb operator token =  ***REMOVED***
    // all access token = ***REMOVED***
    //from(bucket: "AliantWebT") |> range(start: 0)


   // Client ID
//***REMOVED***

  //  client secret:
    //    ***REMOVED***

}
///influxDB
///username Rafic password same as my laptop
///operator ap token: ***REMOVED***
///api token :***REMOVED***
///organizationID:  ***REMOVED***
///from(bucket: "AliantWebT") |> range(start: 0)