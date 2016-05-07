using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Web;

namespace TweetCrawler
{
    public class Crawler
    {
        private readonly string SearchWord = ConfigurationManager.AppSettings["SearchWord"];
        private readonly DateTime SinceDate = DateTime.Parse(ConfigurationManager.AppSettings["SinceDate"]);
        private readonly DateTime UntilDate = DateTime.Parse(ConfigurationManager.AppSettings["UntilDate"]);
        private readonly int DateStep = int.Parse(ConfigurationManager.AppSettings["DateStep"]);

        private IWebDriver wd;
        private StreamWriter logWriter;
        private StreamWriter dataWriter;

        public Crawler()
        {
        }

        public void Start()
        {
            using (logWriter = new StreamWriter(DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log"))
            using (dataWriter = new StreamWriter(DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt"))
            using (wd = new ChromeDriver())
            {
                try
                {
                    var comment = JsonConvert.SerializeObject(new
                    {
                        word = SearchWord,
                        since = SinceDate,
                        until = UntilDate,
                    });

                    dataWriter.WriteLine(comment);
                    logWriter.WriteLine(comment);

                    for (var date = SinceDate; date < UntilDate; date = date.AddDays(DateStep))
                    {
                        var since = date;
                        var until = since.AddDays(DateStep);

                        var tweets = GetTweets(SearchWord, since, until);
                        foreach (var tweet in tweets)
                        {
                            SaveTweet(tweet);
                        }
                    }
                }
                catch (Exception e)
                {
                    logWriter.WriteLine(e.GetType());
                    logWriter.WriteLine(e.Message);
                    logWriter.WriteLine(e.StackTrace);

                    Console.WriteLine(e.GetType());
                    Console.WriteLine(e.Message);
                    Console.WriteLine(e.StackTrace);
                }

                logWriter.WriteLine("end");

                Console.WriteLine("press enter");
                Console.ReadLine();
            }
        }

        private void ScrollToEnd()
        {
            wd.FindElements(By.CssSelector("body")).First().SendKeys(Keys.End);
        }

        private bool HasMoreItems()
        {
            return wd.FindElements(By.CssSelector(".has-more-items")).Count == 1;
        }

        private bool IsNotFoundDisplayed()
        {
            return wd.FindElements(By.CssSelector(".empty-text")).First().Displayed;
        }

        private bool IsErrorDisplayed()
        {
            return wd.FindElements(By.CssSelector(".stream-fail-container")).First().Displayed;
        }

        private IList<IWebElement> GetTweets(string word, DateTime since, DateTime until)
        {
            var encodedWord = HttpUtility.UrlEncode(word);
            var formattedSince = since.ToString("yyyy-MM-dd");
            var formattedUntil = until.ToString("yyyy-MM-dd");

            Debug.WriteLine($"word: {encodedWord}, since: {formattedSince}, until: {formattedUntil}");

            var comment = JsonConvert.SerializeObject(new
            {
                since = formattedSince,
                until = formattedUntil,
            });

            dataWriter.WriteLine(comment);
            logWriter.WriteLine(comment);

            wd.Navigate().GoToUrl("https://twitter.com/" + $"search?f=tweets&q={encodedWord}%20since%3A{formattedSince}%20until%3A{formattedUntil}&src=typd");

            var timeout = DateTime.Now.AddMinutes(1);

            while (HasMoreItems())
            {
                if (timeout < DateTime.Now)
                {
                    Debug.WriteLine("timeout");
                    logWriter.WriteLine("timeout");
                    return null;
                }

                if (IsNotFoundDisplayed())
                {
                    Debug.WriteLine("tweet not found");
                    logWriter.WriteLine("tweet not found");
                    return null;
                }

                if (IsErrorDisplayed())
                {
                    Debug.WriteLine("error");
                    logWriter.WriteLine("error");
                    return null;
                }

                ScrollToEnd();
            }

            Debug.WriteLine("reach to end");

            return wd.FindElements(By.CssSelector("#stream-items-id > li"));
        }

        private void SaveTweet(IWebElement tweet)
        {
            var createdAt = tweet.FindElements(By.CssSelector("span._timestamp")).First().GetAttribute("data-time-ms");
            Debug.WriteLine("createdAt: " + createdAt);

            var originalTweet = tweet.FindElements(By.CssSelector("div.original-tweet")).First();

            var userScreenName = originalTweet.GetAttribute("data-screen-name");
            Debug.WriteLine("userScreenName: " + userScreenName);

            var tweetId = originalTweet.GetAttribute("data-tweet-id");
            Debug.WriteLine("tweetId: " + tweetId);

            // mediaUrl は expandedUrl とのひも付けが難しそうなので取得しない

            var expandedUrls = new List<string>();

            var links = tweet.FindElements(By.CssSelector("a.twitter-timeline-link"));
            foreach (var link in links)
            {
                if (!string.IsNullOrWhiteSpace(link.GetAttribute("data-expanded-url"))) continue;

                var href = link.GetAttribute("href");
                Debug.WriteLine("href: " + href);

                var request = (HttpWebRequest)WebRequest.Create(href);
                using (var response = request.GetResponse())
                {
                    var expandedUrl = response.ResponseUri.ToString();
                    Debug.WriteLine("expandedUrl: " + expandedUrl);

                    expandedUrls.Add(expandedUrl);
                }
            }

            var json = JsonConvert.SerializeObject(new
            {
                createdAt = createdAt,
                userScreenName = userScreenName,
                tweetId = tweetId,
                mediaUrls = new List<string>(),
                expandedUrls = expandedUrls,
                // registerdTime は db 登録時に指定する
            });

            Debug.WriteLine(json);

            dataWriter.WriteLine(json);
            dataWriter.Flush();
        }
    }
}
