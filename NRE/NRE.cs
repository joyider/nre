//#reference: NRE\LumenWorks.Framework.IO.dll
//#reference: C:\Windows\Microsoft.Net\assembly\GAC_64\System.Data\v4.0_4.0.0.0__b77a5c561934e089\System.Data.dll

//Csv Reader (C) http://www.codeproject.com/Articles/9258/A-Fast-CSV-Reader

//---------------------------------------------------------------------------
//News Robot Enhanced (NRE)
//
//André Karlsson   <andre@sess.se>
//Version 0.8a
//Will place news trades based on data downloaded from dailyFX.com
//
//* Place orders based on High/Meedium/Low news importance
//* Show historical news events onscreen
//* One Cancle Other or One DON'T Cancel Other
//* Trailing stop as an option (places SL at half of takeprofit when reached)
//---------------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using cAlgo.API;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;
using LumenWorks.Framework.IO.Csv;
using HorizontalAlignment = cAlgo.API.HorizontalAlignment;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.WEuropeStandardTime, AccessRights = AccessRights.FullAccess)]
    public class NRE : Robot, ILogger
    {
        private SymbolWrapper _symbol;
        private List<NewsItem> _newsItems;
        private Dictionary<DateTime, NewsGroup<NewsItem>> _groups;
        private bool first_run = true;
        private NewsItem NI;
        public bool SymbolFilter = true;
        public int Position = (int)StaticPosition.TopLeft;

        private bool isTrigerred;

        [Parameter(DefaultValue = "1337")]
        public string Label { get; set; }

        [Parameter(DefaultValue = false)]
        public bool ShowLow { get; set; }

        [Parameter(DefaultValue = false)]
        public bool ShowMedium { get; set; }

        [Parameter(DefaultValue = true)]
        public bool ShowHigh { get; set; }

        [Parameter(DefaultValue = 10)]
        public int EventsToDisplay { get; set; }

        [Parameter(DefaultValue = true)]
        public bool ShowPastNews { get; set; }

        [Parameter(DefaultValue = 3)]
        public int PastNewsLookback { get; set; }

        // When the stop order becomes a market order (pips away from news time)
        [Parameter("Pips away", DefaultValue = 10)]
        public int PipsAway { get; set; }

        [Parameter(DefaultValue = false)]
        public bool TrailingStop { get; set; }

        [Parameter("Take Profit", DefaultValue = 20)]
        public int TakeProfit { get; set; }

        [Parameter("Stop Loss", DefaultValue = 10)]
        public int StopLoss { get; set; }

        [Parameter("Volume", DefaultValue = 100000, MinValue = 1000)]
        public int Volume { get; set; }

        //Place stop order X seconds before news release (10 seconds default)
        [Parameter("Seconds Before", DefaultValue = 10, MinValue = 1)]
        public int SecondsBefore { get; set; }

        //When the stop order is to be considered as expired (10 seconds default)
        [Parameter("Seconds Timeout", DefaultValue = 10, MinValue = 1)]
        public int SecondsTimeout { get; set; }

        [Parameter("One Cancels Other")]
        public bool Oco { get; set; }

        [Parameter("ShowTimeLeftNews", DefaultValue = false)]
        public bool ShowTimeLeftToNews { get; set; }

        [Parameter("ShowTimeLeftPlaceOrders", DefaultValue = true)]
        public bool ShowTimeLeftToPlaceOrders { get; set; }

        private bool _ordersCreated;

        private DateTime _triggerTimeInServerTimeZone;

        //private const string Label = "News Robot";

        protected override void OnStart()
        {
            try
            {
                Log("Initialising");

                Log("TimeZone Setting: {0}", TimeZone);
                Log("TimeZone Name: {0}", TimeZone.DisplayName);
                Log("Offset: {0}", TimeZone.BaseUtcOffset);
                Log("DST: {0}", TimeZone.SupportsDaylightSavingTime);

                var downloader = new DailyFxDownloader(this);
                var allNewsItems = downloader.Download(PastNewsLookback);

                Log(string.Format("{0} events loaded", allNewsItems.Count));

                _symbol = new SymbolWrapper(Symbol.Code);

                _newsItems = NewsRepository.FilterNews(allNewsItems, ShowLow, ShowMedium, ShowHigh, SymbolFilter, _symbol);

                List<NewsGroup<NewsItem>> groupList = NewsRepository.GroupNews(_newsItems, _symbol);
                _groups = groupList.ToDictionary(x => x.Time);

                Log("{0} groups created", _groups.Count);
            } catch (Exception e)
            {
                Log("Exception: {0}", e.Message);
                _newsItems = new List<NewsItem>();
            }

            //On a new position check One Cancle Other
            Positions.Opened += OnPositionOpened;
            Timer.Start(1);

            //We need to catch the excpetion if the list is empty for some reason
            try
            {
                NI = DisplayUpcomingNews();
                _triggerTimeInServerTimeZone = NI.UtcDateTime.Add(TimeZone.BaseUtcOffset);
            } catch (Exception e)
            {
                Log("Exception: {0}", e.Message);
            }

        }

        protected override void OnTimer()
        {
            int index = MarketSeries.Close.Count - 1;

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

            try
            {
                NI = DisplayUpcomingNews();
            } catch (Exception e)
            {
                Log("Exception: {0}", e.Message);
            }

            //If we have an order we can che
            try
            {
                var remainingTime = _triggerTimeInServerTimeZone - Server.Time;
                DrawRemainingTime(remainingTime);
            } catch (Exception e)
            {
                Log("Exception: {0}", e.Message);
            }

            if (ShowPastNews)
            {
                //If first loop, we need to check all histarical bars for news
                if (first_run)
                {
                    for (int idx = 0; idx < index; idx++)
                    {
                        DisplayPastNews(idx);
                    }
                    first_run = false;
                }
                //Otherwise just the latest
                else
                {
                    DisplayPastNews(index);
                }

            }

            //If we have a market order and it is active then leave. This is really Ugly....
            if (_ordersCreated && (Positions.Count > 0))
            {
                if (TrailingStop)
                {
                    updateSL();
                }
                return;
            }

            try
            {
                _triggerTimeInServerTimeZone = NI.UtcDateTime.Add(TimeZone.BaseUtcOffset);
            } catch (Exception e)
            {
                Log("Exception: {0}", e.Message);
            }

            if (!_ordersCreated)
            {
                //ExecuteMarketOrder(TradeType.Sell, Symbol, 1000, Label, null, 1);
                //ExecuteMarketOrder(TradeType.Buy, Symbol, 1000, Label, null, 1);
                //_ordersCreated = true;
                var sellOrderTargetPrice = Symbol.Bid - PipsAway * Symbol.PipSize;
                ChartObjects.DrawHorizontalLine("sell target", sellOrderTargetPrice, Colors.Red, 1, LineStyle.DotsVeryRare);
                var buyOrderTargetPrice = Symbol.Ask + PipsAway * Symbol.PipSize;
                ChartObjects.DrawHorizontalLine("buy target", buyOrderTargetPrice, Colors.Blue, 1, LineStyle.DotsVeryRare);

                if (Server.Time <= _triggerTimeInServerTimeZone && (_triggerTimeInServerTimeZone - Server.Time).TotalSeconds <= SecondsBefore)
                {
                    _ordersCreated = true;
                    var expirationTime = _triggerTimeInServerTimeZone.AddSeconds(SecondsTimeout);

                    if (TrailingStop)
                    {
                        PlaceStopOrder(TradeType.Sell, Symbol, Volume, sellOrderTargetPrice, Label, StopLoss, null, expirationTime);
                        PlaceStopOrder(TradeType.Buy, Symbol, Volume, buyOrderTargetPrice, Label, StopLoss, null, expirationTime);
                    }
                    else
                    {
                        PlaceStopOrder(TradeType.Sell, Symbol, Volume, sellOrderTargetPrice, Label, StopLoss, TakeProfit, expirationTime);
                        PlaceStopOrder(TradeType.Buy, Symbol, Volume, buyOrderTargetPrice, Label, StopLoss, TakeProfit, expirationTime);
                    }

                    ChartObjects.RemoveObject("sell target");
                    ChartObjects.RemoveObject("buy target");
                }
            }

            //If we have no pending ordes or positions open we need to setup next newstrade (switch _ordersCreated to false)
            if (_ordersCreated && !PendingOrders.Any(o => o.Label == Label) && (Positions.Count == 0))
            {
                Log("Resuming Order operations");
                _ordersCreated = false;
            }
        }

        private void updateSL()
        {
            int Trigger = 2;
            int TrailingStop = TakeProfit / Trigger;

            foreach (var position in Positions)
            {
                if (position.Label == Label)
                {
                    if (position.TradeType == TradeType.Buy)
                    {
                        double distance = Symbol.Bid - position.EntryPrice;

                        if (distance >= TakeProfit * Symbol.PipSize)
                        {
                            if (!isTrigerred)
                            {
                                isTrigerred = true;
                                Log("BUY Trailing Stop Loss triggered...");
                            }

                            double newStopLossPrice = Math.Round(Symbol.Bid - TrailingStop * Symbol.PipSize, Symbol.Digits);

                            if (position.StopLoss == null || newStopLossPrice > position.StopLoss)
                            {
                                ModifyPosition(position, newStopLossPrice, position.TakeProfit);
                                Print("Position: " + position.Id + " | SL : " + newStopLossPrice + " | Distance : " + (position.EntryPrice - newStopLossPrice) / Symbol.PipSize + " | Last : " + position.StopLoss);
                                isTrigerred = false;
                            }
                        }
                    }
                    else
                    {
                        double distance = position.EntryPrice - Symbol.Ask;

                        if (distance >= TakeProfit * Symbol.PipSize)
                        {
                            if (!isTrigerred)
                            {
                                isTrigerred = true;
                                Log("SELL Trailing Stop Loss triggered...");

                            }

                            double newStopLossPrice = Math.Round(Symbol.Ask + TrailingStop * Symbol.PipSize, Symbol.Digits);

                            if (position.StopLoss == null || newStopLossPrice < position.StopLoss)
                            {
                                ModifyPosition(position, newStopLossPrice, position.TakeProfit);
                                Print("Position: " + position.Id + " | SL : " + newStopLossPrice + " | Distance : " + (newStopLossPrice - position.EntryPrice) / Symbol.PipSize + " | Last : " + position.StopLoss);
                                isTrigerred = false;
                            }
                        }
                    }

                }
            }
        }

        private void DrawRemainingTime(TimeSpan remainingTimeToNews)
        {
            if (ShowTimeLeftToNews)
            {
                if (remainingTimeToNews > TimeSpan.Zero)
                {
                    ChartObjects.DrawText("countdown1", "Time left to news: " + FormatTime(remainingTimeToNews), StaticPosition.Right);
                }
                else
                {
                    ChartObjects.RemoveObject("countdown1");
                }
            }
            if (ShowTimeLeftToPlaceOrders)
            {
                var remainingTimeToOrders = remainingTimeToNews - TimeSpan.FromSeconds(SecondsBefore);
                if (remainingTimeToOrders > TimeSpan.Zero)
                {
                    ChartObjects.DrawText("countdown2", "Time left to place orders: " + FormatTime(remainingTimeToOrders), StaticPosition.TopRight);
                }
                else
                {
                    ChartObjects.RemoveObject("countdown2");
                }
            }
        }

        private static StringBuilder FormatTime(TimeSpan remainingTime)
        {
            var remainingTimeStr = new StringBuilder();
            if (remainingTime.TotalHours >= 1)
                remainingTimeStr.Append((int)remainingTime.TotalHours + "h ");
            if (remainingTime.TotalMinutes >= 1)
                remainingTimeStr.Append(remainingTime.Minutes + "m ");
            if (remainingTime.TotalSeconds > 0)
                remainingTimeStr.Append(remainingTime.Seconds + "s");
            return remainingTimeStr;
        }

        private void OnPositionOpened(PositionOpenedEventArgs args)
        {
            var position = args.Position;
            if (position.Label == Label && position.SymbolCode == Symbol.Code)
            {
                if (Oco)
                {
                    foreach (var order in PendingOrders)
                    {
                        if (order.Label == Label && order.SymbolCode == Symbol.Code)
                        {
                            CancelPendingOrderAsync(order);
                        }
                    }
                }
            }
        }

        private NewsItem DisplayUpcomingNews()
        {
            var upcomingNews = _newsItems.Where(x => x.UtcDateTime >= DateTime.UtcNow).Take(EventsToDisplay).ToList();

            var utcOffset = TimeZone.BaseUtcOffset;

            //remove old objects
            for (int i = 0; i < EventsToDisplay; i++)
            {
                ChartObjects.RemoveObject("NewsItem" + i.ToString());
            }

            int objectId = 0;
            string verticalTab = "";
            foreach (NewsItem newsItem in upcomingNews)
            {
                Colors color;
                switch (newsItem.Importance)
                {
                    case Importance.High:
                        color = Colors.Red;
                        break;

                    case Importance.Low:
                        color = Colors.Yellow;
                        break;

                    case Importance.Medium:
                        color = Colors.Orange;
                        break;
                    default:
                        color = Colors.Black;
                        break;
                }

                var dateStr = (newsItem.UtcDateTime.Add(utcOffset)).ToString("ddd HH:mm");
                string news = string.Format("{0} - {1} - {2}", newsItem.Currency, dateStr, newsItem.Event);

                ChartObjects.DrawText("NewsItem" + objectId++.ToString(), verticalTab + "  " + news, (StaticPosition)Position, color);
                verticalTab += Environment.NewLine;
            }
            //return the next upcomming event.
            //TODO: Don't trust DailFX, verify that this is the next upcoming order
            return upcomingNews.First();
        }

        private void DisplayPastNews(int index)
        {
            DateTime openTime = MarketSeries.OpenTime[index];

            //find all news released between candle open and close time
            List<DateTime> dateTimes = _groups.Keys.Where(x => x >= openTime && x <= openTime.AddMinutes(TimeFrameInMinutes)).ToList();

            foreach (var dateTime in dateTimes)
            {
                var newsGroup = _groups[dateTime];

                var timeId = dateTime.ToString("yyyyMMddHHmmss");

                var high = MarketSeries.High[index];
                var low = MarketSeries.Low[index];

                var pips = Symbol.PipSize;

                const int padding = 20;

                const Colors textColor = Colors.Green;

                //base currency news are displayed above current price
                if (newsGroup.BaseCurrencyNews.NewsList.Any())
                {
                    ChartObjects.DrawVerticalLine("Pointer1" + timeId, openTime, Colors.SlateGray, 1, LineStyle.DotsVeryRare);
                    ChartObjects.DrawText("News1" + timeId, FormatForDisplay(newsGroup.BaseCurrencyNews.NewsList), index, high + padding * pips, VerticalAlignment.Top, HorizontalAlignment.Right, textColor);
                }

                //quote currency news are displayed below current price
                if (newsGroup.QuoteCurrencyNews.NewsList.Any())
                {
                    ChartObjects.DrawVerticalLine("Pointer1" + timeId, openTime, Colors.SlateGray, 1, LineStyle.DotsVeryRare);
                    ChartObjects.DrawText("News2" + timeId, FormatForDisplay(newsGroup.QuoteCurrencyNews.NewsList), index, low - padding * pips, VerticalAlignment.Bottom, HorizontalAlignment.Right, textColor);
                }
            }
        }

        private string FormatForDisplay(List<NewsItem> items)
        {
            string[] events = items.Select(news => news.Event).ToArray();
            return string.Join(Environment.NewLine, events);
        }

        /// <summary>
        /// Returns current TimeFrame in minutes
        /// </summary>
        private int TimeFrameInMinutes
        {
            get
            {
                if (TimeFrame == TimeFrame.Minute)
                    return 1;
                if (TimeFrame == TimeFrame.Minute2)
                    return 2;
                if (TimeFrame == TimeFrame.Minute3)
                    return 3;
                if (TimeFrame == TimeFrame.Minute4)
                    return 4;
                if (TimeFrame == TimeFrame.Minute5)
                    return 5;
                if (TimeFrame == TimeFrame.Minute10)
                    return 10;
                if (TimeFrame == TimeFrame.Minute15)
                    return 15;
                if (TimeFrame == TimeFrame.Minute30)
                    return 30;
                if (TimeFrame == TimeFrame.Hour)
                    return 60;
                if (TimeFrame == TimeFrame.Hour4)
                    return 60 * 4;
                if (TimeFrame == TimeFrame.Hour12)
                    return 60 * 12;
                if (TimeFrame == TimeFrame.Daily)
                    return 24 * 60;
                return 0;
            }
        }

        public void Log(string message, params object[] parameters)
        {
            Print(message, parameters);
        }

        public void Log(object value)
        {
            Print(value);
        }



    }

    public class DailyFxDownloader
    {
        private readonly ILogger _logger;

        //sample url: "http://www.dailyfx.com/files/Calendar-01-06-2013.csv;

        const string urlBase = "http://www.dailyfx.com/files/";
        const string urlFromat = "Calendar-{0:D2}-{1:D2}-{2}.csv";

        public DailyFxDownloader(ILogger logger)
        {
            _logger = logger;
        }

        public List<NewsItem> Download(int lookBack)
        {
            var date = DateTime.Now;

            var result = new List<NewsItem>();

            int calendarsLoaded = 0;

            //DailyFx publishes data every Sunday

            //if today is Saturday - load calendar for the next week

            bool isMostRecent = true;

            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                var newsItems = DownloadAndParse(date.AddDays(1), !isMostRecent);
                result.AddRange(newsItems);
                calendarsLoaded++;
                isMostRecent = false;
            }

            //find last Sunday
            while (true)
            {
                if (date.DayOfWeek == DayOfWeek.Sunday)
                {
                    var newsItems = DownloadAndParse(date, !isMostRecent);
                    result.AddRange(newsItems);
                    calendarsLoaded++;

                    isMostRecent = false;

                    if (calendarsLoaded >= lookBack)
                        break;
                }
                date = date.AddDays(-1);
            }

            return result;

        }

        private List<NewsItem> DownloadAndParse(DateTime date, bool useCache)
        {
            var tmpFolder = Path.GetTempPath();

            using (var wc = new WebClient())
            {
                var fileName = string.Format(urlFromat, date.Month, date.Day, date.Year);
                var tmpFileNamePath = Path.Combine(tmpFolder, fileName);

                string csvData;
                //caching
                if (useCache && File.Exists(tmpFileNamePath))
                {
                    csvData = File.ReadAllText(tmpFileNamePath);
                    _logger.Log("Reading {0} from tmp folder", tmpFileNamePath);
                }
                else
                {
                    string urlAddress = urlBase + fileName;

                    _logger.Log("Downloading {0}", urlAddress);
                    //download CSV 
                    csvData = wc.DownloadString(urlAddress);

                    File.WriteAllText(tmpFileNamePath, csvData);
                }

                //parse data
                List<NewsItem> newsItems = ParseDailyFxCsv(date, csvData);

                _logger.Log("{0} items loaded", newsItems.Count());
                return newsItems;
            }
        }

        /// <summary>
        /// Parses DailyFx csv data
        /// </summary>
        /// <param name="fileDate"></param>
        /// <param name="csvData"></param>
        /// <returns></returns>
        private List<NewsItem> ParseDailyFxCsv(DateTime fileDate, string csvData)
        {
            var list = new List<NewsItem>();

            using (var reader = new StringReader(csvData))
            {
                using (var fields = new CsvReader(reader, true))
                {
                    while (fields.ReadNextRecord())
                    {
                        var newsItem = new NewsItem();

                        int i = 0;
                        var dateStr = fields[i++];
                        var timeStr = fields[i++];

                        newsItem.UtcDateTime = GetDateTime(fileDate, dateStr, timeStr);
                        newsItem.TimeZone = fields[i++];
                        newsItem.Currency = fields[i++].ToUpper();
                        var newsEvent = fields[i++];
                        //if event start with currency - remove it
                        if (newsEvent.StartsWith(newsItem.Currency + " "))
                        {
                            newsEvent = newsEvent.Substring(4);
                        }

                        newsItem.Event = newsEvent;

                        //parse importance
                        var importance = fields[i++].ToLower();
                        newsItem.Importance = ParsingUtil.ParseImportance(importance);

                        newsItem.Actual = fields[i++];
                        newsItem.Forecast = fields[i++];
                        newsItem.Previous = fields[i++];
                        list.Add(newsItem);
                    }
                }
            }

            return list;
        }

        private DateTime GetDateTime(DateTime fileDate, string dateStr, string timeStr)
        {
            try
            {
                var dateTab = dateStr.Split(' ');

                //sample date - Sun Nov 3
                var month = DateTime.ParseExact(dateTab[1], "MMM", null).Month;
                var day = int.Parse(dateTab[2]);

                var year = fileDate.Year;
                var fileMonth = fileDate.Month;

                //week started in December contains data for January following year
                if (month == 1 && fileMonth == 12)
                    year++;

                var result = new DateTime(year, month, day);

                if (!string.IsNullOrEmpty(timeStr))
                {
                    var timeTab = timeStr.Split(':');
                    var hour = int.Parse(timeTab[0]);
                    var min = int.Parse(timeTab[1]);

                    result = result.Date.AddHours(hour).AddMinutes(min);
                }

                return result;
            } catch (Exception e)
            {
                _logger.Log("Error parsing datetime {0} {1} {2}", fileDate, dateStr, timeStr);
                _logger.Log(e.Message);
                throw;
            }
        }
    }


    public class ParsingUtil
    {
        public static Importance? ParseImportance(string importance)
        {
            if (importance == null)
                return null;

            importance = importance.ToLower();

            if (importance == "high")
                return Importance.High;

            if (importance == "low")
                return Importance.Low;

            if (importance == "medium")
                return Importance.Medium;

            return null;
        }
    }

    public class NewsRepository
    {
        public static List<T> FilterNews<T>(List<T> newsItems, bool showLow, bool showMedium, bool showHigh, bool symbolFilter, SymbolWrapper symbol) where T : INewsItem
        {
            //improtance filter               
            var importanceFilter = new List<Importance>();
            if (showLow)
                importanceFilter.Add(Importance.Low);

            if (showMedium)
                importanceFilter.Add(Importance.Medium);

            if (showHigh)
                importanceFilter.Add(Importance.High);

            newsItems = newsItems.Where(x => x.Importance.HasValue && importanceFilter.Contains(x.Importance.Value)).OrderBy(x => x.UtcDateTime).ToList();

            if (symbolFilter)
            {
                //show events only applicable to this currency
                var currencyFilter = new[] 
                {
                    symbol.BaseCurrency,
                    symbol.QuoteCurrency
                };

                newsItems = newsItems.Where(x => currencyFilter.Contains(x.Currency)).ToList();
            }

            return newsItems;
        }

        public static List<NewsGroup<T>> GroupNews<T>(List<T> newsList, SymbolWrapper symbol) where T : INewsItem
        {
            var groups = newsList.GroupBy(x => x.UtcDateTime).Select(x => new NewsGroup<T> 
            {
                Time = x.Key,
                BaseCurrencyNews = new CurrencyNews<T> 
                {
                    NewsList = x.Where(y => y.Currency == symbol.BaseCurrency).ToList(),
                    Currency = symbol.BaseCurrency,
                    Time = x.Key
                },
                QuoteCurrencyNews = new CurrencyNews<T> 
                {
                    NewsList = x.Where(y => y.Currency == symbol.QuoteCurrency).ToList(),
                    Currency = symbol.QuoteCurrency,
                    Time = x.Key
                }
            }).ToList();
            return groups;
        }
    }

    public interface INewsItem
    {
        string Event { get; }
        string Currency { get; }
        DateTime UtcDateTime { get; }
        Importance? Importance { get; }
    }

    public class NewsItem : INewsItem
    {
        public DateTime UtcDateTime { get; set; }
        public string TimeZone { get; set; }
        public string Currency { get; set; }
        public string Event { get; set; }
        public Importance? Importance { get; set; }
        public string Actual { get; set; }
        public string Forecast { get; set; }
        public string Previous { get; set; }
    }

    public class NewsGroup<T> where T : INewsItem
    {
        public DateTime Time { get; set; }

        /// <summary>
        /// News for base currency
        /// </summary>
        public CurrencyNews<T> BaseCurrencyNews { get; set; }

        /// <summary>
        /// News for quote currency
        /// </summary>
        public CurrencyNews<T> QuoteCurrencyNews { get; set; }
    }

    public class CurrencyNews<T> where T : INewsItem
    {
        public DateTime Time { get; set; }
        public string Currency { get; set; }
        public List<T> NewsList { get; set; }
    }

    public class SymbolWrapper
    {
        public string BaseCurrency { get; private set; }
        public string QuoteCurrency { get; private set; }

        public SymbolWrapper(string code)
        {
            BaseCurrency = code.Substring(0, 3);
            QuoteCurrency = code.Substring(3, 3);
        }
    }

    public enum Importance
    {
        Low,
        Medium,
        High
    }

    public interface ILogger
    {
        void Log(string message, params object[] parameters);
        void Log(object value);
    }

}
