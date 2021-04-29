using CsvHelper;
using HtmlAgilityPack;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Coingecko
{
    public class Roi
    {
        public double times { get; set; }
        public string currency { get; set; }
        public double percentage { get; set; }
    }

    public class Root
    {
        public string id { get; set; }
        public string symbol { get; set; }
        public string name { get; set; }
        public string image { get; set; }
        public string current_price { get; set; }
        public string market_cap { get; set; }
        public string market_cap_rank { get; set; }
        public string fully_diluted_valuation { get; set; }
        public string total_volume { get; set; }
        public string high_24h { get; set; }
        public string low_24h { get; set; }
        public string price_change_24h { get; set; }
        public string price_change_percentage_24h { get; set; }
        public string market_cap_change_24h { get; set; }
        public string market_cap_change_percentage_24h { get; set; }
        public string circulating_supply { get; set; }
        public string total_supply { get; set; }
        public string max_supply { get; set; }
        public string ath { get; set; }
        public string ath_change_percentage { get; set; }
        public string ath_date { get; set; }
        public string atl { get; set; }
        public string atl_change_percentage { get; set; }
        public string atl_date { get; set; }
        public string roi { get; set; }
        public string last_updated { get; set; }
    }

    public class CoinModel
    {
        public string id { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
    }
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                List<string> codes = new List<string>();

                for (int i = 1; i <= 100; i++)
                {
                    _logger.LogInformation("Extracting data from page " + i);
                    HtmlWeb web = new HtmlWeb();
                    var doc = web.Load("https://www.coingecko.com/en/coins/recently_added?page=" + i);
                    var body = doc.DocumentNode.SelectSingleNode("/html[1]/body[1]/div[3]/div[3]/div[3]/div[1]/div[1]/table[1]/tbody[1]");
                    if (body != null)
                    {
                        var nodes = body.ChildNodes.Where(x => x.Name == "tr");
                        if (nodes.Count() == 0)
                            break;
                        foreach (var row in nodes)
                        {
                            HtmlDocument sub = new HtmlDocument();
                            sub.LoadHtml(row.InnerHtml);

                            var code = sub.DocumentNode.SelectSingleNode("/td[3]/div[1]/div[2]/span[1]").InnerText.Replace("\n", "").Trim();
                            codes.Add(code);
                        }
                    }
                }

                _logger.LogInformation("Total symbols found: " + codes.Count);

                _logger.LogInformation("Getting ids against symbols");
                var client = new RestClient("https://api.coingecko.com/");
                var request = new RestRequest("api/v3/coins/list", Method.GET);
                var queryResult = client.Execute<List<CoinModel>>(request).Data;

                List<string> ids = new List<string>();
                foreach (var coin in codes)
                {
                    var entry = queryResult.FirstOrDefault(x => x.symbol.ToLower() == coin.ToLower());
                    if (entry != null)
                    {
                        ids.Add(entry.id);
                    }
                    else
                        _logger.LogError($"Symbol {coin} not found.");
                }
                _logger.LogInformation("Total ids found. "+ids.Count);
                _logger.LogInformation("Getting data against ids.");
                if (ids.Count > 0)
                {
                    List<Root> entries = new List<Root>();
                    int pages = (ids.Count + 50 - 1) / 50;
                    _logger.LogInformation("Total pages: "+pages);
                    for (int count = 1; count <= pages; ++count)
                    {
                        try
                        {
                            _logger.LogInformation("Processing page "+count);
                            int index = count - 1;
                            var part = ids.Skip(index * 50).Take(50).ToList();

                            client = new RestClient("https://api.coingecko.com/");
                            request = new RestRequest($"api/v3/coins/markets?vs_currency=usd&ids={string.Join(",", part).Replace(",", "%2C")}&order=market_cap_desc&per_page=100&page=1&sparkline=false", Method.GET);
                            var result = client.Execute<List<Root>>(request).Data;
                            if (result != null && result.Count > 0)
                                entries.AddRange(result);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Unable to get data for page {count}. Reason: {ex.Message}");
                        }
                    }

                    _logger.LogInformation("Total records found "+entries.Count);

                    using (var writer = new StreamWriter("result.csv"))
                    using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
                    {
                        csv.WriteRecords(entries);
                    }
                    _logger.LogInformation("Data has been exported successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Unable to continue. Reason: "+ex.Message);
            }

            _logger.LogInformation("Operation completed");
            Console.ReadKey();
            await Task.CompletedTask;
        }
    }
}
