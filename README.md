# News Robot Enhanced (NRE)

André Karlsson   <andre@sess.se>
Version: Production

Since the Algo is to big to be uploaded here(?) you have to download it from github. You also have to install the CSV-reader prior to running the cbot. CSV-redaer can be found @ 

Download@ GitHub (Algo with source) (Direct link to repository is: https://github.com/joyider/nre )

Updates:

Version 0.9.01b - 2018-02-02

New Feature: Disable trades and only show News(in localtime) on chart

Fixes

New CSV source (Forexfactory) parsed to new server
Timezone fix, now woeking in all timezones without manual hack
Fixed a bug that prevented new orders from being placed after previous win

Version: Production
Will place news trades based on data downloaded from forexfactory using a parser that saves the data @

http(s)://edu.tenforward.io/csvs/Calendar-dd-mm-yyy.csv where the date is the first sunday of the week (US Style)

 Place orders based on High/Meedium/Low news importance
 Show historical news events onscreen
 One Cancle Other or One DON'T Cancel Other
 Trailing stop as an option (places SL at half of takeprofit when reached)
This Cbot was originally based on the News Robot Cbot and the News - DailyFx Economic Calendar Indicator

The News - DailyFx Economic Calendar Indicator is how ever broken and does not work anymore.

I've added some trailing stop and Error handeling to manage empty News lists to make it more versatile and to keep it running during weekends.

Due to the nature on ths Cbot you can NOT backtest it. To try it run it a few weeks on a demo account... or monitor you trades manually in the begining.

This robot will place one or two (depening on settings) pending orders (Buy and Sell) based on the next news event, and will only place a new order (based on next news event) when the first is finished.

If you find any bugs/issues, please let me know. Or if you have any ideas on more enhancement set me an email or write a comment.

 

I Recommend you to only use news with HIGH importance in order to take advantage of the volatility.

The Algo file is unfortunately too big to upload here :( so you have to install the CSV reader your self. Source code can be found @:
