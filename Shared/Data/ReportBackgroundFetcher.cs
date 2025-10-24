using AliantWebS.Data;
using AliantWebS.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class ReportBackgroundFetcher : BackgroundService
{
    private readonly ReportsService _reportsService;
    private readonly ILogger<ReportBackgroundFetcher> _logger;
    private readonly IServiceScopeFactory _scopeFactory;


    public ReportBackgroundFetcher(IServiceScopeFactory scopeFactory, ILogger<ReportBackgroundFetcher> logger)
    {     
        _logger = logger;
        _scopeFactory = scopeFactory;
      
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting background fetcher");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                

                 await ReportsService.StartContinuousFetch(
            onNewReports: newReports =>
            {
                // We must switch to the Blazor UI thread (InvokeAsync) if we want to modify
                // the component’s state from a background thread
          
                  //  reportsList.AddRange(newReports);
                   // StateHasChanged();
                
            },
            cancellationToken: stoppingToken, _scopeFactory,
            pollingIntervalSeconds: 10
        );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching reports");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
        _logger.LogInformation("Background fetcher stopping");
    }
}
