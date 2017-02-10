# News Robot Enhanced (NRE)

André Karlsson   <andre@sess.se>
Version: Pre-Production(Working)
Will place news trades based on data downloaded from dailyFX.com
 Place orders based on High/Meedium/Low news importance
 Show historical news events onscreen
 One Cancle Other or One DON'T Cancel Other
 Trailing stop as an option (places SL at half of takeprofit when reached)

This Cbot Isbased on the News Robot Cbot and the News - DailyFx Economic Calendar Indicator

I've added some trailing stop and Error handeling to manage empty News lists to make it more versatile and to keep it running during weekends.

Due to the nature on ths Cbot you can NOT backtest it. To try it run it a few weeks on a demo account... or monitor you trades manually in the begining.

This robot will place one or two (depening on settings) pending orders (Buy and Sell) based on the next news event, and will only place a new order (based on next news event) when the first is finished.

If you find any bugs/issues, please let me know. Or if you have any ideas on more enhancement set me an email or write a comment.


I Recommend you to only use news with HIGH importance in order to take advantage of the volatility.


